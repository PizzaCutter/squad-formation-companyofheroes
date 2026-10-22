using Godot;

/// <summary>
/// ===== YOUR FILE =====
/// The formation logic. Squad.cs calls the four methods below; everything else is yours.
///
/// What's available to work with:
///   Members, Leader, Destination, DestinationFacing         (Squad.cs)
///   soldier.MoveTo / Stop / SpeedMultiplier / DesiredFacing  (Soldier.cs)
///   soldier.CurrentPath / CurrentPathIndex / Forward / Element / Role
///   NavUtils.GetPath / ClosestPoint / PathLength / Flat      (Core/NavUtils.cs)
///   TerrainZone.QueryContext(this, position)                 (formation context)
///   CoverPoint.FindInRadius(GetTree(), position, radius)     (leapfrog + destination cover)
///   EnemyDummy.FindNearest(GetTree(), position, range)       (auto-facing at destination)
///   DebugDraw.Line / Arrow / Circle / Sphere / Path / Text   (visualisation)
///   LevelNavigation.NavMeshChanged                           (fires after a rebake)
///
/// What's here now is a placeholder: everyone runs straight for the click point.
/// </summary>
public partial class Squad
{
    /// <summary>
    /// Called on spawn, on every death and on every reinforcement.
    /// Split the squad into core / left flank / right flank and pick a leader for each element.
    /// Set <see cref="Leader"/> to the squad leader.
    /// </summary>
    private void AssignRoles()
    {
        // No special logic just assigning by first come first served
        SquadRole Role = SquadRole.SquadLeader;
        SquadElement Elements = SquadElement.Core;

        for (int i = 0; i < Members.Count; i++)
        {
            Members[i].Role = Role;
            Members[i].Element = Elements;

            Elements++;
            if ((int)Elements > (int)SquadElement.RightFlank)
            {
                Elements = SquadElement.LeftFlank;
            }

            if (Role == SquadRole.ElementLeader && Elements == SquadElement.RightFlank)
            {
                continue;
            }

            if (Role < SquadRole.Follower)
            {
                Role++;
            }
        }
        Leader = Members[0];
    }

    /// <summary>
    /// Called once when the player gives a move order.
    /// <paramref name="facing"/> is set when the player right-click-dragged a direction.
    /// </summary>
    private void OnMoveOrder(Vector3 destination, Vector3? facing)
    {
        // Placeholder: the naive approach. Everyone paths to the same point.
        foreach (Soldier soldier in Members)
        {
            soldier.DesiredFacing = facing;
            soldier.MoveTo(destination);
        }
    }

    /// <summary>Called every physics tick while the squad has members.</summary>
    private void UpdateFormation(double delta)
    {
    }

    /// <summary>Called every physics tick while formation debug drawing is on (F1).</summary>
    private void DrawFormationDebug()
    {
        if (Destination is Vector3 destination && Leader is Soldier leader && !leader.HasArrived)
        {
            DebugDraw.Path(leader.CurrentPath, new Color(1f, 1f, 0.3f, 0.8f));
            DebugDraw.Circle(destination, 0.5f, Colors.Yellow);
        }
    }
}
