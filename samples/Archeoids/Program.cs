using System.Globalization;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Archeoids;

internal static class Program
{
    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args is ["--verify"]) return Verification.Run();
        bool smoke = args is ["--smoke-test"];
        if (args.Length > 0 && !smoke)
        {
            Console.Error.WriteLine("Usage: Archeoids [--verify | --smoke-test]");
            return 2;
        }

        WindowOptions options = WindowOptions.Default;
        options.Title = "Archeoids | Archevolt + Silk.NET";
        options.Size = new Vector2D<int>(1280, 800);
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3));
        options.VSync = true;
        options.UpdatesPerSecond = 120;
        options.PreferredDepthBufferBits = 0;
        using IWindow window = Window.Create(options);
        using var game = new Game();
        IInputContext? input = null;
        GL? gl = null;
        VectorRenderer? renderer = null;
        double accumulator = 0;
        double elapsed = 0;
        bool paused = false;
        int ticks = 0;

        window.Load += () =>
        {
            gl = GL.GetApi(window);
            renderer = new VectorRenderer(gl);
            input = window.CreateInput();
            foreach (var keyboard in input.Keyboards)
                keyboard.KeyDown += (_, key, _) =>
                {
                    if (key == Key.Escape) window.Close();
                    if (key == Key.P) { paused = !paused; accumulator = 0; }
                    if (key == Key.R) { game.Restart(); paused = false; accumulator = 0; }
                };
            Console.WriteLine($"OpenGL {gl.GetStringS(StringName.Version)} / {gl.GetStringS(StringName.Renderer)}");
        };

        window.Update += delta =>
        {
            if (paused) return;
            accumulator += Math.Min(delta, 0.1);
            while (accumulator >= Game.TickSeconds)
            {
                Controls controls = smoke ? DemoControls(ticks) : ReadControls(input!);
                game.Step(controls);
                ticks++;
                elapsed += Game.TickSeconds;
                accumulator -= Game.TickSeconds;
            }
        };

        window.Render += _ =>
        {
            game.Capture();
            renderer!.Draw(game.Frame, game.State, game.UpdateMilliseconds, elapsed, paused,
                window.FramebufferSize.X, window.FramebufferSize.Y);
            if (smoke && ticks >= 720)
            {
                string path = Path.GetFullPath("artifacts/archeoids/smoke.bmp");
                Verification.Capture(gl!, window.FramebufferSize.X, window.FramebufferSize.Y, path);
                if (gl!.GetError() != GLEnum.NoError) throw new InvalidOperationException("OpenGL reported an error.");
                Console.WriteLine($"graphics=ok ticks={ticks} entities={game.Frame.Sprites.Count} score={game.State.Score} screenshot={path}");
                window.Close();
            }
        };

        window.Closing += () =>
        {
            renderer?.Dispose();
            input?.Dispose();
            gl?.Dispose();
        };
        window.Run();
        return 0;
    }

    private static Controls ReadControls(IInputContext input)
    {
        bool left = Down(Key.A) || Down(Key.Left);
        bool right = Down(Key.D) || Down(Key.Right);
        return new((right ? 1 : 0) - (left ? 1 : 0), Down(Key.W) || Down(Key.Up), Down(Key.Space));
        bool Down(Key key) => input.Keyboards.Any(keyboard => keyboard.IsKeyPressed(key));
    }

    private static Controls DemoControls(int tick) => new(0.38f, tick % 480 < 220, true);
}
