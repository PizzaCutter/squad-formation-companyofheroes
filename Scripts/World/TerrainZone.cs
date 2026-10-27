using Godot;

/// <summary>Which kind of formation the terrain calls for.</summary>
public enum FormationContext
{
    /// <summary>Open fields. Broad wedge.</summary>
    Open,
    /// <summary>City streets. Tight wedge.</summary>
    Urban,
    /// <summary>Alleys, narrow bridges. Little more than single file fits.</summary>
    Narrow,
}

/// <summary>
/// An Area3D on the TerrainZones layer that tags the ground it covers with a
/// <see cref="FormationContext"/>. Where zones overlap, the highest <see cref="ZonePriority"/> wins.
/// The origin is on the ground; the volume reaches 1 m below and 3 m above it.
/// </summary>
[Tool]
public partial class TerrainZone : Area3D
{
    public const string Group = "terrain_zones";

    [Export] public FormationContext Context { get => _context; set { _context = value; Rebuild(); } }
    [Export] public Vector2 Size { get => _size; set { _size = value; Rebuild(); } }
    [Export] public int ZonePriority { get => _priority; set { _priority = value; Rebuild(); } }
    [Export] public Formation FormationOverride = null; 

    private FormationContext _context;
    private Vector2 _size = new(10f, 10f);
    private int _priority;

    private CollisionShape3D _shape;
    private MeshInstance3D _tint;

    public static Color ColorFor(FormationContext context) => context switch
    {
        FormationContext.Open => new Color(0.45f, 0.95f, 0.45f),
        FormationContext.Urban => new Color(0.45f, 0.65f, 1.0f),
        FormationContext.Narrow => new Color(1.0f, 0.45f, 0.35f),
        _ => Colors.White,
    };

    public override void _Ready()
    {
        AddToGroup(Group);
        CollisionLayer = Layers.TerrainZones;
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = true;
        Rebuild();
    }

    /// <summary>
    /// The formation context at <paramref name="position"/>. Call it from physics code
    /// (_PhysicsProcess), since it queries the physics space.
    /// </summary>
    public static FormationContext QueryContext(Node3D context, Vector3 position,
        FormationContext fallback = FormationContext.Open)
    {
        TerrainZone zone = QueryZone(context, position);
        return zone?.Context ?? fallback;
    }

    public static TerrainZone QueryZone(Node3D context, Vector3 position)
    {
        PhysicsPointQueryParameters3D query = new()
        {
            Position = position + Vector3.Up * 0.5f,
            CollisionMask = Layers.TerrainZones,
            CollideWithAreas = true,
            CollideWithBodies = false,
        };
        var hits = context.GetWorld3D().DirectSpaceState.IntersectPoint(query, 16);

        TerrainZone best = null;
        foreach (var hit in hits)
        {
            if (hit["collider"].AsGodotObject() is TerrainZone zone && (best == null || zone.ZonePriority > best.ZonePriority))
            {
                best = zone;
            }
        }
        return best;
    }

    private void Rebuild()
    {
        if (!IsInsideTree())
        {
            return;
        }
        _shape?.QueueFree();
        _tint?.QueueFree();

        _shape = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(_size.X, 4f, _size.Y) },
            Position = new Vector3(0f, 1f, 0f),
        };
        AddChild(_shape, false, InternalMode.Front);

        // The colored ground overlay only exists in the editor, to see the zones while laying out.
        if (!Engine.IsEditorHint())
        {
            return;
        }
        Color color = ColorFor(_context);
        color.A = 0.2f;
        _tint = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = _size },
            // Lift higher-priority zones a hair so overlapping tints don't z-fight.
            Position = new Vector3(0f, 0.02f + _priority * 0.002f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = color,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        };
        AddChild(_tint, false, InternalMode.Front);
    }
}
