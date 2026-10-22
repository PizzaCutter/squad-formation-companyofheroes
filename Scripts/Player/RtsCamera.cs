using Godot;

/// <summary>
/// RTS camera: WASD / arrow keys pan, Q/E or middle mouse drag rotate, mouse wheel zooms.
/// Hold Shift to pan faster. Pitch flattens as you zoom in.
/// Runs on real time, so it stays smooth at any time scale, including paused.
/// Keys are physical, so on AZERTY the pan keys are ZQSD.
/// </summary>
public partial class RtsCamera : Node3D
{
    [Export] public float PanSpeed { get; set; } = 22f;
    [Export] public float RotateSpeedDegrees { get; set; } = 100f;
    [Export] public float MouseRotateDegreesPerPixel { get; set; } = 0.3f;
    [Export] public float ZoomMin { get; set; } = 6f;
    [Export] public float ZoomMax { get; set; } = 90f;
    [Export] public float Zoom { get; set; } = 32f;
    [Export] public float PitchNearDegrees { get; set; } = 30f;
    [Export] public float PitchFarDegrees { get; set; } = 62f;
    [Export] public float Smoothing { get; set; } = 10f;
    [Export] public Rect2 Bounds { get; set; } = new(-70f, -50f, 150f, 100f);

    public Camera3D Camera { get; private set; }

    private Vector3 _focus;
    private float _yaw;
    private float _zoom;
    private Vector3 _targetFocus;
    private float _targetYaw;
    private float _targetZoom;

    private bool _rotating;
    private ulong _lastTicks;

    public override void _Ready()
    {
        Camera = GetNode<Camera3D>("Camera3D");
        ProcessMode = ProcessModeEnum.Always;

        _focus = _targetFocus = GlobalPosition;
        _yaw = _targetYaw = GlobalRotation.Y;
        _zoom = _targetZoom = Zoom;
        _lastTicks = Time.GetTicksUsec();
        Apply();
    }

    /// <summary>Where the mouse ray meets the ground plane y = <paramref name="height"/>.</summary>
    public Vector3? MouseOnPlane(Vector2 screen, float height = 0f)
    {
        Vector3 origin = Camera.ProjectRayOrigin(screen);
        Vector3 dir = Camera.ProjectRayNormal(screen);
        if (Mathf.Abs(dir.Y) < 0.0001f)
        {
            return null;
        }
        float t = (height - origin.Y) / dir.Y;
        return t > 0f ? origin + dir * t : null;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed)
            {
                _targetZoom = Mathf.Clamp(_targetZoom * 0.88f, ZoomMin, ZoomMax);
            }
            else if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed)
            {
                _targetZoom = Mathf.Clamp(_targetZoom / 0.88f, ZoomMin, ZoomMax);
            }
            else if (button.ButtonIndex == MouseButton.Middle)
            {
                _rotating = button.Pressed;
            }
        }
        else if (@event is InputEventMouseMotion motion && _rotating)
        {
            _targetYaw -= Mathf.DegToRad(motion.Relative.X * MouseRotateDegreesPerPixel);
        }
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        float dt = Mathf.Min((now - _lastTicks) / 1_000_000f, 0.1f);
        _lastTicks = now;

        Vector2 pan = Vector2.Zero;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) pan.Y -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) pan.Y += 1f;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) pan.X -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) pan.X += 1f;

        // Only take keyboard input when no Ctrl shortcut is being pressed (Ctrl+A selects all).
        if (pan != Vector2.Zero && !Input.IsKeyPressed(Key.Ctrl))
        {
            float speed = PanSpeed * (_targetZoom / 32f) * (Input.IsKeyPressed(Key.Shift) ? 2.5f : 1f);
            Vector3 move = new Basis(Vector3.Up, _targetYaw) * new Vector3(pan.X, 0f, pan.Y);
            _targetFocus += move.Normalized() * speed * dt;
        }

        if (Input.IsPhysicalKeyPressed(Key.Q)) _targetYaw += Mathf.DegToRad(RotateSpeedDegrees) * dt;
        if (Input.IsPhysicalKeyPressed(Key.E)) _targetYaw -= Mathf.DegToRad(RotateSpeedDegrees) * dt;

        _targetFocus.X = Mathf.Clamp(_targetFocus.X, Bounds.Position.X, Bounds.End.X);
        _targetFocus.Z = Mathf.Clamp(_targetFocus.Z, Bounds.Position.Y, Bounds.End.Y);

        float t = 1f - Mathf.Exp(-Smoothing * dt);
        _focus = _focus.Lerp(_targetFocus, t);
        _yaw = Mathf.LerpAngle(_yaw, _targetYaw, t);
        _zoom = Mathf.Lerp(_zoom, _targetZoom, t);
        Apply();
    }

    private void Apply()
    {
        float zoomT = Mathf.InverseLerp(ZoomMin, ZoomMax, _zoom);
        float pitch = Mathf.DegToRad(Mathf.Lerp(PitchNearDegrees, PitchFarDegrees, Mathf.Sqrt(zoomT)));

        GlobalPosition = _focus;
        GlobalRotation = new Vector3(0f, _yaw, 0f);
        Camera.Position = new Vector3(0f, Mathf.Sin(pitch) * _zoom, Mathf.Cos(pitch) * _zoom);
        Camera.Rotation = new Vector3(-pitch, 0f, 0f);
    }
}
