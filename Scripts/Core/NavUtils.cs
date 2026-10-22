using System.Collections.Generic;
using Godot;

/// <summary>
/// Thin wrappers around NavigationServer3D so formation code reads cleanly.
/// All queries run against the default navigation map of the given node's world.
///
/// Note: Recast puts the navmesh about 0.2 m (two height cells) above the ground, so points
/// returned here have y ~ 0.2 while soldiers stand at y = 0. Compare positions with .Flat()
/// when that matters.
/// </summary>
public static class NavUtils
{
    public static Rid Map(Node3D context) => context.GetWorld3D().NavigationMap;

    /// <summary>Closest point on the navmesh to <paramref name="point"/>.</summary>
    public static Vector3 ClosestPoint(Node3D context, Vector3 point)
    {
        return NavigationServer3D.MapGetClosestPoint(Map(context), point);
    }

    /// <summary>
    /// Path from <paramref name="from"/> to <paramref name="to"/>. If the target is unreachable the
    /// path ends at the closest reachable point instead.
    /// </summary>
    public static Vector3[] GetPath(Node3D context, Vector3 from, Vector3 to, bool optimize = true)
    {
        return NavigationServer3D.MapGetPath(Map(context), from, to, optimize);
    }

    public static float PathLength(IReadOnlyList<Vector3> path)
    {
        float length = 0f;
        for (int i = 1; i < path.Count; i++)
        {
            length += path[i - 1].DistanceTo(path[i]);
        }
        return length;
    }

    /// <summary>Same vector with Y zeroed. Handy for headings and ground-plane distances.</summary>
    public static Vector3 Flat(this Vector3 v) => new Vector3(v.X, 0f, v.Z);
}
