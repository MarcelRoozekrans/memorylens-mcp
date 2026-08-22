using System.Diagnostics.Tracing;
using MemoryLens.Mcp.Analysis;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace MemoryLens.Mcp.Profiler;

public interface IHeapCollector
{
    Task<SnapshotData> CollectAsync(int pid, CancellationToken ct);
}

/// <summary>Raised when a heap collection cannot produce a usable snapshot.</summary>
public sealed class HeapCollectionException : Exception
{
    public HeapCollectionException(string message) : base(message) { }
    public HeapCollectionException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Collects a per-type heap summary from a live .NET process over EventPipe.
/// No external tool, no text parsing.
/// </summary>
public sealed class HeapCollector(TimeSpan? timeout = null) : IHeapCollector
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(30);

    // Matches dotnet-gcdump. Measured on .NET 10, buffer size is NOT what makes
    // events stream live -- sweeping 10MB..1024MB changes nothing. It is sized to
    // match the reference implementation and to hold a large real heap.
    private const int CircularBufferMb = 1024;

    // How long teardown may spend draining the event stream after the session is
    // stopped. Bounded so teardown can never hang the caller.
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);

    // How long stopping the session may take before we give up on it. Stopping runs on
    // every exit path, caller cancellation included, and nothing above this class bounds
    // it -- an unbounded wait here is a wedged diagnostic endpoint hanging `snapshot`
    // forever.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    // Ceiling on the node addresses buffered while bucketing generations (the note at
    // the declaration of `addresses` below explains why they have to be buffered at
    // all). 32M addresses is 256MB of ulong, the same order as the circular buffer
    // above, and covers any heap this tool is realistically pointed at: 32M live
    // objects is a 1.5-3GB live set at typical object sizes.
    //
    // Past it the collection THROWS rather than truncating. The heap walk emits nodes
    // in address order, so a truncated buffer is a spatially biased sample, and a
    // biased sample yields a confidently WRONG dominant generation rather than a merely
    // approximate one. Everything else in this file refuses rather than degrades --
    // drain timeout, EventsLost, incomplete GC, empty snapshot -- and an unbounded
    // buffer inside a long-lived MCP server would have been the one place where a large
    // heap damaged the host instead of producing a loud failure.
    private const int MaxBufferedAddresses = 32_000_000;

    public async Task<SnapshotData> CollectAsync(int pid, CancellationToken ct)
    {
        var providers = new[]
        {
            new EventPipeProvider(
                "Microsoft-Windows-DotNETRuntime",
                EventLevel.Verbose,
                // GCHeapSurvivalAndMovement is what publishes GCGenerationRange, which is
                // the only way to turn a node's address into a generation. Measured:
                // without it GCGenerationRange fires exactly ZERO times while tens of
                // thousands of nodes stream in carrying addresses.
                (long)(ClrTraceEventParser.Keywords.GCHeapSnapshot
                     | ClrTraceEventParser.Keywords.GCHeapSurvivalAndMovement)),

            // This is what makes events dispatch live, and it is what dotnet-gcdump
            // does. The heap dump itself finishes ~1ms after the session starts, but
            // EventPipe will not release buffered events until it can prove it has
            // seen everything up to a given timestamp. A target sitting idle never
            // advances that watermark, so without this provider nothing arrives until
            // the session is torn down and completion detection can never fire.
            // With it, collection completes in ~200ms instead of the full timeout.
            new EventPipeProvider(
                "Microsoft-DotNETCore-SampleProfiler",
                EventLevel.Informational),
        };

        EventPipeSession session;
        try
        {
            session = new DiagnosticsClient(pid)
                .StartEventPipeSession(providers, requestRundown: false, circularBufferMB: CircularBufferMb);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new HeapCollectionException(
                $"Could not attach to process {pid}. It may have exited, may not be a .NET process, " +
                $"or the current user may lack permission to open its diagnostic endpoint.", ex);
        }

        using (session)
        {
            var typeNames = new Dictionary<ulong, string>();
            var counts = new Dictionary<ulong, int>();
            var bytes = new Dictionary<ulong, long>();

            // Generation state. Same ownership rule as the three dictionaries above:
            // written ONLY on the pump thread, read ONLY where the pump has provably
            // finished (the Build call at the bottom of this method). The abandoned-pump
            // path throws before reaching either of them.
            //
            // Node addresses are buffered rather than bucketed as they arrive, and that
            // is a measurement result, not a preference. GCGenerationRange fires in two
            // bursts -- once at GC start and once at GC end -- and the GCBulkNode events
            // land BETWEEN them (measured: ranges at dispatch index 0..4 and 22..26,
            // nodes at 8..20). Bucketing on arrival could therefore only ever see the
            // pre-GC layout, which labels every survivor with the generation it was in
            // before the collection promoted it. The post-GC burst is the layout the
            // dumped addresses actually belong to, because the heap walk runs at the end
            // of the GC, after relocation. Buffering costs 8 bytes per live object, is
            // capped at MaxBufferedAddresses, and buys the correct answer.
            var addresses = new Dictionary<ulong, List<ulong>>();
            var bufferedAddresses = 0;

            // Only the ranges that arrived after the LAST node event survive in here --
            // see the GCGenerationRange handler for why.
            var ranges = new List<(int Generation, ulong Start, ulong End)>();

            // Pump-thread only. Set by every node event and cleared by the next range
            // event, which is what lets the range handler recognise the start of a burst
            // that follows nodes.
            var nodesSinceLastRange = false;

            // Written on the pump thread when the address budget is exhausted, read on
            // the caller thread beside the EventsLost guard. A flag rather than a throw
            // from inside the handler on purpose: an exception unwinding through
            // TraceEvent's dispatch loop is a far less understood path than the refusal
            // machinery this method already has.
            var addressBudgetExceeded = 0;

            // RunContinuationsAsynchronously is load-bearing, not a style choice.
            // TrySetResult below is called from inside an EventPipe callback on the
            // pump thread, with source.Process() on the stack. With the default
            // (synchronous) continuation mode the awaiter downstream resumes inline
            // on that same pump thread, so session.Stop() ends up being called from
            // inside the event dispatch loop -- and Stop() waits for the stream to
            // drain, which only the pump thread can do. That deadlocks hard: measured
            // 60s+ with no upper bound. Resuming on the pool keeps Stop() at ~100ms.
            var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            long gcNum = -1;

            // Written on the pump thread, read on the caller's. The read below is
            // already fenced by awaiting the pump, but this is a genuine cross-thread
            // read and is cheap to make explicit. (A local cannot be declared
            // volatile, hence Volatile.Read/Write on the captured field.)
            var sawAnyEvent = 0;

            // Hoisted out of the pump so EventsLost is readable once the pump is done.
            // Assigned on the pump thread; read ONLY on the path where the pump has
            // genuinely finished (awaited below), which is what establishes
            // happens-before. Never touched on the abandoned path -- there the pump is
            // still running and this would be a live race.
            EventPipeEventSource? source = null;

            var pump = Task.Run(() =>
            {
                using var eventSource = new EventPipeEventSource(session.EventStream);
                source = eventSource;

                eventSource.Clr.TypeBulkType += e =>
                {
                    Volatile.Write(ref sawAnyEvent, 1);
                    for (var i = 0; i < e.Count; i++)
                    {
                        var v = e.Values(i);
                        typeNames[v.TypeID] = v.TypeName;
                    }
                };

                eventSource.Clr.GCBulkNode += e =>
                {
                    Volatile.Write(ref sawAnyEvent, 1);
                    for (var i = 0; i < e.Count; i++)
                    {
                        var n = e.Values(i);
                        counts.TryGetValue(n.TypeID, out var c);
                        counts[n.TypeID] = c + 1;
                        bytes.TryGetValue(n.TypeID, out var b);
                        bytes[n.TypeID] = b + (long)n.Size;

                        if (bufferedAddresses >= MaxBufferedAddresses)
                        {
                            // Stop appending -- but do not quietly carry on. A truncated
                            // buffer would bias every mode computed from here on, so the
                            // caller-side guard turns this flag into a refusal.
                            Volatile.Write(ref addressBudgetExceeded, 1);
                            continue;
                        }

                        if (!addresses.TryGetValue(n.TypeID, out var addrs))
                            addresses[n.TypeID] = addrs = [];
                        addrs.Add(n.Address);
                        bufferedAddresses++;
                    }

                    nodesSinceLastRange = true;
                };

                // Deliberately does NOT set sawAnyEvent. That flag means "heap data
                // arrived"; a trace carrying generation ranges but no nodes is still an
                // empty heap and must still be refused below.
                eventSource.Clr.GCGenerationRange += e =>
                {
                    // Keep ONLY the ranges that arrived after the last node event.
                    //
                    // The addresses on GCBulkNode come from the end-of-GC heap walk, so
                    // they live in the POST-GC layout. A region the post-GC burst does
                    // not claim is not part of that layout, and labelling an object
                    // there from the pre-GC burst is a guess -- exactly what an
                    // unmappable node must not get. Dropping the earlier burst the
                    // moment a later one starts leaves those addresses at -1, which is
                    // the honest answer.
                    //
                    // It also means a third burst -- a concurrent background GC
                    // publishing its own ranges -- replaces its predecessor rather than
                    // being merged into it.
                    //
                    // No timestamps are consulted: the reset is driven purely by having
                    // seen a node since the previous range event, so what survives is
                    // precisely the last group of ranges with no node between them.
                    if (nodesSinceLastRange)
                    {
                        ranges.Clear();
                        nodesSinceLastRange = false;
                    }

                    ranges.Add((e.Generation, e.RangeStart, e.RangeStart + e.RangeUsedLength));
                };

                // Completion, as dotnet-gcdump does it: remember the induced GC,
                // finish when that same GC stops.
                eventSource.Clr.GCStart += (GCStartTraceData d) =>
                {
                    Volatile.Write(ref sawAnyEvent, 1);
                    if (gcNum < 0 && d.Depth == 2 && d.Type != GCType.BackgroundGC)
                        gcNum = d.Count;
                };

                eventSource.Clr.GCStop += (GCEndTraceData d) =>
                {
                    if (gcNum >= 0 && d.Count == gcNum)
                        complete.TrySetResult();
                };

                eventSource.Process();
            }, CancellationToken.None);

            using var overall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            overall.CancelAfter(_timeout);

            try
            {
                await complete.Task.WaitAsync(overall.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timed out waiting for the induced GC to finish. Fall through so the
                // finally still stops the session and the pump is still drained and
                // observed -- but the completion check below will refuse to return
                // whatever partial heap that produced. See the note there.
            }
            finally
            {
                // Stopping happens on EVERY exit path, caller cancellation included.
                // If this only ran on the success path, a cancelled caller would leave
                // teardown to session.Dispose(), which can block unbounded on the
                // caller's thread.
                //
                // dotnet-gcdump calls EndSession() here, but that is a method on its own
                // EventPipeSessionController wrapper which forwards to Stop();
                // EventPipeSession itself exposes no EndSession in this package. The
                // enclosing `using (session)` performs the Dispose half of that pattern.
                // StopAsync rather than Stop so we do not block a pool thread -- the one
                // this continuation runs on is rooted in the CancelAfter timer callback.
                //
                // Bounded, because this runs on EVERY exit path including caller
                // cancellation: a wedged diagnostic endpoint would otherwise hang
                // `snapshot` forever, and there is nothing above this call to bound it.
                try
                {
                    using var stop = new CancellationTokenSource(StopTimeout);
                    await session.StopAsync(stop.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Stopping a dead session is not an error, and neither is giving up
                    // on a wedged one -- the drain below is what decides whether we have
                    // an honest snapshot to return.
                }
            }

            // Draining is bounded: never let teardown hang the caller. WaitAsync's timer
            // is torn down when the wait resolves, so no timer or Task is left live.
            // Awaiting the pump here is also what establishes happens-before for the
            // dictionaries below, and what marks a pump fault observed.
            var pumpFinished = true;
            try
            {
                await pump.WaitAsync(DrainTimeout, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                pumpFinished = false;
            }
            catch (Exception)
            {
                // The pump faulted. Surfaced just below from pump.Exception so the real
                // root cause reaches the caller; observed by having awaited it here.
            }

            ct.ThrowIfCancellationRequested();

            // A pump fault is the root cause; everything below it is a symptom. This is
            // reachable in normal operation: disposing the session closes EventStream
            // under an actively-reading pump. Without this the exception would be lost
            // to UnobservedTaskException and the caller would get a misleading
            // "no data arrived" after burning the whole timeout.
            if (pump.IsFaulted)
            {
                var fault = pump.Exception is { InnerExceptions.Count: 1 } single
                    ? single.InnerExceptions[0]
                    : pump.Exception!;

                throw new HeapCollectionException(
                    $"Reading the EventPipe stream from process {pid} failed: {fault.Message}", fault);
            }

            // The pump is STILL RUNNING and still writing to typeNames/counts/bytes and
            // to the addresses/ranges collections.
            // Reading them from this thread would be an unsynchronised race against
            // Dictionary insert and resize: a reader racing a resize can throw, read a
            // torn entry, or spin forever in bucket traversal -- an unbounded hang that
            // no timeout in this method covers, reachable precisely when the heap is
            // large. So do not touch them at all on this path. A loud failure beats a
            // silent undercount for a leak-finding tool.
            if (!pumpFinished)
            {
                // We are abandoning the pump. Disposing the session closes EventStream
                // under it, so it will very likely fault -- observe that fault here or
                // it resurfaces as an UnobservedTaskException on the finalizer thread,
                // in an unrelated part of the process.
                _ = pump.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);

                throw new HeapCollectionException(
                    $"Heap collection from process {pid} did not complete: the event stream was " +
                    $"still being read {DrainTimeout.TotalSeconds:N0}s after the session was stopped. " +
                    $"Refusing to report a snapshot assembled from a partially written heap.");
            }

            if (Volatile.Read(ref sawAnyEvent) == 0)
                throw new HeapCollectionException(
                    $"No EventPipe data arrived from process {pid} within {_timeout.TotalSeconds:N0}s. " +
                    $"The process may not be a .NET process, or may have exited during collection.");

            // The EventPipe buffer is CIRCULAR. On a multi-GB heap it can wrap and drop
            // node events, and nothing else in this method would notice: Build() would
            // simply sum the events that survived and hand back a confident undercount
            // with no diagnostic -- the original bug's signature exactly. Safe to read
            // here and only here: the pump has finished, which fences the write.
            var eventsLost = source?.EventsLost ?? 0;

            if (eventsLost != 0)
                throw new HeapCollectionException(
                    $"EventPipe dropped {eventsLost} event(s) while collecting from process {pid}. " +
                    $"The circular buffer wrapped, so an unknown number of heap nodes never arrived. " +
                    $"Refusing to report a snapshot that would silently under-count the heap.");

            // Same shape and same reason as the guard above. Safe to read here and only
            // here: the pump has finished, which fences the write.
            if (Volatile.Read(ref addressBudgetExceeded) != 0)
                throw new HeapCollectionException(
                    $"Heap collection from process {pid} exceeded the {MaxBufferedAddresses:N0} " +
                    $"object budget for generation bucketing. The heap walk emits nodes in " +
                    $"address order, so carrying on with a truncated address buffer would report " +
                    $"a confidently wrong dominant generation for every type beyond the budget. " +
                    $"Refusing to report a snapshot whose generations would be biased.");

            // Did the induced GC actually report GCStop? Checked AFTER the drain, so a
            // completion that landed between the timeout and the drain still counts.
            //
            // If it never fired, Stop() flushed a MID-WALK node stream: Build() sums only
            // what arrived, and the result is internally consistent and indistinguishable
            // from a complete snapshot. That is a silent undercount on exactly the big
            // leaking heaps this tool exists for, so it gets the same treatment as the
            // drain timeout above -- throw rather than answer.
            if (!complete.Task.IsCompletedSuccessfully)
                throw new HeapCollectionException(
                    $"Heap collection from process {pid} did not complete within " +
                    $"{_timeout.TotalSeconds:N0}s: the induced GC never reported completion, so the " +
                    $"heap walk was still in progress when the session was stopped. Refusing to " +
                    $"report a partially collected heap.");

            // Reading addresses/ranges here and only here is what keeps them safe: this
            // point is past every abandoned-pump exit above, so the pump has finished and
            // its writes are fenced by the await on it.
            var data = Build(typeNames, counts, bytes, addresses, ranges);

            // An empty heap is never a real answer. Returning one is how a broken
            // pipeline renders as "no memory issues found" -- see issue #161.
            // TotalBytes is checked too: a snapshot with rows but zero bytes is just
            // as useless downstream, and the tests already demand TotalBytes > 0, so
            // without it the product would enforce less than its own tests expect.
            if (data.Types.Count == 0 || data.Heap.TotalBytes <= 0)
                throw new HeapCollectionException(
                    $"Heap collection from process {pid} produced no objects ({data.Types.Count} types, " +
                    $"{data.Heap.TotalBytes} bytes). Refusing to report an empty snapshot.");

            return data;
        }
    }

    /// <summary>
    /// Per-name accumulator. A class, not a tuple, because the generation histogram is
    /// merged across every type id sharing a name and copying it per update would be
    /// both wasteful and easy to get wrong.
    /// </summary>
    private sealed class TypeAccumulator
    {
        public int Count;
        public long Bytes;

        /// <summary>generation -> instances of this name observed in it.</summary>
        public Dictionary<int, int> Generations { get; } = [];
    }

    private static SnapshotData Build(
        Dictionary<ulong, string> typeNames,
        Dictionary<ulong, int> counts,
        Dictionary<ulong, long> bytes,
        Dictionary<ulong, List<ulong>> addresses,
        List<(int Generation, ulong Start, ulong End)> ranges)
    {
        var generations = GenerationMap.Build(ranges);

        // Aggregate by resolved type NAME, not by type id. The CLR can report the
        // same name under more than one type id within a single dump; keying on id
        // would emit duplicate FullName rows and split a leaking type's counts
        // across them, which under-reports it -- the exact silent under-reporting
        // this collector exists to eliminate. Generation histograms are merged the
        // same way, so the mode is taken over the whole name, not per id.
        var byName = new Dictionary<string, TypeAccumulator>(StringComparer.Ordinal);

        foreach (var (typeId, count) in counts)
        {
            // A node whose type id never resolved to a name is dropped, and its bytes
            // do not appear in Heap.TotalBytes. There is deliberately no accounting row
            // for it: SnapshotData has no field to carry one, and inventing a synthetic
            // type would put a fabricated row in front of the rules. The lossy case this
            // guards against -- TypeBulkType events going missing wholesale -- is now
            // caught upstream by the EventsLost check in CollectAsync, which refuses the
            // whole snapshot rather than letting it render as a quiet undercount.
            if (!typeNames.TryGetValue(typeId, out var name) || string.IsNullOrEmpty(name))
                continue;

            var size = bytes.TryGetValue(typeId, out var b) ? b : 0;

            if (!byName.TryGetValue(name, out var acc))
                byName[name] = acc = new TypeAccumulator();

            acc.Count += count;
            acc.Bytes += size;

            if (!addresses.TryGetValue(typeId, out var addrs))
                continue;

            foreach (var address in addrs)
            {
                // An address inside no reported range keeps no generation at all.
                // Measured at roughly 2% of a live heap; guessing at those would put
                // fabricated generations in front of the rules.
                var gen = generations.Find(address);
                if (gen < 0)
                    continue;

                acc.Generations.TryGetValue(gen, out var seen);
                acc.Generations[gen] = seen + 1;
            }
        }

        var types = new List<TypeInfo>(byName.Count);
        long lohBytes = 0;
        var lohCount = 0;

        foreach (var (name, acc) in byName)
        {
            var count = acc.Count;
            var size = acc.Bytes;
            var avg = count > 0 ? size / count : 0;
            var isLoh = avg >= 85_000;

            if (isLoh)
            {
                lohBytes += size;
                lohCount += count;
            }

            types.Add(new TypeInfo
            {
                FullName = name,
                InstanceCount = count,
                TotalBytes = size,
                IsLargeObjectHeap = isLoh,
                ImplementsIDisposable = TypeClassifier.IsLikelyDisposable(name),
                HasFinalizer = TypeClassifier.IsLikelyFinalizable(name),
                DominantGeneration = DominantGeneration(acc.Generations),
            });
        }

        return new SnapshotData
        {
            Types = types,
            Heap = new HeapInfo
            {
                TotalBytes = types.Sum(t => t.TotalBytes),
                LargeObjectHeapBytes = lohBytes,
                LargeObjectCount = lohCount,
            },
        };
    }

    /// <summary>
    /// The generation holding the most instances, or -1 when none of them mapped to a
    /// reported range. Ties go to the LOWER generation, so a type split evenly between
    /// two generations can never be talked up into looking long-lived.
    /// </summary>
    private static int DominantGeneration(Dictionary<int, int> histogram)
    {
        var best = -1;
        var bestCount = 0;

        foreach (var (generation, count) in histogram)
        {
            if (count > bestCount || (count == bestCount && best >= 0 && generation < best))
            {
                best = generation;
                bestCount = count;
            }
        }

        return best;
    }

    /// <summary>
    /// Address to GC generation, flattened from the GCGenerationRange events into
    /// disjoint sorted intervals so a lookup is a binary search rather than a scan over
    /// every reported range. That matters on server GC, where the runtime reports a
    /// range per generation per heap per burst -- dozens to hundreds of ranges against
    /// millions of nodes.
    /// </summary>
    private sealed class GenerationMap
    {
        private readonly ulong[] _starts;
        private readonly ulong[] _ends;
        private readonly int[] _generations;

        private GenerationMap(ulong[] starts, ulong[] ends, int[] generations)
        {
            _starts = starts;
            _ends = ends;
            _generations = generations;
        }

        public static GenerationMap Build(List<(int Generation, ulong Start, ulong End)> ranges)
        {
            // Cut the address space at every reported boundary, then label each resulting
            // slice with the LAST range that covers it.
            //
            // By the time this runs, `ranges` holds a single burst -- the collector drops
            // everything published before the last node event -- so the ranges are
            // already disjoint and last-wins is only a deterministic tie-break for a
            // runtime that overlapped two ranges inside one burst. Nothing here reaches
            // back to an earlier burst to fill a gap; an address the surviving burst does
            // not cover gets -1, not a guess.
            var points = new List<ulong>(ranges.Count * 2);
            foreach (var (_, start, end) in ranges)
            {
                if (end <= start)
                    continue; // Empty regions are reported routinely and match nothing.

                points.Add(start);
                points.Add(end);
            }

            points.Sort();

            var starts = new List<ulong>(points.Count);
            var ends = new List<ulong>(points.Count);
            var generations = new List<int>(points.Count);

            for (var i = 0; i + 1 < points.Count; i++)
            {
                var sliceStart = points[i];
                var sliceEnd = points[i + 1];
                if (sliceStart == sliceEnd)
                    continue; // Duplicate boundary; the sort leaves these adjacent.

                var generation = -1;
                for (var j = ranges.Count - 1; j >= 0; j--)
                {
                    var r = ranges[j];
                    if (r.Start <= sliceStart && sliceEnd <= r.End)
                    {
                        generation = r.Generation;
                        break;
                    }
                }

                if (generation < 0)
                    continue; // A gap between ranges. Nothing to say about it.

                // Coalesce so the search array stays small when the bursts agree.
                if (starts.Count > 0 &&
                    ends[^1] == sliceStart &&
                    generations[^1] == generation)
                {
                    ends[^1] = sliceEnd;
                    continue;
                }

                starts.Add(sliceStart);
                ends.Add(sliceEnd);
                generations.Add(generation);
            }

            return new GenerationMap([.. starts], [.. ends], [.. generations]);
        }

        /// <summary>The generation containing <paramref name="address"/>, or -1.</summary>
        public int Find(ulong address)
        {
            var i = Array.BinarySearch(_starts, address);
            if (i < 0)
            {
                i = ~i - 1;
                if (i < 0)
                    return -1;
            }

            return address < _ends[i] ? _generations[i] : -1;
        }
    }
}
