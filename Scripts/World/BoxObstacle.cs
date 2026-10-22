using System.Collections.Generic;
using Godot;

/// <summary>
/// The level building block: a box with a mesh and collision, sized from the inspector.
/// The node's origin is the bottom-center of the box, so y = 0 sits on the ground.
///
/// Ground pieces (<see cref="IsGround"/>) are on the World layer and become navmesh.
/// Everything else is on the Obstacles layer and cuts a hole in the navmesh through a
/// NavigationObstacle3D. That keeps rooftops and wall tops from becoming walkable islands.
/// </summary>
[Tool]
public partial class BoxObstacle : StaticBody3D
{
    [Export] public Vector3 Size { get => _size; set { _size = value; Rebuild(); } }
    [Export] public Color Color { get => _color; set { _color = value; Rebuild(); } }
    [Export] public Material MaterialOverride { get => _material; set { _material = value; Rebuild(); } }
    [Export] public bool IsGround { get => _isGround; set { _isGround = value; Rebuild(); } }

    /// <summary>Generates <see cref="CoverPoint"/>s along each side at runtime.</summary>
    [Export] public bool ProvidesCover { get; set; }
    [Export] public float CoverSpacing { get; set; } = 1.3f;

    /// <summary>Can be blown up with X in play mode. Triggers a navmesh rebake.</summary>
    [Export] public bool Destructible { get; set; }

    /// <summary>Distance from the wall face to a generated cover point.</summary>
    private const float CoverStandoff = 0.75f;

    private Vector3 _size = new(2f, 1f, 2f);
    private Color _color = new(0.6f, 0.6f, 0.6f);
    private Material _material;
    private bool _isGround;

    private MeshInstance3D _mesh;
    private CollisionShape3D _shape;
    private NavigationObstacle3D _navObstacle;
    private readonly List<CoverPoint> _coverPoints = new();
    private bool _destroyed;

    public override void _Ready()
    {
        Rebuild();
        if (!Engine.IsEditorHint() && ProvidesCover)
        {
            GenerateCoverPoints();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationTransformChanged)
        {
            SyncNavObstacle();
        }
    }

    /// <summary>Removes the obstacle from the world and rebakes the navmesh. Returns false if it can't be destroyed.</summary>
    public bool Destroy()
    {
        if (!Destructible || _destroyed)
        {
            return false;
        }
        _destroyed = true;

        // Pull everything out of the tree right now, not at end of frame, so the rebake below
        // no longer sees this obstacle.
        CollisionLayer = 0;
        if (_navObstacle != null)
        {
            RemoveChild(_navObstacle);
            _navObstacle.QueueFree();
            _navObstacle = null;
        }
        foreach (CoverPoint point in _coverPoints)
        {
            point.GetParent()?.RemoveChild(point);
            point.QueueFree();
        }
        _coverPoints.Clear();

        LevelNavigation.RequestRebake();

        Tween tween = CreateTween();
        tween.TweenProperty(_mesh, "position:y", -_size.Y * 0.5f, 0.6f)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(QueueFree));
        return true;
    }

    private void Rebuild()
    {
        if (!IsInsideTree() || _destroyed)
        {
            return;
        }

        _mesh?.QueueFree();
        _shape?.QueueFree();
        _navObstacle?.QueueFree();
        _navObstacle = null;

        Vector3 center = new(0f, _size.Y * 0.5f, 0f);

        _mesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = _size },
            Position = center,
            MaterialOverride = _material ?? new StandardMaterial3D { AlbedoColor = _color, Roughness = 0.9f },
        };
        AddChild(_mesh, false, InternalMode.Front);

        _shape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = _size },
            Position = center,
        };
        AddChild(_shape, false, InternalMode.Front);

        CollisionLayer = _isGround ? Layers.World : Layers.Obstacles;
        CollisionMask = 0;

        if (!_isGround)
        {
            _navObstacle = new NavigationObstacle3D
            {
                AffectNavigationMesh = true,
                AvoidanceEnabled = false,
                Height = _size.Y,
                // The baker ignores node rotation, so the obstacle lives unrotated in world space
                // and SyncNavObstacle bakes the rotation into its vertices.
                TopLevel = true,
            };
            AddChild(_navObstacle, false, InternalMode.Front);
            SetNotifyTransform(true);
            SyncNavObstacle();
        }
    }

    private void SyncNavObstacle()
    {
        if (_navObstacle == null || !IsInsideTree())
        {
            return;
        }

        Basis yaw = new(Vector3.Up, GlobalRotation.Y);
        float hx = _size.X * 0.5f;
        float hz = _size.Z * 0.5f;
        _navObstacle.GlobalTransform = new Transform3D(Basis.Identity, GlobalPosition);
        _navObstacle.Vertices = new[]
        {
            yaw * new Vector3(-hx, 0f, -hz),
            yaw * new Vector3(hx, 0f, -hz),
            yaw * new Vector3(hx, 0f, hz),
            yaw * new Vector3(-hx, 0f, hz),
        };
    }

    private void GenerateCoverPoints()
    {
        float hx = _size.X * 0.5f;
        float hz = _size.Z * 0.5f;

        // (side center, outward normal, direction along the side, side length), all local.
        (Vector3 center, Vector3 normal, Vector3 along, float length)[] sides =
        {
            (new Vector3(0, 0, -hz), Vector3.Forward, Vector3.Right, _size.X),
            (new Vector3(0, 0, hz), Vector3.Back, Vector3.Right, _size.X),
            (new Vector3(-hx, 0, 0), Vector3.Left, Vector3.Back, _size.Z),
            (new Vector3(hx, 0, 0), Vector3.Right, Vector3.Back, _size.Z),
        };

        foreach (var (sideCenter, normal, along, length) in sides)
        {
            if (length < 0.9f)
            {
                continue;
            }
            int count = Mathf.Max(1, Mathf.RoundToInt(length / CoverSpacing));
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count * length - length * 0.5f;
                Vector3 local = sideCenter + normal * CoverStandoff + along * t;
                CoverPoint point = new() { Name = $"Cover{_coverPoints.Count}" };
                AddChild(point);
                point.Position = local;
                // The point faces the wall, i.e. over the cover towards the threat.
                point.Basis = Basis.LookingAt(-normal, Vector3.Up);
                point.Source = this;
                _coverPoints.Add(point);
            }
        }
    }
}
