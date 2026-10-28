using System.Collections.Concurrent;
using Archevolt.Scheduling;

namespace Archevolt.Tests;

[Trait("Category", "Scheduling")]
public sealed class ScheduleExecutionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Timing_is_disabled_by_default_and_reports_only_the_latest_enabled_run()
    {
        using var world = new WorldBuilder().Build();
        int calls = 0;
        using var schedule = BoundSchedule.Bind(world, ActionSystem("body", _ => calls++));
        schedule.Run(TestContext.Current.CancellationToken);
        var disabled = schedule.Timings[0];
        Assert.Equal("body", disabled.InstanceKey);
        Assert.Equal("body", disabled.SystemName);
        Assert.Equal(0, disabled.BodyInvocations);
        Assert.Equal(0, disabled.BodyElapsedTicks);
        Assert.Equal(0, disabled.ConditionElapsedTicks);
        Assert.Equal(1, calls);

        schedule.EnableTiming();
        schedule.Run(TestContext.Current.CancellationToken);
        var first = schedule.Timings[0];
        Assert.Equal(1, first.BodyInvocations);
        Assert.Equal(0, first.BodyFailures);
        Assert.Equal(0, first.ConditionEvaluations);
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(1, schedule.Timings[0].BodyInvocations);
        Assert.Equal(1, first.BodyInvocations);
        schedule.EnableTiming(false);
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(4, calls);
        Assert.Equal(0, schedule.Timings[0].BodyInvocations);
        Assert.Equal(TimeSpan.Zero, schedule.Timings[0].BodyElapsed);
        Assert.Equal(TimeSpan.Zero, schedule.Timings[0].ConditionElapsed);
    }

    [Fact]
    public void Timing_records_condition_skips_and_joined_body_or_condition_failures()
    {
        using var world = new WorldBuilder().Build();
        bool enabled = false;
        bool failBody = false;
        bool failCondition = false;
        var condition = SystemDescriptor.Create("condition", _ => new PredicateCondition(_ =>
        {
            if (failCondition) throw new MarkerException();
            return enabled;
        })).AsCondition().Build();
        var body = ActionSystem("body", _ => { if (failBody) throw new MarkerException(); });
        using var schedule = BoundSchedule.Bind(world, [body], [], conditions: [condition]).EnableTiming();

        schedule.Run(TestContext.Current.CancellationToken);
        var skipped = schedule.Timings[0];
        Assert.True(skipped.SkippedByCondition);
        Assert.Equal(1, skipped.ConditionEvaluations);
        Assert.Equal(0, skipped.ConditionFailures);
        Assert.Equal(0, skipped.BodyInvocations);
        Assert.Equal(0, skipped.BodyElapsedTicks);
        enabled = true;
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.False(schedule.Timings[0].SkippedByCondition);
        Assert.Equal(1, schedule.Timings[0].BodyInvocations);
        Assert.Equal(1, schedule.Timings[0].ConditionEvaluations);
        failBody = true;
        Assert.Throws<AggregateException>(() => schedule.Run(TestContext.Current.CancellationToken));
        Assert.Equal(1, schedule.Timings[0].BodyInvocations);
        Assert.Equal(1, schedule.Timings[0].BodyFailures);
        Assert.Equal(0, schedule.Timings[0].ConditionFailures);
        schedule.Reset();
        failBody = false;
        failCondition = true;
        Assert.Throws<AggregateException>(() => schedule.Run(TestContext.Current.CancellationToken));
        Assert.Equal(1, schedule.Timings[0].ConditionEvaluations);
        Assert.Equal(1, schedule.Timings[0].ConditionFailures);
        Assert.Equal(0, schedule.Timings[0].BodyInvocations);
        Assert.False(schedule.Timings[0].SkippedByCondition);
        Assert.True(skipped.SkippedByCondition);
    }

    [Fact]
    public void Timing_configuration_and_inspection_require_an_idle_owner_thread()
    {
        using var world = new WorldBuilder().Build();
        BoundSchedule? schedule = null;
        using var bound = BoundSchedule.Bind(world, ActionSystem("body", run =>
        {
            Assert.Throws<InvalidOperationException>(() => schedule!.EnableTiming());
            Assert.Throws<InvalidOperationException>(() => { _ = schedule!.Timings.Length; });
        })).EnableTiming();
        schedule = bound;
        int rejections = 0;
        var wrongThread = new Thread(() =>
        {
            try { bound.EnableTiming(); }
            catch (InvalidOperationException) { rejections++; }
            try { _ = bound.Timings.Length; }
            catch (InvalidOperationException) { rejections++; }
        });
        wrongThread.Start();
        Assert.True(wrongThread.Join(Timeout));
        Assert.Equal(2, rejections);
        bound.Run(TestContext.Current.CancellationToken);
        Assert.Equal(1, bound.Timings[0].BodyInvocations);
        bound.Dispose();
        Assert.Throws<ObjectDisposedException>(() => bound.EnableTiming());
        Assert.Throws<ObjectDisposedException>(() => { _ = bound.Timings.Length; });
    }

    [Fact]
    public void World_disposal_requires_every_bound_schedule_to_release_its_borrow()
    {
        using var world = new WorldBuilder().AddComponent<Position>().Build();
        var entity = world.Spawn();
        world.Insert(entity, new Position(7));
        int calls = 0;
        using var first = BoundSchedule.Bind(world, ActionSystem("first", _ => calls++));
        using var second = BoundSchedule.Bind(world, ActionSystem("second", _ => calls++));

        Assert.Throws<InvalidOperationException>(world.Dispose);
        Assert.Equal(7, world.Get<Position>(entity).Value);
        first.Run(TestContext.Current.CancellationToken);
        first.Dispose();
        Assert.Throws<InvalidOperationException>(world.Dispose);
        second.Run(TestContext.Current.CancellationToken);
        Assert.Equal(2, calls);
        second.Dispose();

        world.Dispose();
        world.Dispose();
        Assert.Throws<ObjectDisposedException>(() => world.Contains(entity));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Serial_and_parallel_execution_use_the_same_dependency_order_and_results(int budget)
    {
        using var world = new WorldBuilder().AddComponent<Position>().UseParallelExecution(budget).Build();
        var entities = Enumerable.Range(0, 100).Select(i =>
        {
            var entity = world.Spawn();
            world.Insert(entity, new Position(i));
            return entity;
        }).ToArray();
        var shape = QueryShape.Create("positions").Write<Position>(0).Build();
        var doubleValue = SystemDescriptor.Create("double", binder => new TransformBody(binder.Query(0), static x => x * 2)).Query(shape).Build();
        var increment = SystemDescriptor.Create("increment", binder => new TransformBody(binder.Query(0), static x => x + 1)).Query(shape).Build();
        using var schedule = BoundSchedule.Bind(world, [doubleValue, increment], [new EdgeInput(1, 0)]);

        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(Enumerable.Range(0, 100).Select(i => (i + 1) * 2), entities.Select(e => world.Get<Position>(e).Value));
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(Enumerable.Range(0, 100).Select(i => ((i + 1) * 2 + 1) * 2), entities.Select(e => world.Get<Position>(e).Value));
        Assert.False(schedule.IsFaulted);
    }

    [Fact]
    public void Owner_thread_resource_work_overlaps_disjoint_worker_work_within_the_total_budget()
    {
        int owner = Environment.CurrentManagedThreadId;
        using var world = new WorldBuilder().AddResource<OwnerMarker>(ownerThread: true).UseParallelExecution(2).Build();
        world.InsertResource(new OwnerMarker(owner));
        using var together = new Barrier(2);
        int workerThread = 0;
        var ownerSystem = SystemDescriptor.Create("owner", binder => new OwnerBody(binder.Resource<OwnerMarker>(0), _ =>
            Assert.True(together.SignalAndWait(Timeout)))).Res<OwnerMarker>().Build();
        var workerSystem = ActionSystem("worker", _ =>
        {
            workerThread = Environment.CurrentManagedThreadId;
            Assert.True(together.SignalAndWait(Timeout));
        });
        using var schedule = BoundSchedule.Bind(world, ownerSystem, workerSystem);

        schedule.Run(TestContext.Current.CancellationToken);

        Assert.NotEqual(owner, workerThread);
        Assert.Equal([2], schedule.Plan.Steps.Where(s => s.Kind == StepKind.Batch).Select(s => s.Count));
    }

    [Fact]
    public void Managed_resource_systems_execute_exclusively_on_the_owner()
    {
        int owner = Environment.CurrentManagedThreadId;
        var module = new ModuleBuilder("managed").ManagedResource<ManagedMarker>().Build();
        using var world = new WorldBuilder().AddModule(module).UseParallelExecution(4).Build();
        world.InsertResource(new ManagedMarker());
        int active = 0;
        var before = ActionSystem("before", _ =>
        {
            Assert.Equal(1, Interlocked.Increment(ref active));
            Interlocked.Decrement(ref active);
        });
        var exclusive = SystemDescriptor.Create("exclusive", binder => new ManagedBody(binder.Managed<ManagedMarker>(0), _ =>
        {
            Assert.Equal(owner, Environment.CurrentManagedThreadId);
            Assert.Equal(1, Interlocked.Increment(ref active));
            Interlocked.Decrement(ref active);
        })).Managed<ManagedMarker>().Build();
        var after = ActionSystem("after", _ =>
        {
            Assert.Equal(1, Interlocked.Increment(ref active));
            Interlocked.Decrement(ref active);
        });
        using var schedule = BoundSchedule.Bind(world, before, exclusive, after);

        schedule.Run(TestContext.Current.CancellationToken);

        Assert.Equal([1, 1, 1], schedule.Plan.Steps.Where(s => s.Kind == StepKind.Batch).Select(s => s.Count));
        Assert.Equal(0, active);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Partitioned_each_updates_every_entity_once_and_uses_one_tick_per_invocation(int budget)
    {
        using var world = new WorldBuilder().AddComponent<Position>(tracked: true).UseParallelExecution(budget).Build();
        var entities = Enumerable.Range(0, 200).Select(i =>
        {
            var entity = world.Spawn();
            world.Insert(entity, new Position(i));
            return entity;
        }).ToArray();
        var shape = QueryShape.Create("positions").Write<Position>(0).Build();
        using var together = new Barrier(budget);
        var ticks = new ConcurrentBag<ulong>();
        var descriptor = SystemDescriptor.Create("partitioned", binder => new PartitionBody(binder.Query(0), ticks, together))
            .EachQuery(shape, partitionable: true).Build();
        int evaluations = 0;
        var condition = SystemDescriptor.Create("condition", _ => new PredicateCondition(_ => { evaluations++; return true; })).AsCondition().Build();
        using var schedule = BoundSchedule.Bind(world, [descriptor], [], conditions: [condition]).EnableTiming();

        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(Enumerable.Range(0, 200).Select(i => i + 1), entities.Select(e => world.Get<Position>(e).Value));
        Assert.Equal(budget, ticks.Count);
        Assert.Equal(1, evaluations);
        Assert.Equal(budget, schedule.Timings[0].BodyInvocations);
        Assert.Equal(0, schedule.Timings[0].BodyFailures);
        Assert.True(schedule.Timings[0].BodyElapsedTicks > 0);
        Assert.Equal(1, schedule.Timings[0].ConditionEvaluations);
        ulong first = Assert.Single(ticks.Distinct());
        ticks.Clear();
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(Enumerable.Range(0, 200).Select(i => i + 2), entities.Select(e => world.Get<Position>(e).Value));
        Assert.Equal(budget, ticks.Count);
        Assert.Equal(2, evaluations);
        Assert.Equal(budget, schedule.Timings[0].BodyInvocations);
        Assert.Equal(1, schedule.Timings[0].ConditionEvaluations);
        Assert.True(Assert.Single(ticks.Distinct()) > first);
    }

    [Fact]
    public void Owner_failure_joins_active_workers_and_requires_reset_before_reuse()
    {
        using var world = new WorldBuilder().AddResource<OwnerMarker>(ownerThread: true).UseParallelExecution(2).Build();
        world.InsertResource(new OwnerMarker(Environment.CurrentManagedThreadId));
        using var workerStarted = new ManualResetEventSlim();
        using var failed = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var returned = new ManualResetEventSlim();
        bool fail = true;
        int completed = 0;
        bool returnedEarly = false;
        var ownerSystem = SystemDescriptor.Create("owner", binder => new OwnerBody(binder.Resource<OwnerMarker>(0), _ =>
        {
            if (!fail) return;
            Assert.True(workerStarted.Wait(Timeout));
            failed.Set();
            throw new MarkerException();
        })).Res<OwnerMarker>().Build();
        var workerSystem = ActionSystem("worker", _ =>
        {
            workerStarted.Set();
            Assert.True(release.Wait(Timeout));
            Interlocked.Increment(ref completed);
        });
        using var schedule = BoundSchedule.Bind(world, [ownerSystem, workerSystem], [], ["owner instance", "worker instance"]);
        var helper = new Thread(() =>
        {
            if (failed.Wait(Timeout))
                returnedEarly = returned.Wait(TimeSpan.FromMilliseconds(150));
            release.Set();
        }) { IsBackground = true };
        helper.Start();
        try
        {
            var failure = Assert.Throws<AggregateException>(() => schedule.Run(TestContext.Current.CancellationToken));
            returned.Set();
            var systemFailure = Assert.IsType<SystemExecutionException>(Assert.Single(failure.InnerExceptions));
            Assert.Equal("owner instance", systemFailure.InstanceKey);
            Assert.IsType<MarkerException>(systemFailure.InnerException);
            Assert.Equal(1, completed);
            Assert.False(returnedEarly);
            Assert.True(schedule.IsFaulted);
            Assert.Throws<InvalidOperationException>(() => schedule.Run(TestContext.Current.CancellationToken));
            Assert.True(world.Contains(world.Spawn()));
            fail = false;
            schedule.Reset();
            schedule.Run(TestContext.Current.CancellationToken);
            Assert.Equal(2, completed);
            Assert.False(schedule.IsFaulted);
        }
        finally
        {
            release.Set();
            Assert.True(helper.Join(Timeout));
        }
    }

    [Fact]
    public void Cancellation_joins_started_work_and_a_reset_schedule_can_run_again()
    {
        using var world = new WorldBuilder().UseParallelExecution(2).Build();
        using var cancellation = new CancellationTokenSource();
        using var started = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        int completed = 0;
        bool coordinate = true;
        RunAction action = _ =>
        {
            if (coordinate)
            {
                started.Signal();
                Assert.True(release.Wait(Timeout));
            }
            Interlocked.Increment(ref completed);
        };
        using var schedule = BoundSchedule.Bind(world, ActionSystem("A", action), ActionSystem("B", action));
        var helper = new Thread(() =>
        {
            if (started.Wait(Timeout)) cancellation.Cancel();
            release.Set();
        }) { IsBackground = true };
        helper.Start();
        try
        {
            Assert.Throws<OperationCanceledException>(() => schedule.Run(cancellation.Token));
            Assert.Equal(2, completed);
            Assert.True(schedule.IsFaulted);
            coordinate = false;
            schedule.Reset();
            schedule.Run(TestContext.Current.CancellationToken);
            Assert.Equal(4, completed);
        }
        finally
        {
            release.Set();
            Assert.True(helper.Join(Timeout));
        }
    }

    [Fact]
    public void Failed_bodies_keep_their_previous_change_window_until_a_successful_run()
    {
        using var world = new WorldBuilder().Build();
        ulong failedTick = 0;
        bool fail = true;
        bool observedFailedTick = false;
        using var schedule = BoundSchedule.Bind(world, ActionSystem("A", run =>
        {
            if (fail)
            {
                failedTick = run.Window.Now;
                throw new MarkerException();
            }
            observedFailedTick = run.Window.Contains(failedTick);
        }));

        Assert.Throws<AggregateException>(() => schedule.Run(TestContext.Current.CancellationToken));
        fail = false;
        schedule.Reset();
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.True(observedFailedTick);
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.False(observedFailedTick);
    }

    [Fact]
    public void Pre_cancellation_and_missing_resources_execute_no_bodies()
    {
        using var world = new WorldBuilder().AddResource<OwnerMarker>().Build();
        world.InsertResource(new OwnerMarker());
        int calls = 0;
        var descriptor = SystemDescriptor.Create("A", _ => new ActionBody(_ => calls++)).Res<OwnerMarker>().Build();
        using var schedule = BoundSchedule.Bind(world, descriptor);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => schedule.Run(cancellation.Token));
        Assert.Equal(0, calls);
        schedule.Reset();
        world.RemoveResource<OwnerMarker>();
        Assert.Throws<InvalidOperationException>(() => schedule.Run(TestContext.Current.CancellationToken));
        Assert.Equal(0, calls);
        schedule.Reset();
        world.InsertResource(new OwnerMarker());
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Schedule_control_rejects_wrong_threads_reentrancy_and_running_disposal()
    {
        using var world = new WorldBuilder().Build();
        BoundSchedule? schedule = null;
        using var bound = BoundSchedule.Bind(world, ActionSystem("A", _ =>
        {
            Assert.Throws<InvalidOperationException>(() => schedule!.Run(TestContext.Current.CancellationToken));
            Assert.Throws<InvalidOperationException>(() => schedule!.Reset());
            Assert.Throws<InvalidOperationException>(() => schedule!.Dispose());
            Assert.Throws<InvalidOperationException>(world.Dispose);
        }));
        schedule = bound;
        Exception? wrongThread = null;
        var thread = new Thread(() =>
        {
            try { bound.Run(TestContext.Current.CancellationToken); }
            catch (Exception exception) { wrongThread = exception; }
        });
        thread.Start();
        Assert.True(thread.Join(Timeout));
        Assert.IsType<InvalidOperationException>(wrongThread);
        bound.Run(TestContext.Current.CancellationToken);
        bound.Dispose();
        bound.Dispose();
        Assert.Throws<ObjectDisposedException>(() => bound.Run(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_false_condition_preserves_the_skipped_bodys_change_cursor()
    {
        using var world = new WorldBuilder().AddComponent<Position>(tracked: true).AddResource<Gate>().Build();
        var entity = world.Spawn();
        world.Insert(entity, new Position(5));
        world.InsertResource(new Gate(false));
        var counts = new List<int>();
        var shape = QueryShape.Create("changed").Read<Position>(0).Changed<Position>(0).Build();
        var body = SystemDescriptor.Create("body", binder => new CountBody(binder.Query(0), counts)).Query(shape).Build();
        var condition = SystemDescriptor.Create("gate", binder => new GateCondition(binder.Resource<Gate>(0))).Res<Gate>().AsCondition().Build();
        using var schedule = BoundSchedule.Bind(world, [body], [], conditions: [condition]);

        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Empty(counts);
        world.InsertResource(new Gate(true));
        schedule.Run(TestContext.Current.CancellationToken);
        schedule.Run(TestContext.Current.CancellationToken);

        Assert.Equal([1, 0], counts);
    }

    [Fact]
    public void Repeated_conditions_have_independent_local_state()
    {
        using var world = new WorldBuilder().UseParallelExecution(2).Build();
        int calls = 0;
        var body = ActionSystem("body", _ => Interlocked.Increment(ref calls));
        var condition = SystemDescriptor.Create("every other", binder => new EveryOtherCondition(binder.Local<int>(0))).Local<int>().AsCondition().Build();
        using var schedule = BoundSchedule.Bind(world, [body, body], [], ["first", "second"], conditions: [condition, condition]);

        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(0, calls);
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(2, calls);
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Condition_reads_create_conflicts_and_evaluate_after_the_predecessor_body()
    {
        using var world = new WorldBuilder().AddComponent<Position>().UseParallelExecution(2).Build();
        var entity = world.Spawn();
        world.Insert(entity, new Position(0));
        int calls = 0;
        var write = QueryShape.Create("write").Write<Position>(0).Build();
        var read = QueryShape.Create("read").Read<Position>(0).Build();
        var producer = SystemDescriptor.Create("producer", binder => new TransformBody(binder.Query(0), static _ => 7)).Query(write).Build();
        var condition = SystemDescriptor.Create("is seven", binder => new QueryCondition(binder.Query(0), null, true)).Query(read).AsCondition().Build();
        using var schedule = BoundSchedule.Bind(world, [producer, ActionSystem("consumer", _ => calls++)], [], conditions: [null, condition]);

        schedule.Run(TestContext.Current.CancellationToken);

        Assert.Equal(1, calls);
        Assert.Equal(["AV008"], schedule.Plan.Diagnostics.Select(d => d.Code));
        Assert.Equal([1, 1], schedule.Plan.Steps.Where(s => s.Kind == StepKind.Batch).Select(s => s.Count));
    }

    [Fact]
    public void False_conditions_advance_their_own_change_cursor()
    {
        using var world = new WorldBuilder().AddComponent<Position>(tracked: true).Build();
        var entity = world.Spawn();
        world.Insert(entity, new Position(7));
        var counts = new List<int>();
        var changed = QueryShape.Create("changed").Read<Position>(0).Changed<Position>(0).Build();
        var condition = SystemDescriptor.Create("condition", binder => new QueryCondition(binder.Query(0), counts, false)).Query(changed).AsCondition().Build();
        using var schedule = BoundSchedule.Bind(world, [ActionSystem("body", _ => Assert.Fail("A false condition executed its body."))], [], conditions: [condition]);

        schedule.Run(TestContext.Current.CancellationToken);
        schedule.Run(TestContext.Current.CancellationToken);

        Assert.Equal([1, 0], counts);
    }

    [Fact]
    public void Condition_failures_preserve_their_change_window_and_require_reset()
    {
        using var world = new WorldBuilder().Build();
        bool fail = true;
        ulong failedTick = 0;
        bool observed = false;
        int calls = 0;
        var condition = SystemDescriptor.Create("condition", _ => new PredicateCondition(run =>
        {
            if (fail)
            {
                failedTick = run.Window.Now;
                throw new MarkerException();
            }
            observed = run.Window.Contains(failedTick);
            return true;
        })).AsCondition().Build();
        using var schedule = BoundSchedule.Bind(world, [ActionSystem("body", _ => calls++)], [], conditions: [condition]);

        Assert.Throws<AggregateException>(() => schedule.Run(TestContext.Current.CancellationToken));
        Assert.True(schedule.IsFaulted);
        Assert.Equal(0, calls);
        fail = false;
        schedule.Reset();
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.True(observed);
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.False(observed);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Conditions_reject_mutable_world_access(bool resource)
    {
        var builder = SystemDescriptor.Create("bad condition", _ => new PredicateCondition(_ => true)).AsCondition();
        if (resource) builder.Res<Gate>(write: true);
        else builder.Query(QueryShape.Create("write").Write<Position>(0).Build());

        Assert.Throws<ArgumentException>(() => builder.Build());
    }

    [Fact]
    public void Repeated_method_registrations_have_independent_local_state_and_shared_world_pool()
    {
        using var world = new WorldBuilder().UseParallelExecution(2).Build();
        var values = new ConcurrentBag<int>();
        var descriptor = SystemDescriptor.Create("counter", binder => new LocalBody(binder.Local<int>(0), values)).Local<int>().Build();
        using var first = BoundSchedule.Bind(world, [descriptor, descriptor], [], ["first", "second"]);
        using var second = BoundSchedule.Bind(world, [descriptor], [], ["third"]);
        first.Run(TestContext.Current.CancellationToken);
        first.Run(TestContext.Current.CancellationToken);
        second.Run(TestContext.Current.CancellationToken);
        first.Dispose();
        second.Run(TestContext.Current.CancellationToken);

        Assert.Equal([1, 1, 1, 2, 2, 2], values.Order());
    }

    private static SystemDescriptor ActionSystem(string name, RunAction action)
        => SystemDescriptor.Create(name, _ => new ActionBody(action)).Build();
    private delegate void RunAction(SystemRun run);
    private delegate bool ConditionAction(SystemRun run);
    private sealed class PredicateCondition(ConditionAction action) : ConditionBody
    {
        protected override bool Evaluate(SystemRun run) => action(run);
    }
    private sealed class GateCondition(ResourceHandle<Gate> gate) : ConditionBody
    {
        protected override bool Evaluate(SystemRun run) => gate.Read(run).Value.Enabled;
    }
    private sealed class EveryOtherCondition(LocalHandle<int> count) : ConditionBody
    {
        protected override bool Evaluate(SystemRun run) => ++count.Get(run).Value % 2 == 0;
    }
    private sealed class QueryCondition(QueryState query, List<int>? counts, bool result) : ConditionBody
    {
        protected override bool Evaluate(SystemRun run)
        {
            int count = 0;
            var cursor = query.Begin(run);
            while (cursor.Next(out var block))
            {
                var positions = block.Read<Position>(0);
                for (int row = 0; row < block.Count; row++)
                    if (block.MatchesRow(row) && positions[row].Value == 7) count++;
            }
            counts?.Add(count);
            return result && count > 0;
        }
    }
    private sealed class CountBody(QueryState query, List<int> counts) : SystemBody
    {
        protected override void Run(SystemRun run)
        {
            int count = 0;
            var cursor = query.Begin(run);
            while (cursor.Next(out var block))
                for (int row = 0; row < block.Count; row++) if (block.MatchesRow(row)) count++;
            counts.Add(count);
        }
    }
    private sealed class ActionBody(RunAction action) : SystemBody
    {
        protected override void Run(SystemRun run) => action(run);
    }
    private sealed class OwnerBody(ResourceHandle<OwnerMarker> resource, RunAction action) : SystemBody
    {
        protected override void Run(SystemRun run)
        {
            Assert.Equal(resource.Read(run).Value.Thread, Environment.CurrentManagedThreadId);
            action(run);
        }
    }
    private sealed class ManagedBody(ManagedResourceHandle<ManagedMarker> resource, RunAction action) : SystemBody
    {
        protected override void Run(SystemRun run)
        {
            Assert.NotNull(resource.Read(run).Value);
            action(run);
        }
    }
    private sealed class TransformBody(QueryState query, Func<int, int> transform) : SystemBody
    {
        protected override void Run(SystemRun run)
        {
            var cursor = query.Begin(run);
            while (cursor.Next(out var block))
            {
                var values = block.Write<Position>(0);
                for (int row = 0; row < block.Count; row++) values[row] = new Position(transform(values[row].Value));
            }
        }
    }
    private sealed class PartitionBody(QueryState query, ConcurrentBag<ulong> ticks, Barrier together) : SystemBody
    {
        protected override void Run(SystemRun run)
        {
            ticks.Add(run.Window.Now);
            Assert.True(together.SignalAndWait(Timeout));
            var cursor = query.Begin(run);
            while (cursor.Next(out var block))
            {
                var values = block.Tracked<Position>(0);
                for (int row = 0; row < block.Count; row++)
                {
                    ref var value = ref values.Write(row);
                    value = new Position(value.Value + 1);
                }
            }
        }
    }
    private sealed class LocalBody(LocalHandle<int> local, ConcurrentBag<int> values) : SystemBody
    {
        protected override void Run(SystemRun run) => values.Add(++local.Get(run).Value);
    }
    private readonly record struct Position(int Value);
    private readonly record struct OwnerMarker(int Thread);
    private readonly record struct Gate(bool Enabled);
    private sealed class ManagedMarker;
    private sealed class MarkerException : Exception;
}
