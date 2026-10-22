using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Bakes the level's navmesh at startup and again whenever something is destroyed.
/// Put all level geometry (BoxObstacles) under this node.
/// Only World-layer colliders are parsed as ground; obstacles carve themselves out.
/// </summary>
public partial class LevelNavigation : NavigationRegion3D
{
    /// <summary>Raised after every bake (startup and rebakes) once the new navmesh is live.</summary>
    public static event Action NavMeshChanged;

    /// <summary>Toggled with F3.</summary>
    public static bool ShowOverlay { get; private set; }

    [Export] public float AgentRadius { get; set; } = 0.4f;
    [Export] public float AgentHeight { get; set; } = 1.8f;
    [Export] public float AgentMaxClimb { get; set; } = 0.3f;

    // Must match navigation/3d/default_cell_size and default_cell_height in Project Settings,
    // and the agent sizes above should be whole multiples of them.
    private const float CellSize = 0.2f;
    private const float CellHeight = 0.1f;

    private static LevelNavigation _instance;
    private bool _rebakePending;
    private MeshInstance3D _overlay;

    public override void _Ready()
    {
        _instance = this;

        NavigationMesh ??= new NavigationMesh();
        NavigationMesh.GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders;
        NavigationMesh.GeometryCollisionMask = Layers.World;
        NavigationMesh.AgentRadius = AgentRadius;
        NavigationMesh.AgentHeight = AgentHeight;
        NavigationMesh.AgentMaxClimb = AgentMaxClimb;
        NavigationMesh.CellSize = CellSize;
        NavigationMesh.CellHeight = CellHeight;

        BakeFinished += OnBakeFinished;

        // Children are ready by now, so all obstacles exist. Bake synchronously so the
        // navmesh is there before the first physics frame.
        BakeNavigationMesh(false);
    }

    public override void _ExitTree()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    public static void RequestRebake()
    {
        if (_instance == null)
        {
            return;
        }
        if (_instance.IsBaking())
        {
            _instance._rebakePending = true;
            return;
        }
        _instance.BakeNavigationMesh(true);
    }

    public static void SetShowOverlay(bool show)
    {
        ShowOverlay = show;
        if (_instance?._overlay != null)
        {
            _instance._overlay.Visible = show;
        }
    }

    private void OnBakeFinished()
    {
        if (_rebakePending)
        {
            _rebakePending = false;
            BakeNavigationMesh(true);
            return;
        }
        RebuildOverlay();
        NavMeshChanged?.Invoke();
    }

    private void RebuildOverlay()
    {
        _overlay?.QueueFree();

        Vector3[] vertices = NavigationMesh.GetVertices();
        HashSet<(int, int)> edges = new();
        for (int p = 0; p < NavigationMesh.GetPolygonCount(); p++)
        {
            int[] polygon = NavigationMesh.GetPolygon(p);
            for (int i = 0; i < polygon.Length; i++)
            {
                int a = polygon[i];
                int b = polygon[(i + 1) % polygon.Length];
                edges.Add(a < b ? (a, b) : (b, a));
            }
        }

        LineBatch batch = new(drawOnTop: false) { Name = "NavOverlay" };
        AddChild(batch, false, InternalMode.Front);
        batch.Begin();
        Color color = new(0.2f, 0.9f, 1f, 0.55f);
        Vector3 lift = Vector3.Up * 0.06f;
        foreach (var (a, b) in edges)
        {
            batch.Add(ToGlobal(vertices[a]) + lift, ToGlobal(vertices[b]) + lift, color);
        }
        batch.End();
        batch.Visible = ShowOverlay;
        _overlay = batch;
    }
}
