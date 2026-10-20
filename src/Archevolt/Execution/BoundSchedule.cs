using Archevolt.Scheduling;
using System.Diagnostics;

namespace Archevolt;

public sealed class BoundSchedule : IDisposable
{
    private readonly World _world;
    private readonly SystemInstance[] _instances;
    private readonly SystemInstance?[] _conditions;
    private readonly NodeTraits[] _traits;
    private readonly WorkItem[] _work;
    private readonly WorkItem[] _ownerWork;
    private readonly int[] _remaining;
    private readonly int[] _failed;
    private readonly bool[] _begun;
    private readonly Action<int> _execute;
    private readonly Action _executeOwners;
    private readonly TimingAccumulator[] _timingAccumulators;
    private readonly SystemTiming[] _timings;
    private bool _timingEnabled;
    private CancellationToken _cancellation;
    private int _ownerCount;
    private int _batchFailed;
    private ScheduleState _state;

    private BoundSchedule(World world, SystemInstance[] instances, SystemInstance?[] conditions, SchedulePlan plan, NodeTraits[] traits)
    {
        _world = world;
        _instances = instances;
        _conditions = conditions;
        Plan = plan;
        _traits = traits;
        _work = new WorkItem[checked(instances.Length * world.MaxConcurrency)];
        _ownerWork = new WorkItem[instances.Length];
        _remaining = new int[instances.Length];
        _failed = new int[instances.Length];
        _begun = new bool[instances.Length];
        _execute = Execute;
        _executeOwners = ExecuteOwners;
        _timingAccumulators = new TimingAccumulator[instances.Length];
        _timings = new SystemTiming[instances.Length];
        PublishTimings();
    }

    public SchedulePlan Plan { get; }
    public bool IsFaulted => _state == ScheduleState.Faulted;
    public ReadOnlySpan<SystemTiming> Timings
    {
        get
        {
            RequireTimingAccess();
            return _timings;
        }
    }

    public BoundSchedule EnableTiming(bool enabled = true)
    {
        RequireTimingAccess();
        _timingEnabled = enabled;
        Array.Clear(_timingAccumulators);
        PublishTimings();
        return this;
    }

    private void RequireTimingAccess()
    {
        _world.RequireIdle();
        ObjectDisposedException.ThrowIf(_state == ScheduleState.Disposed, this);
    }

    private void PublishTimings()
    {
        for (int node = 0; node < _instances.Length; node++)
        {
            var instance = _instances[node];
            ref var timing = ref _timingAccumulators[node];
            _timings[node] = new SystemTiming(instance.Key, instance.Descriptor.Name,
                timing.BodyInvocations, timing.BodyFailures, timing.SkippedByCondition, timing.BodyElapsedTicks,
                timing.ConditionEvaluations, timing.ConditionFailures, timing.ConditionElapsedTicks);
        }
    }

    public static BoundSchedule Bind(World world, params SystemDescriptor[] systems)
        => Bind(world, systems, []);

    public static BoundSchedule Bind(World world, SystemDescriptor[] systems, EdgeInput[] edges,
        string[]? instanceKeys = null, int[]? orders = null, SystemDescriptor?[]? conditions = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(systems);
        ArgumentNullException.ThrowIfNull(edges);
        world.RequireIdle();
        if (instanceKeys is not null && instanceKeys.Length != systems.Length)
            throw new ArgumentException("Instance keys must match the system count.", nameof(instanceKeys));
        if (orders is not null && orders.Length != systems.Length)
            throw new ArgumentException("Run ordinals must match the system count.", nameof(orders));
        if (conditions is not null && conditions.Length != systems.Length)
            throw new ArgumentException("Conditions must match the system count.", nameof(conditions));
        var descriptors = (SystemDescriptor[])systems.Clone();
        var conditionDescriptors = conditions is null ? new SystemDescriptor?[systems.Length] : (SystemDescriptor?[])conditions.Clone();
        var nodes = new NodeInput[descriptors.Length];
        var traits = new NodeTraits[descriptors.Length];
        for (int i = 0; i < descriptors.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(descriptors[i]);
            var access = descriptors[i].ToAccess(world);
            if (conditionDescriptors[i] is { } condition)
            {
                if (!condition.IsCondition) throw new ArgumentException("Run conditions must be declared with AsCondition().", nameof(conditions));
                var conditionAccess = condition.ToAccess(world);
                access = new SystemAccess(access.Name, access.Clauses, access.ResourcesRead, access.ResourcesWritten,
                    access.MessagesRead.Concat(conditionAccess.MessagesRead), access.MessagesWritten,
                    access.Traits | conditionAccess.Traits, conditionAccess.Clauses, conditionAccess.ResourcesRead);
            }
            nodes[i] = new NodeInput(i, instanceKeys is null ? descriptors[i].Name : instanceKeys[i], access, orders?[i] ?? i,
                hasCondition: conditionDescriptors[i] is not null);
            traits[i] = access.Traits;
        }
        var plan = SchedulePlanner.Plan(new ScheduleInput(nodes, edges));
        if (!plan.IsValid)
            throw new ArgumentException(string.Join(Environment.NewLine, plan.Diagnostics.Select(d => d.Code + ": " + d.Detail)));
        var instances = new SystemInstance[descriptors.Length];
        var conditionInstances = new SystemInstance?[descriptors.Length];
        for (int i = 0; i < instances.Length; i++)
        {
            instances[i] = new SystemInstance(world, descriptors[i], nodes[i].InstanceKey);
            if (conditionDescriptors[i] is { } condition)
                conditionInstances[i] = new SystemInstance(world, condition, nodes[i].InstanceKey + "/condition");
        }
        var schedule = new BoundSchedule(world, instances, conditionInstances, plan, traits);
        world.Schedules.Add(schedule);
        return schedule;
    }

    public void Run(CancellationToken cancellation = default)
    {
        _world.RequireIdle();
        ObjectDisposedException.ThrowIf(_state == ScheduleState.Disposed, this);
        if (_state == ScheduleState.Faulted)
            throw new InvalidOperationException("Reset the faulted schedule before running it again.");
        if (_state != ScheduleState.Idle)
            throw new InvalidOperationException("The schedule is already running.");
        _state = ScheduleState.Running;
        _world.EnterRun();
        _cancellation = cancellation;
        if (_timingEnabled) Array.Clear(_timingAccumulators);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var instance in _instances)
                instance.Prepare();
            foreach (var condition in _conditions)
                condition?.Prepare();
            for (int i = 0; i < Plan.Steps.Count; i++)
            {
                var step = Plan.Steps[i];
                cancellation.ThrowIfCancellationRequested();
                if (step.Kind == StepKind.Batch)
                    RunBatch(step);
                else
                {
                    _world.FlushCommands(_instances, Plan.NodeOrder, _world.NextTick());
                    foreach (var instance in _instances) instance.Prepare();
                    foreach (var condition in _conditions) condition?.Prepare();
                }
            }
            _state = ScheduleState.Idle;
        }
        catch
        {
            _state = ScheduleState.Faulted;
            _world.DiscardCommands(_instances, Plan.NodeOrder);
            throw;
        }
        finally
        {
            if (_timingEnabled) PublishTimings();
            _world.ExitRun();
            _cancellation = default;
        }
    }

    private void RunBatch(PlanStep step)
    {
        int workCount = 0;
        _ownerCount = 0;
        _batchFailed = 0;
        try
        {
            for (int ordinal = step.First; ordinal < step.First + step.Count; ordinal++)
            {
                int node = Plan.BatchNodes[ordinal];
                var instance = _instances[node];
                _failed[node] = 0;
                _remaining[node] = 0;
                if (_conditions[node] is { } condition && !Evaluate(node, condition))
                {
                    if (_timingEnabled) _timingAccumulators[node].SkippedByCondition = true;
                    continue;
                }
                instance.Begin();
                _begun[node] = true;
                if ((_traits[node] & (NodeTraits.OwnerThread | NodeTraits.Exclusive)) != 0)
                {
                    _ownerWork[_ownerCount++] = new WorkItem(node, 0, int.MaxValue);
                    _remaining[node] = 1;
                }
                else if (instance.Descriptor.Partitionable && _world.MaxConcurrency > 1)
                {
                    int chunks = instance.Queries[0]!.TotalChunks;
                    int partitions = Math.Max(1, Math.Min(chunks, _world.MaxConcurrency));
                    _remaining[node] = partitions;
                    for (int partition = 0; partition < partitions; partition++)
                    {
                        int first = (int)((long)chunks * partition / partitions);
                        int end = (int)((long)chunks * (partition + 1) / partitions);
                        _work[workCount++] = new WorkItem(node, first, end - first);
                    }
                }
                else
                {
                    _work[workCount++] = new WorkItem(node, 0, int.MaxValue);
                    _remaining[node] = 1;
                }
            }
            _world.Workers.Run(workCount, _execute, _ownerCount == 0 ? null : _executeOwners, _cancellation);
        }
        finally
        {
            for (int ordinal = step.First; ordinal < step.First + step.Count; ordinal++)
            {
                int node = Plan.BatchNodes[ordinal];
                if (!_begun[node]) continue;
                _instances[node].Complete(_remaining[node] == 0 && _failed[node] == 0);
                _begun[node] = false;
            }
        }
    }

    private void Execute(int index) => Execute(_work[index]);

    private bool Evaluate(int node, SystemInstance condition)
    {
        bool success = false;
        bool timed = _timingEnabled;
        long started = timed ? Stopwatch.GetTimestamp() : 0;
        if (timed) _timingAccumulators[node].ConditionEvaluations++;
        try
        {
            condition.Body.Execute(condition.Begin());
            success = true;
            return ((ConditionBody)condition.Body).Result;
        }
        catch (Exception exception)
        {
            if (timed) _timingAccumulators[node].ConditionFailures++;
            throw new AggregateException("An Archevolt run condition failed.",
                new SystemExecutionException(condition.Key, condition.Descriptor.Name, 0, int.MaxValue, exception));
        }
        finally
        {
            if (timed) _timingAccumulators[node].ConditionElapsedTicks += Stopwatch.GetTimestamp() - started;
            condition.Complete(success);
        }
    }

    private void ExecuteOwners()
    {
        for (int i = 0; i < _ownerCount && !_cancellation.IsCancellationRequested && Volatile.Read(ref _batchFailed) == 0; i++)
            Execute(_ownerWork[i]);
    }

    private void Execute(WorkItem work)
    {
        var instance = _instances[work.Node];
        bool timed = _timingEnabled;
        long started = timed ? Stopwatch.GetTimestamp() : 0;
        if (timed) Interlocked.Increment(ref _timingAccumulators[work.Node].BodyInvocations);
        try
        {
            instance.Body.Execute(new SystemRun(instance, work.FirstChunk, work.ChunkCount));
        }
        catch (Exception exception)
        {
            if (timed) Interlocked.Increment(ref _timingAccumulators[work.Node].BodyFailures);
            Interlocked.Exchange(ref _failed[work.Node], 1);
            Interlocked.Exchange(ref _batchFailed, 1);
            throw new SystemExecutionException(instance.Key, instance.Descriptor.Name, work.FirstChunk, work.ChunkCount, exception);
        }
        finally
        {
            if (timed) Interlocked.Add(ref _timingAccumulators[work.Node].BodyElapsedTicks, Stopwatch.GetTimestamp() - started);
            Interlocked.Decrement(ref _remaining[work.Node]);
        }
    }

    public void Reset()
    {
        _world.RequireIdle();
        ObjectDisposedException.ThrowIf(_state == ScheduleState.Disposed, this);
        if (_state == ScheduleState.Running)
            throw new InvalidOperationException("Cannot reset a running schedule.");
        _state = ScheduleState.Idle;
    }

    public void Dispose()
    {
        _world.RequireOwner();
        if (_state == ScheduleState.Disposed) return;
        if (_state == ScheduleState.Running || _world.IsRunning)
            throw new InvalidOperationException("Cannot dispose a schedule while the world is running.");
        _world.DiscardCommands(_instances, Plan.NodeOrder);
        _state = ScheduleState.Disposed;
        _world.Schedules.Remove(this);
    }

    private readonly record struct WorkItem(int Node, int FirstChunk, int ChunkCount);
    private struct TimingAccumulator
    {
        internal int BodyInvocations;
        internal int BodyFailures;
        internal bool SkippedByCondition;
        internal long BodyElapsedTicks;
        internal int ConditionEvaluations;
        internal int ConditionFailures;
        internal long ConditionElapsedTicks;
    }
    private enum ScheduleState { Idle, Running, Faulted, Disposed }
}

public readonly record struct SystemTiming(string InstanceKey, string SystemName, int BodyInvocations,
    int BodyFailures, bool SkippedByCondition, long BodyElapsedTicks, int ConditionEvaluations,
    int ConditionFailures, long ConditionElapsedTicks)
{
    public TimeSpan BodyElapsed => TimeSpan.FromSeconds((double)BodyElapsedTicks / Stopwatch.Frequency);
    public TimeSpan ConditionElapsed => TimeSpan.FromSeconds((double)ConditionElapsedTicks / Stopwatch.Frequency);
}

public abstract class ConditionBody : SystemBody
{
    internal bool Result { get; private set; }
    protected abstract bool Evaluate(SystemRun run);
    protected sealed override void Run(SystemRun run) => Result = Evaluate(run);
}

public sealed class SystemExecutionException : Exception
{
    internal SystemExecutionException(string instanceKey, string systemName, int firstChunk, int chunkCount, Exception inner)
        : base($"System '{instanceKey}' failed.", inner)
    {
        InstanceKey = instanceKey;
        SystemName = systemName;
        FirstChunk = firstChunk;
        ChunkCount = chunkCount;
    }

    public string InstanceKey { get; }
    public string SystemName { get; }
    public int FirstChunk { get; }
    public int ChunkCount { get; }
}
