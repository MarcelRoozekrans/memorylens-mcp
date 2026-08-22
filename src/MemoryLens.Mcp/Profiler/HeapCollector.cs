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

    public async Task<SnapshotData> CollectAsync(int pid, CancellationToken ct)
    {
        var providers = new[]
        {
            new EventPipeProvider(
                "Microsoft-Windows-DotNETRuntime",
                EventLevel.Verbose,
                (long)ClrTraceEventParser.Keywords.GCHeapSnapshot),

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
            var sawAnyEvent = false;

            var pump = Task.Run(() =>
            {
                using var source = new EventPipeEventSource(session.EventStream);

                source.Clr.TypeBulkType += e =>
                {
                    sawAnyEvent = true;
                    for (var i = 0; i < e.Count; i++)
                    {
                        var v = e.Values(i);
                        typeNames[v.TypeID] = v.TypeName;
                    }
                };

                source.Clr.GCBulkNode += e =>
                {
                    sawAnyEvent = true;
                    for (var i = 0; i < e.Count; i++)
                    {
                        var n = e.Values(i);
                        counts.TryGetValue(n.TypeID, out var c);
                        counts[n.TypeID] = c + 1;
                        bytes.TryGetValue(n.TypeID, out var b);
                        bytes[n.TypeID] = b + (long)n.Size;
                    }
                };

                // Completion, as dotnet-gcdump does it: remember the induced GC,
                // finish when that same GC stops.
                source.Clr.GCStart += (GCStartTraceData d) =>
                {
                    sawAnyEvent = true;
                    if (gcNum < 0 && d.Depth == 2 && d.Type != GCType.BackgroundGC)
                        gcNum = d.Count;
                };

                source.Clr.GCStop += (GCEndTraceData d) =>
                {
                    if (gcNum >= 0 && d.Count == gcNum)
                        complete.TrySetResult();
                };

                source.Process();
            }, CancellationToken.None);

            using var overall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            overall.CancelAfter(_timeout);

            try
            {
                await complete.Task.WaitAsync(overall.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timed out waiting for completion. Fall through: stop the session and
                // see whether what we collected is usable. If it is not, we throw below.
            }

            // dotnet-gcdump calls EndSession() here, but that is a method on its own
            // EventPipeSessionController wrapper which forwards to session.Stop();
            // EventPipeSession itself exposes no EndSession in this package. The
            // enclosing `using (session)` performs the Dispose half of that pattern.
            try { session.Stop(); } catch (Exception) { /* stopping a dead session is not an error */ }

            // Draining is bounded: never let teardown hang the caller.
            await Task.WhenAny(pump, Task.Delay(TimeSpan.FromSeconds(15), CancellationToken.None)).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            if (!sawAnyEvent)
                throw new HeapCollectionException(
                    $"No EventPipe data arrived from process {pid} within {_timeout.TotalSeconds:N0}s. " +
                    $"The process may not be a .NET process, or may have exited during collection.");

            var data = Build(typeNames, counts, bytes);

            // An empty heap is never a real answer. Returning one is how a broken
            // pipeline renders as "no memory issues found" -- see issue #161.
            if (data.Types.Count == 0)
                throw new HeapCollectionException(
                    $"Heap collection from process {pid} produced no objects. Refusing to report an empty snapshot.");

            return data;
        }
    }

    private static SnapshotData Build(
        Dictionary<ulong, string> typeNames,
        Dictionary<ulong, int> counts,
        Dictionary<ulong, long> bytes)
    {
        // Aggregate by resolved type NAME, not by type id. The CLR can report the
        // same name under more than one type id within a single dump; keying on id
        // would emit duplicate FullName rows and split a leaking type's counts
        // across them, which under-reports it -- the exact silent under-reporting
        // this collector exists to eliminate.
        var byName = new Dictionary<string, (int Count, long Bytes)>(StringComparer.Ordinal);

        foreach (var (typeId, count) in counts)
        {
            if (!typeNames.TryGetValue(typeId, out var name) || string.IsNullOrEmpty(name))
                continue;

            var size = bytes.TryGetValue(typeId, out var b) ? b : 0;
            byName.TryGetValue(name, out var acc);
            byName[name] = (acc.Count + count, acc.Bytes + size);
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
}
