using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Archevolt.Benchmarks;

internal readonly record struct PartitionMeasurementCase(string Case, int Rows, int Concurrency, Action Operation);

internal readonly record struct PartitionMeasurementResult(
    string Case, int Rows, int Concurrency,
    double MeanMicroseconds, double MedianMicroseconds, double P95Microseconds, double MaximumMicroseconds,
    long OwnerAllocatedBytes, long AllThreadAllocatedBytes);

internal static class PartitionMeasurement
{
    private const int Warmups = 128;
    private const int Samples = 32;
    private const int OperationsPerSample = 16;
    private const int AllocationOperations = 128;

    internal static void Run()
    {
        Console.WriteLine($"# utc={DateTimeOffset.UtcNow:O}; runtime={RuntimeInformation.FrameworkDescription}; os={RuntimeInformation.OSDescription}; arch={RuntimeInformation.ProcessArchitecture}; logical_cpu={Environment.ProcessorCount}; timer_hz={Stopwatch.Frequency}");
        Console.WriteLine($"# assembly={typeof(PartitionMeasurement).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}");
        Console.WriteLine($"# DOTNET_TieredCompilation={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "<default>"}; DOTNET_ReadyToRun={Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") ?? "<default>"}; DOTNET_TieredPGO={Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "<default>"}");
        Console.WriteLine($"# minimum_warmups_per_case={Warmups}; minimum_warmup_seconds=1; samples={Samples}; operations_per_sample={OperationsPerSample}; allocation_operations_per_case={AllocationOperations}; rotating_case_order=true; allocation_measurement=separate_untimed_batch; delta=1; velocity=1/2/3; one_shared_world_per_rows_and_concurrency=true");
        Console.WriteLine("case,rows,concurrency,mean_us,median_us,p95_us,max_us,owner_bytes_per_op,all_thread_bytes_per_op,owner_bytes_total,all_thread_bytes_total");
        bool allocationFree = true;
        foreach (int rows in new[] { 0, 1, 100, 1_000, 4_096, 16_384, 32_768, 65_536, 100_000, 1_000_000 })
        foreach (int concurrency in new[] { 1, 4 })
        {
            var workload = new QueryPartitionBenchmarks { Count = rows, Concurrency = concurrency };
            try
            {
                workload.Setup();
                foreach (var result in Measure(workload))
                {
                    Console.WriteLine(FormattableString.Invariant(
                        $"{result.Case},{result.Rows},{result.Concurrency},{result.MeanMicroseconds:F6},{result.MedianMicroseconds:F6},{result.P95Microseconds:F6},{result.MaximumMicroseconds:F6},{(double)result.OwnerAllocatedBytes / AllocationOperations:F6},{(double)result.AllThreadAllocatedBytes / AllocationOperations:F6},{result.OwnerAllocatedBytes},{result.AllThreadAllocatedBytes}"));
                    allocationFree &= result.AllThreadAllocatedBytes == 0;
                }
            }
            finally { workload.Cleanup(); }
        }
        Console.WriteLine($"# zero_process_allocations={allocationFree.ToString().ToLowerInvariant()}; matrix=ok");
        if (!allocationFree)
            throw new InvalidOperationException("A warmed partition measurement allocated managed memory. Inspect all_thread_bytes_total.");
    }

    private static PartitionMeasurementResult[] Measure(QueryPartitionBenchmarks workload)
    {
        var cases = workload.MeasurementCases();
        var elapsed = new long[cases.Length][];
        var ownerBytes = new long[cases.Length];
        var allBytes = new long[cases.Length];
        for (int method = 0; method < cases.Length; method++)
            elapsed[method] = new long[Samples];
        long warmupStarted = Stopwatch.GetTimestamp();
        for (int repeat = 0; repeat < Warmups || Stopwatch.GetElapsedTime(warmupStarted).TotalSeconds < 1; repeat++)
            for (int offset = 0; offset < cases.Length; offset++)
                cases[(repeat + offset) % cases.Length].Operation();
        workload.ResetPositions();
        int operations = 0;

        for (int sample = 0; sample < Samples; sample++)
        {
            for (int offset = 0; offset < cases.Length; offset++)
            {
                int method = (sample + offset) % cases.Length;
                Action operation = cases[method].Operation;
                long start = Stopwatch.GetTimestamp();
                for (int repeat = 0; repeat < OperationsPerSample; repeat++) operation();
                elapsed[method][sample] = Stopwatch.GetTimestamp() - start;
            }
            operations += OperationsPerSample * cases.Length;
        }
        workload.ValidateState(operations, "timed batches");

        for (int method = 0; method < cases.Length; method++)
        {
            Action operation = cases[method].Operation;
            long allBefore = GC.GetTotalAllocatedBytes(precise: true);
            long ownerBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int repeat = 0; repeat < AllocationOperations; repeat++) operation();
            ownerBytes[method] = GC.GetAllocatedBytesForCurrentThread() - ownerBefore;
            allBytes[method] = GC.GetTotalAllocatedBytes(precise: true) - allBefore;
            operations += AllocationOperations;
        }
        workload.ValidateState(operations, "allocation batches");

        var results = new PartitionMeasurementResult[cases.Length];
        double microsecondsPerTick = 1_000_000d / Stopwatch.Frequency / OperationsPerSample;
        for (int method = 0; method < cases.Length; method++)
        {
            Array.Sort(elapsed[method]);
            double mean = elapsed[method].Average() * microsecondsPerTick;
            double median = ((double)elapsed[method][Samples / 2 - 1] + elapsed[method][Samples / 2]) / 2 * microsecondsPerTick;
            double p95 = elapsed[method][(int)Math.Ceiling(Samples * 0.95) - 1] * microsecondsPerTick;
            results[method] = new(cases[method].Case, cases[method].Rows, cases[method].Concurrency,
                mean, median, p95, elapsed[method][^1] * microsecondsPerTick, ownerBytes[method], allBytes[method]);
        }
        return results;
    }
}
