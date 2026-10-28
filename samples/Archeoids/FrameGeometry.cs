using System.Numerics;
using System.Runtime.InteropServices;

namespace Archeoids;

// Shared by both hosts. Vertices are x, y, r, g, b, a in logical canvas coordinates.
internal sealed class FrameGeometry
{
    public const float CanvasHeight = 800;
    private static readonly Vector2 PlayfieldOffset = new(0, 104);
    private static readonly Vector4 Dim = new(0.31f, 0.43f, 0.49f, 1);
    private readonly List<float> _vertices = new(160_000);
    private readonly (Vector2 Position, float Brightness)[] _stars;
    public Span<float> Vertices => CollectionsMarshal.AsSpan(_vertices);

    public FrameGeometry()
    {
        var random = new Random(1982);
        _stars = Enumerable.Range(0, 180).Select(_ =>
            (new Vector2(random.NextSingle() * Arena.Width, random.NextSingle() * CanvasHeight),
             random.NextSingle() * 0.5f + 0.15f)).ToArray();
    }

    public void Build(RenderFrame frame, Session state, double seconds, bool paused)
    {
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

        for (int i = 0; i < 3; i++)
            DrawSprite(new(new(1082 + i * 45, 65), -MathF.PI / 2,
                new(Shape.Ship, 11, 0, i < state.Lives ? Factory.Cyan : Dim), i < state.Lives ? 1 : 0.2f), Vector2.Zero);

        if (paused || state.GameOver)
            Rect(0, 101, Arena.Width, 643, new(0.018f, 0.032f, 0.049f, 0.82f));
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

    public void Rect(float x, float y, float width, float height, Vector4 color)
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
}
