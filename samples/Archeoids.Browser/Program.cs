using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Archeoids;

[SupportedOSPlatform("browser")]
internal static partial class Program
{
    private static readonly Game Game = new();
    private static readonly FrameGeometry Geometry = new();
    private static double _accumulator;
    private static double _seconds;
    private static bool _paused;

    public static void Main() { }

    [JSExport]
    public static void Frame(double deltaSeconds, int turn, bool thrust, bool fire)
    {
        if (!_paused)
        {
            _accumulator += Math.Clamp(deltaSeconds, 0, 0.1);
            while (_accumulator >= Game.TickSeconds)
            {
                Game.Step(new Controls(turn, thrust, fire));
                _seconds += Game.TickSeconds;
                _accumulator -= Game.TickSeconds;
            }
        }

        Game.Capture();
        Session state = Game.State;
        Geometry.Build(Game.Frame, state, _seconds, _paused);
        Render(MemoryMarshal.AsBytes(Geometry.Vertices), state.Score, state.Wave, state.Lives,
            Game.Frame.Sprites.Count, Game.UpdateMilliseconds, _paused);
    }

    [JSExport]
    public static void TogglePause()
    {
        _paused = !_paused;
        ResetClock();
    }

    [JSExport]
    public static void Restart()
    {
        Game.Restart();
        _paused = false;
        _seconds = 0;
        ResetClock();
    }

    [JSExport]
    public static void ResetClock() => _accumulator = 0;

    [JSImport("render", "archeoids")]
    private static partial void Render([JSMarshalAs<JSType.MemoryView>] Span<byte> vertices,
        int score, int wave, int lives, int entities, double milliseconds, bool paused);
}
