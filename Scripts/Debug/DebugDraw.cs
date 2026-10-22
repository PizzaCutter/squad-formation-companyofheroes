using System.Collections.Generic;
using Godot;

/// <summary>
/// Immediate-mode debug drawing (autoload). Call from anywhere, any frame:
/// <code>DebugDraw.Arrow(leader.GlobalPosition, futurePos, Colors.Yellow);</code>
///
/// Lifetime: with duration 0 a shape lives exactly one tick. Shapes submitted from
/// _PhysicsProcess survive until the next physics tick, so they don't flicker when the
/// render rate is higher than the physics rate. Shapes with duration > 0 live that many
/// seconds of game time, so they freeze while the game is paused.
///
/// <see cref="Enabled"/> is the "show formation debug" toggle (F1). DebugDraw itself does
/// not check it, so UI feedback like order markers stays visible; check it yourself
/// around your formation visualisation.
/// </summary>
public partial class DebugDraw : Node
{
    public static bool Enabled { get; set; } = true;

    private enum Life { PhysicsTick, Frame, Timed }

    private struct Segment
    {
        public Vector3 A, B;
        public Color Color;
        public Life Life;
        public ulong Tick;
        public double ExpiresAt;
    }

    private struct TextEntry
    {
        public Vector3 Position;
        public string Text;
        public Color Color;
        public Life Life;
        public ulong Tick;
        public double ExpiresAt;
    }

    private static DebugDraw _instance;

    private readonly List<Segment> _segments = new();
    private readonly List<TextEntry> _texts = new();
    private readonly List<Label3D> _labelPool = new();
    private LineBatch _batch;
    private ulong _physicsTick;
    private double _time;

    public override void _EnterTree()
    {
        _instance = this;
        ProcessMode = ProcessModeEnum.Always;
        // Clear stale physics shapes before anyone else submits new ones this tick...
        ProcessPhysicsPriority = int.MinValue;
        // ...and draw after everyone has submitted this frame.
        ProcessPriority = int.MaxValue;
    }

    public override void _Ready()
    {
        _batch = new LineBatch(drawOnTop: true) { Name = "Lines" };
        AddChild(_batch);
    }

    public override void _ExitTree()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _physicsTick++;
        _segments.RemoveAll(s => s.Life == Life.PhysicsTick && s.Tick < _physicsTick);
        _texts.RemoveAll(t => t.Life == Life.PhysicsTick && t.Tick < _physicsTick);
    }

    public override void _Process(double delta)
    {
        _time += delta;

        _batch.Begin();
        foreach (Segment s in _segments)
        {
            _batch.Add(s.A, s.B, s.Color);
        }
        _batch.End();

        for (int i = 0; i < _texts.Count; i++)
        {
            Label3D label = GetLabel(i);
            label.Visible = true;
            label.GlobalPosition = _texts[i].Position;
            label.Text = _texts[i].Text;
            label.Modulate = _texts[i].Color;
        }
        for (int i = _texts.Count; i < _labelPool.Count; i++)
        {
            _labelPool[i].Visible = false;
        }

        _segments.RemoveAll(s => s.Life == Life.Frame || (s.Life == Life.Timed && s.ExpiresAt <= _time));
        _texts.RemoveAll(t => t.Life == Life.Frame || (t.Life == Life.Timed && t.ExpiresAt <= _time));
    }

    // ---------------------------------------------------------------- public API

    public static void Line(Vector3 a, Vector3 b, Color color, float duration = 0f)
    {
        if (_instance == null)
        {
            return;
        }
        _instance.AddSegment(a, b, color, duration);
    }

    public static void Arrow(Vector3 from, Vector3 to, Color color, float headSize = 0.35f, float duration = 0f)
    {
        Line(from, to, color, duration);
        Vector3 dir = to - from;
        if (dir.LengthSquared() < 0.0001f)
        {
            return;
        }
        dir = dir.Normalized();
        Vector3 side = dir.Cross(Vector3.Up);
        if (side.LengthSquared() < 0.0001f)
        {
            side = Vector3.Right;
        }
        side = side.Normalized();
        Vector3 back = to - dir * headSize;
        Line(to, back + side * headSize * 0.6f, color, duration);
        Line(to, back - side * headSize * 0.6f, color, duration);
    }

    /// <summary>Horizontal circle on the XZ plane.</summary>
    public static void Circle(Vector3 center, float radius, Color color, int segments = 24, float duration = 0f)
    {
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Line(prev, next, color, duration);
            prev = next;
        }
    }

    public static void Sphere(Vector3 center, float radius, Color color, float duration = 0f)
    {
        const int segments = 16;
        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.Tau * i / segments;
            float a1 = Mathf.Tau * (i + 1) / segments;
            Vector2 p0 = new(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius);
            Vector2 p1 = new(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius);
            Line(center + new Vector3(p0.X, 0, p0.Y), center + new Vector3(p1.X, 0, p1.Y), color, duration);
            Line(center + new Vector3(p0.X, p0.Y, 0), center + new Vector3(p1.X, p1.Y, 0), color, duration);
            Line(center + new Vector3(0, p0.X, p0.Y), center + new Vector3(0, p1.X, p1.Y), color, duration);
        }
    }

    public static void Cross(Vector3 position, float size, Color color, float duration = 0f)
    {
        float h = size * 0.5f;
        Line(position - new Vector3(h, 0, 0), position + new Vector3(h, 0, 0), color, duration);
        Line(position - new Vector3(0, h, 0), position + new Vector3(0, h, 0), color, duration);
        Line(position - new Vector3(0, 0, h), position + new Vector3(0, 0, h), color, duration);
    }

    public static void Path(IReadOnlyList<Vector3> points, Color color, float duration = 0f)
    {
        if (points == null)
        {
            return;
        }
        for (int i = 1; i < points.Count; i++)
        {
            Line(points[i - 1], points[i], color, duration);
        }
    }

    /// <summary>Billboarded text that is always drawn on top.</summary>
    public static void Text(Vector3 position, string text, Color color, float duration = 0f)
    {
        if (_instance == null)
        {
            return;
        }
        Life life = _instance.LifeFor(duration);
        _instance._texts.Add(new TextEntry
        {
            Position = position,
            Text = text,
            Color = color,
            Life = life,
            Tick = _instance._physicsTick,
            ExpiresAt = _instance._time + duration,
        });
    }

    // ---------------------------------------------------------------- internals

    private Life LifeFor(float duration)
    {
        if (duration > 0f)
        {
            return Life.Timed;
        }
        return Engine.IsInPhysicsFrame() ? Life.PhysicsTick : Life.Frame;
    }

    private void AddSegment(Vector3 a, Vector3 b, Color color, float duration)
    {
        _segments.Add(new Segment
        {
            A = a,
            B = b,
            Color = color,
            Life = LifeFor(duration),
            Tick = _physicsTick,
            ExpiresAt = _time + duration,
        });
    }

    private Label3D GetLabel(int index)
    {
        while (_labelPool.Count <= index)
        {
            Label3D label = new()
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                FixedSize = true,
                PixelSize = 0.0012f,
                FontSize = 28,
                OutlineSize = 8,
                RenderPriority = 11,
                OutlineRenderPriority = 10,
            };
            AddChild(label);
            _labelPool.Add(label);
        }
        return _labelPool[index];
    }
}
