using System.Diagnostics;
using Archevolt;

namespace Archeoids;

internal sealed class Game : IDisposable
{
    public const float TickSeconds = 1f / 120;
    private readonly uint _seed;
    private World _world;
    private BoundSchedule _simulation;
    private BoundSchedule _presentation;

    public RenderFrame Frame { get; private set; }
    public Session State => _world.GetResource<Session>();
    public double UpdateMilliseconds { get; private set; }

    public Game(uint seed = 0xA2C401D)
    {
        _seed = seed;
        _world = CreateWorld(seed);
        _simulation = Simulation.Create(_world);
        _presentation = Presentation.Create(_world);
        Frame = _world.GetResource<RenderFrame>();
        _world.Spawn(Factory.Ship());
        Capture();
    }

    public static World CreateWorld(uint seed)
    {
        var world = new WorldBuilder().AddModule(GameEcs.Module)
            .Reserve(entities: 4096, commandBytesPerProducer: 65536, messagesPerType: 256).Build();
        world.InsertResource(new Controls());
        world.InsertResource(new FrameTime(TickSeconds));
        world.InsertResource(new Session { Lives = 3, WaveDelay = 0.8f });
        world.InsertResource(new RandomSource(seed));
        world.InsertResource(new RenderFrame());
        return world;
    }

    public void Step(Controls controls)
    {
        _world.InsertResource(controls);
        _world.AdvanceMessages();
        long start = Stopwatch.GetTimestamp();
        _simulation.Run();
        UpdateMilliseconds = UpdateMilliseconds * 0.9 + Stopwatch.GetElapsedTime(start).TotalMilliseconds * 0.1;
    }

    public void Capture() => _presentation.Run();

    public void Restart()
    {
        Dispose();
        _world = CreateWorld(_seed);
        _simulation = Simulation.Create(_world);
        _presentation = Presentation.Create(_world);
        Frame = _world.GetResource<RenderFrame>();
        _world.Spawn(Factory.Ship());
        UpdateMilliseconds = 0;
        Capture();
    }

    public void Dispose()
    {
        _presentation.Dispose();
        _simulation.Dispose();
        _world.Dispose();
    }
}
