using Godot;

/// <summary>
/// One soldier. Knows how to path to a point, turn and die. It has no formation knowledge;
/// the owning <see cref="Squad"/> decides where it should go via <see cref="MoveTo"/>.
/// </summary>
public partial class Soldier : CharacterBody3D
{
    [Signal] public delegate void DiedEventHandler(Soldier soldier);

    public const string Group = "soldiers";

    /// <summary>Toggled with F2. Shows the role label above each soldier.</summary>
    public static bool ShowLabels { get; private set; } = true;

    [Export] public float MoveSpeed { get; set; } = 3.5f;
    [Export] public float Acceleration { get; set; } = 14f;
    [Export] public float TurnSpeed { get; set; } = 10f;

    /// <summary>
    /// Multiplies <see cref="MoveSpeed"/>. This is the hook for catching up or holding back
    /// to keep formation. Clamped to [0, 2].
    /// </summary>
    public float SpeedMultiplier
    {
        get => _speedMultiplier;
        set => _speedMultiplier = Mathf.Clamp(value, 0f, 2f);
    }

    /// <summary>Direction to turn to once the soldier stops moving. Null keeps the current heading.</summary>
    public Vector3? DesiredFacing { get; set; }

    public Squad Squad { get; internal set; }
    public bool IsAlive { get; private set; } = true;
    public NavigationAgent3D Agent { get; private set; }

    public SquadElement Element
    {
        get => _element;
        set { _element = value; RefreshVisuals(); }
    }

    public SquadRole Role
    {
        get => _role;
        set { _role = value; RefreshVisuals(); }
    }

    /// <summary>Horizontal unit vector the soldier is facing.</summary>
    public Vector3 Forward => (-GlobalBasis.Z).Flat().Normalized();

    public Vector3 Target => Agent.TargetPosition;
    public bool HasOrder => _hasOrder;
    public bool HasArrived => !_hasOrder || Agent.IsNavigationFinished();
    public float CurrentSpeed => Velocity.Flat().Length();

    /// <summary>The path the agent is currently following, in world space.</summary>
    public Vector3[] CurrentPath => Agent.GetCurrentNavigationPath();

    /// <summary>Index into <see cref="CurrentPath"/> of the waypoint the agent is walking towards.</summary>
    public int CurrentPathIndex => Agent.GetCurrentNavigationPathIndex();

    private static readonly Color CoreColor = new(0.30f, 0.50f, 0.95f);
    private static readonly Color LeftColor = new(0.35f, 0.80f, 0.35f);
    private static readonly Color RightColor = new(0.95f, 0.60f, 0.20f);
    private static readonly Color UnassignedColor = new(0.55f, 0.55f, 0.55f);

    private float _speedMultiplier = 1f;
    private bool _hasOrder;
    private SquadElement _element;
    private SquadRole _role;
    private Vector3 _avoidanceVelocity;

    private Node3D _visual;
    private MeshInstance3D _selectionRing;
    private Label3D _roleLabel;
    private readonly StandardMaterial3D _bodyMaterial = new();
    private readonly StandardMaterial3D _headMaterial = new();

    public override void _Ready()
    {
        AddToGroup(Group);

        Agent = GetNode<NavigationAgent3D>("NavigationAgent3D");
        Agent.VelocityComputed += v => _avoidanceVelocity = v;

        _visual = GetNode<Node3D>("Visual");
        _selectionRing = GetNode<MeshInstance3D>("SelectionRing");
        _roleLabel = GetNode<Label3D>("RoleLabel");
        GetNode<MeshInstance3D>("Visual/Body").MaterialOverride = _bodyMaterial;
        GetNode<MeshInstance3D>("Visual/Head").MaterialOverride = _headMaterial;

        _roleLabel.Visible = ShowLabels;
        RefreshVisuals();
    }

    // ---------------------------------------------------------------- orders

    /// <summary>
    /// Path to <paramref name="target"/>. Safe to call every tick with a new goal; the agent
    /// repaths whenever the target changes.
    /// </summary>
    public void MoveTo(Vector3 target)
    {
        if (!IsAlive)
        {
            return;
        }
        Agent.TargetPosition = target;
        _hasOrder = true;
    }

    public void Stop()
    {
        _hasOrder = false;
        Agent.TargetPosition = GlobalPosition;
    }

    public void SetSelected(bool selected) => _selectionRing.Visible = selected && IsAlive;

    // ---------------------------------------------------------------- movement

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        Vector3 desired = Vector3.Zero;
        if (_hasOrder && !Agent.IsNavigationFinished())
        {
            Vector3 next = Agent.GetNextPathPosition();
            Vector3 toNext = (next - GlobalPosition).Flat();
            if (toNext.LengthSquared() > 0.0001f)
            {
                desired = toNext.Normalized() * MoveSpeed * _speedMultiplier;
            }
        }

        if (Agent.AvoidanceEnabled)
        {
            Agent.Velocity = desired;
            desired = _avoidanceVelocity;
        }

        Vector3 horizontal = Velocity.Flat().MoveToward(desired, Acceleration * dt);
        float vertical = IsOnFloor() ? 0f : Velocity.Y - 9.8f * dt;
        Velocity = new Vector3(horizontal.X, vertical, horizontal.Z);
        MoveAndSlide();

        UpdateFacing(dt);
    }

    private void UpdateFacing(float dt)
    {
        Vector3 flatVelocity = Velocity.Flat();
        Vector3? lookDir = null;
        if (flatVelocity.LengthSquared() > 0.04f)
        {
            lookDir = flatVelocity;
        }
        else if (DesiredFacing is Vector3 facing && facing.Flat().LengthSquared() > 0.0001f)
        {
            lookDir = facing.Flat();
        }

        if (lookDir is Vector3 dir)
        {
            float targetYaw = Mathf.Atan2(-dir.X, -dir.Z);
            Vector3 rot = Rotation;
            rot.Y = Mathf.LerpAngle(rot.Y, targetYaw, 1f - Mathf.Exp(-TurnSpeed * dt));
            Rotation = rot;
        }
    }

    // ---------------------------------------------------------------- death

    /// <summary>Kills the soldier. <see cref="Died"/> fires immediately; the body stays for a few seconds.</summary>
    public void Kill()
    {
        if (!IsAlive)
        {
            return;
        }
        IsAlive = false;
        _hasOrder = false;
        SetPhysicsProcess(false);
        CollisionLayer = 0;
        CollisionMask = 0;
        Agent.AvoidanceEnabled = false;
        _selectionRing.Visible = false;
        _roleLabel.Visible = false;
        RemoveFromGroup(Group);

        EmitSignal(SignalName.Died, this);

        Tween tween = CreateTween();
        tween.TweenProperty(_visual, "rotation:x", -Mathf.Pi / 2f, 0.4f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tween.TweenInterval(3.0);
        tween.TweenProperty(_visual, "position:y", -0.6f, 1.0);
        tween.TweenCallback(Callable.From(QueueFree));
    }

    // ---------------------------------------------------------------- visuals

    public static void SetShowLabels(SceneTree tree, bool show)
    {
        ShowLabels = show;
        foreach (Node node in tree.GetNodesInGroup(Group))
        {
            if (node is Soldier soldier && soldier.IsAlive)
            {
                soldier._roleLabel.Visible = show;
            }
        }
    }

    private void RefreshVisuals()
    {
        if (_roleLabel == null)
        {
            return;
        }

        Color elementColor = _element switch
        {
            SquadElement.Core => CoreColor,
            SquadElement.LeftFlank => LeftColor,
            SquadElement.RightFlank => RightColor,
            _ => UnassignedColor,
        };
        _bodyMaterial.AlbedoColor = elementColor;
        _headMaterial.AlbedoColor = _role switch
        {
            SquadRole.SquadLeader => new Color(1.0f, 0.85f, 0.2f),
            SquadRole.ElementLeader => new Color(0.95f, 0.95f, 0.95f),
            _ => elementColor.Darkened(0.35f),
        };

        string elementTag = _element switch
        {
            SquadElement.Core => "Core",
            SquadElement.LeftFlank => "LeftFlank",
            SquadElement.RightFlank => "RightFlank",
            _ => "?",
        };
        string roleTag = _role switch
        {
            SquadRole.SquadLeader => "SquadLeader",
            SquadRole.ElementLeader => "ElementLeader",
            SquadRole.Follower => "Follower",
            _ => "",
        };
        _roleLabel.Text = roleTag.Length > 0 ? $"{elementTag} {roleTag}" : elementTag;
        _roleLabel.Modulate = elementColor.Lightened(0.3f);
    }
}
