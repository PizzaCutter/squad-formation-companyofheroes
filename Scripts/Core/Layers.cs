/// <summary>
/// Physics layer bits, matching the names in Project Settings > Layer Names > 3D Physics.
/// </summary>
public static class Layers
{
    /// <summary>Walkable ground. The only thing the navmesh bake parses as geometry.</summary>
    public const uint World = 1u << 0;

    public const uint Soldiers = 1u << 1;

    /// <summary>Area3D volumes that tell a squad which formation context it is in.</summary>
    public const uint TerrainZones = 1u << 2;

    /// <summary>Buildings, walls, cover. They carve the navmesh through a NavigationObstacle3D instead of being parsed.</summary>
    public const uint Obstacles = 1u << 3;
}
