using Godot;

/// <summary>
/// A static enemy. It exists so squads have something to face when the player gives a move
/// order without a facing. Faces its -Z.
/// </summary>
[Tool]
public partial class EnemyDummy : Node3D
{
    public const string Group = "enemies";

    public override void _Ready()
    {
        AddToGroup(Group);

        StandardMaterial3D red = new() { AlbedoColor = new Color(0.85f, 0.2f, 0.2f) };
        StandardMaterial3D dark = new() { AlbedoColor = new Color(0.15f, 0.12f, 0.1f) };

        AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.28f, Height = 1.5f },
            Position = new Vector3(0f, 0.75f, 0f),
            MaterialOverride = red,
        }, false, InternalMode.Front);
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.17f, Height = 0.34f },
            Position = new Vector3(0f, 1.65f, 0f),
            MaterialOverride = red,
        }, false, InternalMode.Front);
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.06f, 0.7f) },
            Position = new Vector3(0.22f, 1.1f, -0.3f),
            MaterialOverride = dark,
        }, false, InternalMode.Front);
    }

    /// <summary>Nearest enemy within <paramref name="maxRange"/>, or null.</summary>
    public static EnemyDummy FindNearest(SceneTree tree, Vector3 position, float maxRange = float.PositiveInfinity)
    {
        EnemyDummy best = null;
        float bestSq = maxRange * maxRange;
        foreach (Node node in tree.GetNodesInGroup(Group))
        {
            if (node is EnemyDummy enemy)
            {
                float sq = enemy.GlobalPosition.DistanceSquaredTo(position);
                if (sq < bestSq)
                {
                    best = enemy;
                    bestSq = sq;
                }
            }
        }
        return best;
    }
}
