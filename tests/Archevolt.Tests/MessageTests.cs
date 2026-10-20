using Archevolt.Scheduling;

namespace Archevolt.Tests;

public sealed class MessageTests
{
    private readonly record struct Signal(int Value);
    private readonly record struct Position(int Value);

    [Fact]
    public void Readers_keep_independent_cursors_and_report_expired_messages()
    {
        using var world = new WorldBuilder().AddMessage<Signal>().Reserve(messagesPerType: 4).Build();
        var first = new ReadState { OneAtATime = true };
        var second = new ReadState();
        ReadState[] states = [first, second];
        var bindings = 0;
        var descriptor = SystemDescriptor.Create("read", binder => new Reader(binder, states[bindings++]))
            .MessageReader<Signal>().Build();
        using var firstSchedule = BoundSchedule.Bind(world, descriptor);
        using var secondSchedule = BoundSchedule.Bind(world, descriptor);

        world.SendMessage(new Signal(1));
        world.SendMessage(new Signal(2));
        firstSchedule.Run(TestContext.Current.CancellationToken);
        secondSchedule.Run(TestContext.Current.CancellationToken);
        firstSchedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal([1, 2], first.Values);
        Assert.Equal([1, 2], second.Values);

        world.AdvanceMessages();
        world.AdvanceMessages();
        world.SendMessage(new Signal(3));
        world.AdvanceMessages();
        world.AdvanceMessages();
        world.SendMessage(new Signal(4));
        firstSchedule.Run(TestContext.Current.CancellationToken);
        secondSchedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal([1, 2, 4], first.Values);
        Assert.Equal([1, 2, 4], second.Values);
        Assert.Equal([0UL, 0UL, 1UL], first.Missed);
        Assert.Equal([0UL, 1UL], second.Missed);
        secondSchedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal([1, 2, 4], second.Values);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void An_ordered_reader_observes_writes_in_the_same_run(int concurrency)
    {
        using var world = new WorldBuilder().AddMessage<Signal>().UseParallelExecution(concurrency).Build();
        var state = new ReadState();
        var writer = SystemDescriptor.Create("write", static binder => new Writer(binder)).MessageWriter<Signal>().Build();
        var reader = SystemDescriptor.Create("read", binder => new Reader(binder, state)).MessageReader<Signal>().Build();
        using var schedule = BoundSchedule.Bind(world, [writer, reader], [new EdgeInput(0, 1)]);
        schedule.Run(TestContext.Current.CancellationToken);
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal([7, 7], state.Values);
        Assert.Equal([0UL, 0UL], state.Missed);
    }

    [Fact]
    public void Removal_readers_observe_remove_and_despawn_with_retention()
    {
        using var world = new WorldBuilder().AddComponent<Position>().Build();
        var removals = new List<Removal>();
        var missed = new List<ulong>();
        var descriptor = SystemDescriptor.Create("removed", binder => new RemovalReader(binder, removals, missed))
            .Removed<Position>().Build();
        using var schedule = BoundSchedule.Bind(world, descriptor);
        var first = world.Spawn();
        var second = world.Spawn();
        world.Insert(first, new Position(1));
        world.Insert(second, new Position(2));
        Assert.True(world.Remove<Position>(first));
        Assert.True(world.Despawn(second));
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal([first, second], removals.Select(removal => removal.Entity));
        Assert.True(removals[0].Tick > 0);
        Assert.True(removals[1].Tick > removals[0].Tick);
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal(2, removals.Count);

        world.Insert(first, new Position(3));
        Assert.True(world.Remove<Position>(first));
        world.AdvanceMessages();
        world.AdvanceMessages();
        schedule.Run(TestContext.Current.CancellationToken);
        Assert.Equal([0UL, 0UL, 1UL], missed);
        Assert.Equal(2, removals.Count);
    }

    [Fact]
    public void A_message_writer_cannot_alias_a_reader_in_one_system()
    {
        Assert.Throws<ArgumentException>(() => SystemDescriptor.Create("invalid", static _ => new EmptyBody())
            .MessageWriter<Signal>().MessageReader<Signal>().Build());
    }

    private sealed class ReadState
    {
        internal readonly List<int> Values = [];
        internal readonly List<ulong> Missed = [];
        internal bool OneAtATime;
    }

    private sealed class Reader(SystemBinder binder, ReadState state) : SystemBody
    {
        private readonly MessageHandle<Signal> _handle = binder.Message<Signal>(0);
        protected override void Run(SystemRun run)
        {
            var reader = _handle.Read(run);
            state.Missed.Add(reader.MissedCount);
            foreach (var message in reader)
            {
                state.Values.Add(message.Value);
                if (state.OneAtATime)
                    break;
            }
        }
    }

    private sealed class Writer(SystemBinder binder) : SystemBody
    {
        private readonly MessageHandle<Signal> _handle = binder.Message<Signal>(0);
        protected override void Run(SystemRun run) => _handle.Write(run).Send(new Signal(7));
    }

    private sealed class RemovalReader(SystemBinder binder, List<Removal> values, List<ulong> missed) : SystemBody
    {
        private readonly RemovedHandle<Position> _handle = binder.Removed<Position>(0);
        protected override void Run(SystemRun run)
        {
            var reader = _handle.Read(run);
            missed.Add(reader.MissedCount);
            foreach (var removal in reader)
                values.Add(removal);
        }
    }

    private sealed class EmptyBody : SystemBody
    {
        protected override void Run(SystemRun run) { }
    }
}
