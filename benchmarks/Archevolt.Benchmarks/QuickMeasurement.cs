using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Archevolt.Benchmarks;

internal static class QuickMeasurement
{
    internal static void Run()
    {
        Measure(1);
        Measure(4);
    }

    private static void Measure(int concurrency)
    {
        var workload = new MovementBenchmarks { Count = 100_000, Concurrency = concurrency };
        try
        {
            workload.Setup();
            string[] names = ["handwritten", "generated-each", "generated-query"];
            Action[] methods = [workload.Handwritten, workload.GeneratedEach, workload.GeneratedQuery];
            const int warmup = 32;
            const int repetitions = 64;
            var elapsed = new long[methods.Length];
            var allocations = new long[methods.Length];
            var totalAllocations = new long[methods.Length];

            for (var repeat = 0; repeat < warmup; repeat++)
                for (var method = 0; method < methods.Length; method++)
                    methods[method]();

            for (var repeat = 0; repeat < repetitions; repeat++)
            {
                for (var offset = 0; offset < methods.Length; offset++)
                {
                    var method = (repeat + offset) % methods.Length;
                    var totalBefore = GC.GetTotalAllocatedBytes(precise: true);
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    var start = Stopwatch.GetTimestamp();
                    methods[method]();
                    elapsed[method] += Stopwatch.GetTimestamp() - start;
                    allocations[method] += GC.GetAllocatedBytesForCurrentThread() - before;
                    totalAllocations[method] += GC.GetTotalAllocatedBytes(precise: true) - totalBefore;
                }
            }

            Console.WriteLine($"runtime={RuntimeInformation.FrameworkDescription}");
            Console.WriteLine($"os={RuntimeInformation.OSDescription}");
            Console.WriteLine($"architecture={RuntimeInformation.ProcessArchitecture}");
            Console.WriteLine($"processors={Environment.ProcessorCount}");
            Console.WriteLine($"maxConcurrency={concurrency}");
            Console.WriteLine($"entities={workload.Count};warmup={warmup};repetitions={repetitions}");
            Console.WriteLine("method,ns_per_entity,owner_bytes,total_managed_bytes");
            for (var method = 0; method < methods.Length; method++)
            {
                var nanoseconds = elapsed[method] * (1_000_000_000d / Stopwatch.Frequency) / repetitions / workload.Count;
                Console.WriteLine(FormattableString.Invariant($"{names[method]},{nanoseconds:F3},{allocations[method]},{totalAllocations[method]}"));
            }
        }
        finally
        {
            workload.Cleanup();
        }
    }
}
