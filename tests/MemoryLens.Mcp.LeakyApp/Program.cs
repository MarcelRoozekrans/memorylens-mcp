using System.Globalization;

// A process that leaks on purpose, in shapes the ML rules detect.
// Driven over stdin so a test can make it grow deterministically -- no sleeps,
// no wall-clock races.

// ML002: a static collection that only ever grows.
var retained = new List<object>();

// ML010: many duplicate strings that could be interned.
var duplicates = new List<string>();

// ML003 / ML009: disposables that are never disposed.
var streams = new List<MemoryStream>();

void Grow(int tranche)
{
    for (var i = 0; i < 20_000; i++)
        duplicates.Add("memorylens-leak-" + (i % 200).ToString(CultureInfo.InvariantCulture));

    for (var i = 0; i < 500; i++)
        streams.Add(new MemoryStream(new byte[256]));

    // ML007: closures capturing and retaining state.
    for (var i = 0; i < 2_000; i++)
    {
        var captured = tranche * 1000 + i;
        retained.Add(new Func<int>(() => captured));
    }
}

Grow(0);

Console.WriteLine("READY " + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
Console.Out.Flush();

var tranche = 1;
string? line;
while ((line = Console.ReadLine()) is not null)
{
    if (string.Equals(line, "grow", StringComparison.Ordinal))
    {
        Grow(tranche++);
        Console.WriteLine("GROWN");
        Console.Out.Flush();
    }
    else if (string.Equals(line, "exit", StringComparison.Ordinal))
    {
        break;
    }
}

// Keep everything alive to the very end so the heap still holds it when sampled.
GC.KeepAlive(retained);
GC.KeepAlive(duplicates);
GC.KeepAlive(streams);
