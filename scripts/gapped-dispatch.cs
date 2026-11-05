#:property PublishAot=false
// Times one movement schedule run at a time, with a gap before each run, against a preserved benchmark build.
// Usage: dotnet run --file scripts/gapped-dispatch.cs -- <benchmark output dir> <method> <count> <concurrency> <gap> <runs> [<interleaved method>]
// <gap> is "none", "busy:<microseconds>" (the owner keeps working), or "sleep:<milliseconds>" (the owner blocks).
// An interleaved method runs after the gap and before each timed run, and is timed separately.
using System.Diagnostics;
using System.Reflection;

var directory = Path.GetFullPath(args[0]);
var assembly = Assembly.LoadFrom(Path.Combine(directory, "Archevolt.Benchmarks.dll"));
var type = assembly.GetType("Archevolt.Benchmarks.MovementBenchmarks", throwOnError: true)!;
object workload = Activator.CreateInstance(type)!;
type.GetProperty("Count")!.SetValue(workload, int.Parse(args[2]));
type.GetProperty("Concurrency")!.SetValue(workload, int.Parse(args[3]));
type.GetMethod("Setup")!.Invoke(workload, null);
Action Bind(string name) => (Action)Delegate.CreateDelegate(typeof(Action), workload, type.GetMethod(name)!);
var run = Bind(args[1]);
var interleaved = args.Length > 6 ? Bind(args[6]) : null;
string gap = args[4];
int runs = int.Parse(args[5]);

void Gap()
{
    if (gap.StartsWith("busy:", StringComparison.Ordinal))
    {
        long end = Stopwatch.GetTimestamp() + long.Parse(gap[5..]) * Stopwatch.Frequency / 1_000_000;
        while (Stopwatch.GetTimestamp() < end) { }
    }
    else if (gap.StartsWith("sleep:", StringComparison.Ordinal))
        Thread.Sleep(int.Parse(gap[6..]));
}

double Microseconds(long ticks) => ticks * 1e6 / Stopwatch.Frequency;

var warmup = Stopwatch.StartNew();
while (warmup.ElapsedMilliseconds < 1500) { interleaved?.Invoke(); run(); }
var samples = new double[runs];
var other = new double[runs];
var pairs = new double[runs];
TimeSpan cpuBefore = Environment.CpuUsage.TotalTime;
var wall = Stopwatch.StartNew();
for (int i = 0; i < runs; i++)
{
    Gap();
    long first = Stopwatch.GetTimestamp();
    interleaved?.Invoke();
    long start = Stopwatch.GetTimestamp();
    run();
    long end = Stopwatch.GetTimestamp();
    samples[i] = Microseconds(end - start);
    other[i] = interleaved is null ? 0 : Microseconds(start - first);
    pairs[i] = interleaved is null ? samples[i] : Microseconds(end - first);
}
wall.Stop();
double cpuMs = (Environment.CpuUsage.TotalTime - cpuBefore).TotalMilliseconds;
type.GetMethod("Cleanup")!.Invoke(workload, null);
Array.Sort(samples);
Array.Sort(other);
Array.Sort(pairs);
Console.WriteLine(FormattableString.Invariant(
    $"{args[1]},{args[2]},{args[3]},{gap},{samples[runs / 2]:F3},{samples[runs * 9 / 10]:F3},{samples.Average():F3},{cpuMs / wall.Elapsed.TotalMilliseconds:F3},{other[runs / 2]:F3},{pairs[runs / 2]:F3}"));
