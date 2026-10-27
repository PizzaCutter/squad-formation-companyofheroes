using System.Linq;
using Godot;
using Godot.Collections;

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
    // DEBUG VARIABLES FOR BETTER VISUALIZATION
    Vector3 PredictedAnchorLocation = Vector3.Zero;
    Vector3 PredictedHeading = Vector3.Zero;
    Array<Vector3> PredictedLocations = new Array<Vector3>();
    Array<Vector3> ReferenceLocations = new Array<Vector3>();
    const float AvoidanceDebugArrowScale = 0.5f;    // metres of arrow per m/s of velocity
    // ~DEBUG VARIABLES FOR BETTER VISUALIZATION

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
    private void OnMoveOrder(Vector3 destination, Vector3 facing)
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

        int slot = 2;
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

    private (Vector3 outPosition, Vector3 outHeading) GetPredictedLocationAhead(Soldier inSoldier, float distanceAhead)
    {
        Vector3 outPosition = inSoldier.GlobalPosition;
        Vector3 outHeading = inSoldier.DesiredFacing.Value;

        Vector3[] path = inSoldier.CurrentPath;
        if (inSoldier.HasArrived || path.IsEmpty())
        {
            return (outPosition, outHeading);
        }

        for (int i = inSoldier.CurrentPathIndex; i < path.Length; i++)
        {
            Vector3 segment = (path[i] - outPosition).Flat();
            float length = segment.Length();
            if (Mathf.IsZeroApprox(length))
            {
                continue;
            }

            outHeading = segment.Flat().Normalized();
            if (distanceAhead <= length)
            {
                return (outPosition + outHeading * distanceAhead, outHeading);
            }

            distanceAhead -= length;
            outPosition = path[i];
        }

        return (outPosition, inSoldier.DesiredFacing.Value);
    }

    private Vector3 SoftenSlot(Vector3 anchor, Vector3 target, float radius)
    {
        NavigationPathQueryParameters3D queryParams = new();
        queryParams.Map = NavUtils.Map(this);
        queryParams.StartPosition = anchor;
        queryParams.TargetPosition = target;
        // limits the A* search
        queryParams.PathSearchMaxDistance = radius;
        NavigationPathQueryResult3D queryResult = new();

        NavigationServer3D.QueryPath(queryParams, queryResult);

        // If no point is available at all then we stack on the leader
        // this shouldn't really happen though
        if (queryResult.Path.IsEmpty())
        {
            return anchor;
        }
        return queryResult.Path.Last();
    }

    private Formation GetDesiredFormation()
    {
        TerrainZone targetZone = TerrainZone.QueryZone(Leader, Leader.GlobalPosition);
        if (targetZone != null)
        {
            if (targetZone.FormationOverride != null)
            {
                return targetZone.FormationOverride;
            }
        }
        return DefaultFormation;
    }

    /// <summary>Called every physics tick while the squad has members.</summary>
    private void UpdateFormation(double delta)
    {
        if (!Destination.HasValue && !DestinationFacing.HasValue)
        {
            return;
        }

        // Here we need to look ahead of where leader will be instead of just taking current position
        var (anchor, heading) = GetPredictedLocationAhead(Leader, Leader.MoveSpeed * LookAheadSeconds);
        Vector3 back = -heading;              // +Z points behind the leader

        Formation desiredFormation = GetDesiredFormation();
        CurrentFormation = desiredFormation;

        float fan = Mathf.DegToRad(desiredFormation.FanAngle);

        PredictedAnchorLocation = anchor;
        PredictedHeading = heading;

        PredictedLocations.Clear();
        ReferenceLocations.Clear();

        foreach (Soldier soldier in Members)
        {
            Soldier localLeader = GetLocalLeader(soldier);
            if (localLeader == null)
            {
                continue;
            }

            Vector3 offsetVector = soldier.Element switch
            {
                SquadElement.LeftFlank  => back.Rotated(Vector3.Up, -fan),
                SquadElement.RightFlank => back.Rotated(Vector3.Up,  fan),
                _=> back,              // Core: straight behind
            };

            // Adding "randomized" drift so the soldiers look less robotic
            Vector3 drift = PredictedHeading * soldier.Drift;
            Vector3 slotOffset = offsetVector * desiredFormation.DesiredOffset * GetSlot(soldier) + drift;

            Vector3 soldierPosition = anchor + slotOffset; 
            Vector3 referencePosition = Leader.GlobalPosition + offsetVector * desiredFormation.DesiredOffset * GetSlot(soldier);

            // soldierPosition might be on the other side of a wall which might causes characters to path around a long way
            // so we do pathfind from leader final position with small area then pick that location
            float searchRadius = 2.0f * (float)GetSlot(soldier);
            soldierPosition = SoftenSlot(Leader.Target, soldierPosition, searchRadius);

            PredictedLocations.Add(soldierPosition);
            ReferenceLocations.Add(referencePosition);

            if (soldier.GlobalPosition.DistanceTo(soldierPosition) > 1.0f)
            {
                soldier.MoveTo(soldierPosition);
                soldier.DesiredFacing = DestinationFacing;
            }

            Vector3 soldierToReference = (referencePosition - soldier.GlobalPosition).Flat();
            float along = soldierToReference.Dot(PredictedHeading);
            float t = Mathf.Clamp(Mathf.Abs(along) / CatchupDistance, 0f, 1f);
            float speedMultiplierTarget = along > 0 
                ? Mathf.Lerp(1f, MaxMovementSpeedMultiplier, t)
                : Mathf.Lerp(1f, MinMovementSpeedMultiplier, t);
            
            // smoothing out the movement speed up/down
            soldier.SpeedMultiplier = (float)Mathf.MoveToward(soldier.SpeedMultiplier, speedMultiplierTarget, MovementSpeedMultiplierInterpolationSpeed * delta);
        }
    }

    /// <summary>Called every physics tick while formation debug drawing is on (F1).</summary>
    private void DrawFormationDebug()
    {
        if (Destination is Vector3 destination && Leader is Soldier leader && !leader.HasArrived)
        {
            DebugDraw.Path(leader.CurrentPath, Colors.Green);
            DebugDraw.Circle(destination, 0.5f, Colors.Green);
        }
        
        // Avoidance: white = wanted velocity, magenta = what avoidance gave back.
        // Ring is the avoidance radius, red while arrived (high priority), grey while moving.
        foreach (Soldier soldier in Members)
        {
            Vector3 origin = soldier.GlobalPosition + Vector3.Up * 0.1f;
            DebugDraw.Circle(origin, soldier.Agent.Radius, soldier.HasArrived ? Colors.Red : Colors.Gray);

            if (soldier.DesiredVelocity.LengthSquared() > 0.0001f)
            {
                DebugDraw.Arrow(origin, origin + soldier.DesiredVelocity * AvoidanceDebugArrowScale, Colors.White, 0.2f);
            }
            if (soldier.Agent.AvoidanceEnabled && soldier.AvoidanceVelocity.LengthSquared() > 0.0001f)
            {
                DebugDraw.Arrow(origin, origin + soldier.AvoidanceVelocity * AvoidanceDebugArrowScale, Colors.Magenta, 0.2f);
            }
        }

        int index = 0;
        foreach(Soldier soldier in Members)
        {
            Soldier localLeader = GetLocalLeader(soldier);
            if (localLeader == null)
            {
                continue;
            }

            DebugDraw.Arrow(soldier.GlobalPosition, localLeader.GlobalPosition, Colors.Blue.Lightened(0.2f));

            Color SpeedMultiplierTextColor = soldier.SpeedMultiplier > 1f ? Colors.Green : Colors.Red;
            DebugDraw.Text(soldier.GlobalPosition + new Vector3(0f, 1f, 0f), $"{soldier.SpeedMultiplier:F2}", SpeedMultiplierTextColor);

            if (PredictedLocations.Count > index)
            {
                DebugDraw.Circle(PredictedLocations[index], 0.5f, Colors.Orange);
                DebugDraw.Circle(ReferenceLocations[index], 0.5f, Colors.Black.Lightened(0.5f));
            }

            index++;
        }
    }
}
