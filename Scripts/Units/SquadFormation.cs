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

    private Soldier GetLocalLeader(Soldier soldier) => soldier.Role switch
    {
        SquadRole.SquadLeader   => null,
        SquadRole.ElementLeader => Leader,
        SquadRole.Follower      => GetElementLeader(soldier.Element) ?? Leader,
        _                           => Leader,
    };

    private Soldier GetElementLeader(SquadElement element)
    {
        foreach(Soldier soldier in Members)
        {
            if (soldier.Element == element && (soldier.Role == SquadRole.SquadLeader || soldier.Role == SquadRole.ElementLeader))
            {
                return soldier;
            }
        }

        return null;
    }

    /// <summary>
    /// Called once when the player gives a move order.
    /// <paramref name="facing"/> is set when the player right-click-dragged a direction.
    /// </summary>
    private void OnMoveOrder(Vector3 destination, Vector3? facing)
    {
        Destination = destination;
        DestinationFacing = facing;

        Leader.DesiredFacing = DestinationFacing;
        Leader.MoveTo(Destination.Value);
    }

    private int GetSlot(Soldier soldier)
    {
        if (soldier.Role != SquadRole.Follower)
        {
            return 1;
        }

        int slot = 1;
        foreach (Soldier other in Members)
        {
            if (other == soldier)
            {
                break;
            }

            if (other.Element == soldier.Element && other.Role == SquadRole.Follower)
            {
                slot++;
            }
        }
        return slot;
    }

    /// <summary>Called every physics tick while the squad has members.</summary>
    private void UpdateFormation(double delta)
    {
        if (!Destination.HasValue && !DestinationFacing.HasValue)
        {
            return;
        }

        foreach (Soldier soldier in Members)
        {
            Soldier localLeader = GetLocalLeader(soldier);
            if (localLeader == null)
            {
                continue;
            }

            Vector3 soldierPosition = localLeader.GlobalPosition;

            Vector3 back = Leader.GlobalBasis.Z;              // +Z points behind the leader
            float fan = Mathf.DegToRad(FanAngle);

            Vector3 offsetVector = soldier.Element switch
            {
                SquadElement.LeftFlank  => back.Rotated(Vector3.Up, -fan),
                SquadElement.RightFlank => back.Rotated(Vector3.Up,  fan),
                _=> back,              // Core: straight behind
            };

            soldierPosition += offsetVector * DesiredSoldierOffset * GetSlot(soldier); 

            soldier.MoveTo(soldierPosition);
            soldier.DesiredFacing = DestinationFacing ?? Leader.Forward;
        }
    }

    /// <summary>Called every physics tick while formation debug drawing is on (F1).</summary>
    private void DrawFormationDebug()
    {
        if (Destination is Vector3 destination && Leader is Soldier leader && !leader.HasArrived)
        {
            DebugDraw.Path(leader.CurrentPath, new Color(1f, 1f, 0.3f, 0.8f));
            DebugDraw.Circle(destination, 0.5f, Colors.Yellow);
        }
        
        foreach(Soldier soldier in Members)
        {
            Soldier localLeader = GetLocalLeader(soldier);
            if (localLeader == null)
            {
                continue;
            }

            DebugDraw.Arrow(soldier.GlobalPosition, localLeader.GlobalPosition, new Color(1f, 1f, 0.3f, 0.8f));
        }
    }
}
