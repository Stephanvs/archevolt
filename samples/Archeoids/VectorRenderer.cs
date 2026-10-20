using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Archeoids;

internal sealed class VectorRenderer : IDisposable
{
    private const float CanvasHeight = 800;
    private static readonly Vector2 PlayfieldOffset = new(0, 104);
    private readonly GL _gl;
    private readonly uint _program;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly List<float> _vertices = new(160_000);
    private readonly (Vector2 Position, float Brightness)[] _stars;
    private static readonly Vector4 Dim = new(0.31f, 0.43f, 0.49f, 1);
    private static readonly Vector4 White = new(0.86f, 0.93f, 0.93f, 1);

    public unsafe VectorRenderer(GL gl)
    {
        _gl = gl;
        uint vertex = Compile(ShaderType.VertexShader, """
            #version 330 core
            layout (location = 0) in vec2 position;
            layout (location = 1) in vec4 color;
            out vec4 tint;
            void main() {
                gl_Position = vec4(position.x / 640.0 - 1.0, 1.0 - position.y / 400.0, 0.0, 1.0);
                tint = color;
            }
            """);
        uint fragment = Compile(ShaderType.FragmentShader, """
            #version 330 core
            in vec4 tint;
            out vec4 outputColor;
            void main() { outputColor = tint; }
            """);
        _program = _gl.CreateProgram();
        _gl.AttachShader(_program, vertex);
        _gl.AttachShader(_program, fragment);
        _gl.LinkProgram(_program);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
        _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0) throw new InvalidOperationException(_gl.GetProgramInfoLog(_program));
        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
        _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(2 * sizeof(float)));
        _gl.EnableVertexAttribArray(0);
        _gl.EnableVertexAttribArray(1);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        var random = new Random(1982);
        _stars = Enumerable.Range(0, 180).Select(_ =>
            (new Vector2(random.NextSingle() * Arena.Width, random.NextSingle() * CanvasHeight),
             random.NextSingle() * 0.5f + 0.15f)).ToArray();
    }

    public void Draw(RenderFrame frame, Session state, double milliseconds, double seconds, bool paused, int width, int height)
    {
        _gl.Viewport(0, 0, (uint)Math.Max(width, 0), (uint)Math.Max(height, 0));
        _gl.ClearColor(0.018f, 0.032f, 0.049f, 1);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        if (width <= 0 || height <= 0) return;
        float scale = Math.Min(width / Arena.Width, height / CanvasHeight);
        int viewportWidth = (int)(Arena.Width * scale);
        int viewportHeight = (int)(CanvasHeight * scale);
        _gl.Viewport((width - viewportWidth) / 2, (height - viewportHeight) / 2, (uint)viewportWidth, (uint)viewportHeight);
        _vertices.Clear();

        for (int x = 0; x <= Arena.Width; x += 80)
            Line(new(x, 100), new(x, 744), 0.6f, new(0.16f, 0.27f, 0.32f, 0.13f));
        for (int y = 104; y < 744; y += 80)
            Line(new(0, y), new(Arena.Width, y), 0.6f, new(0.16f, 0.27f, 0.32f, 0.13f));
        foreach (var star in _stars)
        {
            float flicker = 0.8f + 0.2f * MathF.Sin((float)seconds * 0.7f + star.Position.X);
            Rect(star.Position.X, star.Position.Y, 1.4f, 1.4f, new(0.6f, 0.8f, 0.9f, star.Brightness * flicker));
        }

        foreach (var sprite in frame.Sprites)
        {
            DrawSprite(sprite, PlayfieldOffset);
            float r = sprite.Appearance.Radius + 4;
            // Draw across seams as well as colliding across them.
            float dx = sprite.Position.X < r ? Arena.Width : sprite.Position.X > Arena.Width - r ? -Arena.Width : 0;
            float dy = sprite.Position.Y < r ? Arena.Height : sprite.Position.Y > Arena.Height - r ? -Arena.Height : 0;
            if (dx != 0) DrawSprite(sprite, PlayfieldOffset + new Vector2(dx, 0));
            if (dy != 0) DrawSprite(sprite, PlayfieldOffset + new Vector2(0, dy));
            if (dx != 0 && dy != 0) DrawSprite(sprite, PlayfieldOffset + new Vector2(dx, dy));
        }

        Rect(0, 0, Arena.Width, 101, new(0.025f, 0.045f, 0.062f, 0.97f));
        Rect(0, 744, Arena.Width, 56, new(0.025f, 0.045f, 0.062f, 0.97f));
        Line(new(32, 100), new(1248, 100), 1, new(0.3f, 0.46f, 0.5f, 0.55f));
        Line(new(32, 744), new(1248, 744), 1, new(0.3f, 0.46f, 0.5f, 0.55f));

        Text("ARCHEOIDS", 34, 24, 4.8f, Factory.Amber);
        Text("ARCHEVOLT / SILK.NET", 36, 70, 1.5f, Dim);
        Text("SCORE", 590, 27, 1.4f, Dim);
        Text(state.Score.ToString("D6"), 590, 49, 3.4f, White);
        Text("SECTOR", 830, 27, 1.4f, Dim);
        Text(Math.Max(state.Wave, 1).ToString("D2"), 830, 49, 3.4f, White);
        Text("HULL", 1070, 27, 1.4f, Dim);
        for (int i = 0; i < 3; i++)
            DrawSprite(new(new(1082 + i * 45, 65), -MathF.PI / 2,
                new(Shape.Ship, 11, 0, i < state.Lives ? Factory.Cyan : Dim), i < state.Lives ? 1 : 0.2f), Vector2.Zero);

        Text("A/D  TURN    W  THRUST    SPACE  FIRE    P  PAUSE    R  RESTART", 36, 766, 1.3f, Dim);
        string stats = $"{frame.Sprites.Count} ENTITIES / {milliseconds:F2} MS";
        Text(stats, 1246 - TextWidth(stats, 1.3f), 766, 1.3f, Factory.Cyan * new Vector4(1, 1, 1, 0.8f));

        if (paused || state.GameOver)
        {
            Rect(0, 101, Arena.Width, 643, new(0.018f, 0.032f, 0.049f, 0.82f));
            string title = paused ? "PAUSED" : "SIGNAL LOST";
            CenterText(title, 350, 7, paused ? White : Factory.Amber);
            CenterText(paused ? "PRESS P TO RESUME" : "PRESS R TO LAUNCH AGAIN", 430, 2, Dim);
        }
        else if (state.Wave == 0)
        {
            CenterText("CLEAR THE SECTOR", 250, 3, White);
            CenterText("THRUST. DRIFT. SURVIVE.", 292, 1.5f, Dim);
        }
        Flush();
    }

    private void DrawSprite(Sprite sprite, Vector2 offset)
    {
        Vector2 p = sprite.Position + offset;
        Appearance a = sprite.Appearance;
        Vector4 color = a.Color with { W = a.Color.W * sprite.Opacity };
        Vector2 forward = Arena.Direction(sprite.Angle);
        Vector2 side = new(-forward.Y, forward.X);
        if (a.Shape == Shape.Ship)
        {
            Vector2 tip = p + forward * a.Radius;
            Vector2 left = p - forward * a.Radius * 0.8f + side * a.Radius * 0.72f;
            Vector2 right = p - forward * a.Radius * 0.8f - side * a.Radius * 0.72f;
            Vector2 tail = p - forward * a.Radius * 0.4f;
            Stroke(tip, left, color); Stroke(left, tail, color);
            Stroke(tail, right, color); Stroke(right, tip, color);
        }
        else if (a.Shape == Shape.Rock)
        {
            uint seed = a.Seed;
            Span<Vector2> points = stackalloc Vector2[11];
            for (int i = 0; i < points.Length; i++)
            {
                seed = seed * 1664525 + 1013904223;
                float radius = a.Radius * (0.76f + (seed >> 24) / 255f * 0.24f);
                points[i] = p + Arena.Direction(sprite.Angle + i * MathF.Tau / points.Length) * radius;
            }
            for (int i = 0; i < points.Length; i++) Stroke(points[i], points[(i + 1) % points.Length], color);
        }
        else if (a.Shape == Shape.Bullet) Stroke(p - forward * a.Radius, p + forward * a.Radius, color, 2);
        else
        {
            Rect(p.X - a.Radius * 2, p.Y - a.Radius * 2, a.Radius * 4, a.Radius * 4,
                color with { W = color.W * 0.09f });
            Rect(p.X - a.Radius / 2, p.Y - a.Radius / 2, a.Radius, a.Radius, color);
        }
    }

    private void Stroke(Vector2 a, Vector2 b, Vector4 color, float width = 1.25f)
    {
        Line(a, b, width * 4, color with { W = color.W * 0.075f });
        Line(a, b, width, color);
    }

    private void Line(Vector2 a, Vector2 b, float width, Vector4 color)
    {
        Vector2 delta = b - a;
        if (delta.LengthSquared() < 0.0001f) return;
        Vector2 n = Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * (width / 2);
        Triangle(a + n, b + n, b - n, color);
        Triangle(a + n, b - n, a - n, color);
    }

    private void Rect(float x, float y, float width, float height, Vector4 color)
    {
        Triangle(new(x, y), new(x + width, y), new(x + width, y + height), color);
        Triangle(new(x, y), new(x + width, y + height), new(x, y + height), color);
    }

    private void Triangle(Vector2 a, Vector2 b, Vector2 c, Vector4 color)
    {
        Vertex(a, color); Vertex(b, color); Vertex(c, color);
    }

    private void Vertex(Vector2 point, Vector4 color)
    {
        _vertices.Add(point.X); _vertices.Add(point.Y);
        _vertices.Add(color.X); _vertices.Add(color.Y); _vertices.Add(color.Z); _vertices.Add(color.W);
    }

    private unsafe void Flush()
    {
        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        ReadOnlySpan<float> vertices = CollectionsMarshal.AsSpan(_vertices);
        _gl.BufferData<float>(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StreamDraw);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(_vertices.Count / 6));
    }

    private uint Compile(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled == 0) throw new InvalidOperationException(_gl.GetShaderInfoLog(shader));
        return shader;
    }

    private static float TextWidth(string text, float size) => Math.Max(text.Length * 6 - 1, 0) * size;
    private void CenterText(string text, float y, float size, Vector4 color)
        => Text(text, (Arena.Width - TextWidth(text, size)) / 2, y, size, color);

    private void Text(string text, float x, float y, float size, Vector4 color)
    {
        foreach (char c in text)
        {
            ReadOnlySpan<byte> rows = Glyph(c);
            for (int row = 0; row < rows.Length; row++)
                for (int col = 0; col < 5; col++)
                    if ((rows[row] & (1 << (4 - col))) != 0)
                        Rect(x + col * size, y + row * size, size * 0.85f, size * 0.85f, color);
            x += size * 6;
        }
    }

    private static ReadOnlySpan<byte> Glyph(char c) => c switch
    {
        'A' => [14, 17, 17, 31, 17, 17, 17], 'B' => [30, 17, 17, 30, 17, 17, 30],
        'C' => [14, 17, 16, 16, 16, 17, 14], 'D' => [30, 17, 17, 17, 17, 17, 30],
        'E' => [31, 16, 16, 30, 16, 16, 31], 'F' => [31, 16, 16, 30, 16, 16, 16],
        'G' => [14, 17, 16, 23, 17, 17, 15], 'H' => [17, 17, 17, 31, 17, 17, 17],
        'I' => [14, 4, 4, 4, 4, 4, 14], 'J' => [7, 2, 2, 2, 18, 18, 12],
        'K' => [17, 18, 20, 24, 20, 18, 17], 'L' => [16, 16, 16, 16, 16, 16, 31],
        'M' => [17, 27, 21, 21, 17, 17, 17], 'N' => [17, 25, 21, 19, 17, 17, 17],
        'O' => [14, 17, 17, 17, 17, 17, 14], 'P' => [30, 17, 17, 30, 16, 16, 16],
        'Q' => [14, 17, 17, 17, 21, 18, 13], 'R' => [30, 17, 17, 30, 20, 18, 17],
        'S' => [15, 16, 16, 14, 1, 1, 30], 'T' => [31, 4, 4, 4, 4, 4, 4],
        'U' => [17, 17, 17, 17, 17, 17, 14], 'V' => [17, 17, 17, 17, 17, 10, 4],
        'W' => [17, 17, 17, 21, 21, 21, 10], 'X' => [17, 17, 10, 4, 10, 17, 17],
        'Y' => [17, 17, 10, 4, 4, 4, 4], 'Z' => [31, 1, 2, 4, 8, 16, 31],
        '0' => [14, 17, 19, 21, 25, 17, 14], '1' => [4, 12, 4, 4, 4, 4, 14],
        '2' => [14, 17, 1, 2, 4, 8, 31], '3' => [30, 1, 1, 14, 1, 1, 30],
        '4' => [2, 6, 10, 18, 31, 2, 2], '5' => [31, 16, 16, 30, 1, 1, 30],
        '6' => [14, 16, 16, 30, 17, 17, 14], '7' => [31, 1, 2, 4, 8, 8, 8],
        '8' => [14, 17, 17, 14, 17, 17, 14], '9' => [14, 17, 17, 15, 1, 1, 14],
        '/' => [1, 2, 2, 4, 8, 8, 16], '.' => [0, 0, 0, 0, 0, 12, 12],
        '-' => [0, 0, 0, 31, 0, 0, 0], ':' => [0, 12, 12, 0, 12, 12, 0],
        _ => []
    };

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
    }
}
