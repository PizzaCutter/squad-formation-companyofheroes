using Godot;

/// <summary>
/// A MeshInstance3D that redraws a set of colored line segments. Fill it between
/// <see cref="Begin"/> and <see cref="End"/> once per frame.
/// </summary>
public partial class LineBatch : MeshInstance3D
{
    private readonly ImmediateMesh _mesh = new();
    private readonly StandardMaterial3D _material = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    };
    private bool _open;
    private int _vertexCount;

    public LineBatch(bool drawOnTop = true)
    {
        _material.NoDepthTest = drawOnTop;
        _material.RenderPriority = drawOnTop ? 10 : 0;
        Mesh = _mesh;
        CastShadow = ShadowCastingSetting.Off;
    }

    public LineBatch() : this(true) { }

    public void Begin()
    {
        _mesh.ClearSurfaces();
        _vertexCount = 0;
        _open = false;
    }

    public void Add(Vector3 a, Vector3 b, Color color)
    {
        if (!_open)
        {
            _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _material);
            _open = true;
        }
        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(a);
        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(b);
        _vertexCount += 2;
    }

    public void End()
    {
        if (_open && _vertexCount > 0)
        {
            _mesh.SurfaceEnd();
        }
        _open = false;
    }
}
