using System.Collections.Generic;
using System.Linq;
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
    // DEBUG VARIABLES FOR BETTER VISUALIZATION
    Dictionary<Soldier, Vector3> PredictedLocations = new();
    Dictionary<Soldier, Vector3> ReferenceLocations = new();
    const float AvoidanceDebugArrowScale = 0.5f;    // metres of arrow per m/s of velocity
    // ~DEBUG VARIABLES FOR BETTER VISUALIZATION

    /// <summary>
    /// Called on spawn, on every death and on every reinforcement.
    /// Split the squad into core / left flank / right flank and pick a leader for each element.
    /// Set <see cref="Leader"/> to the squad leader.
    /// </summary>
    private void AssignRoles()
    {
        // Soldiers keep their element; only newcomers and an empty core need one
        foreach (Soldier soldier in Members.Where(s => s.Element == SquadElement.Unassigned))
        {
            soldier.Element = CountIn(SquadElement.Core) == 0 ? SquadElement.Core : SmallestFlank();
        }

        if (CountIn(SquadElement.Core) == 0)
        {
            // Promote the bigger flank's leader; its first follower steps up to lead the flank
            Members.First(s => s.Element == LargestFlank()).Element = SquadElement.Core;
        }

        // The longest-serving soldier of each element leads it
        HashSet<SquadElement> led = [];
        foreach (Soldier soldier in Members)
        {
            bool first = led.Add(soldier.Element);
            soldier.Role = !first ? SquadRole.Follower
                : soldier.Element == SquadElement.Core ? SquadRole.SquadLeader
                : SquadRole.ElementLeader;
        }

        // _leader rather than Leader: the getter already falls back to Members[0] once the old leader is dead
        Soldier previousLeader = _leader;
        Leader = Members.First(s => s.Role == SquadRole.SquadLeader);

        if (Leader != previousLeader)
        {
            // The new leader was walking to a follower slot, so hand it the squad's order
            Leader.SpeedMultiplier = 1f;
            if (Destination.HasValue)
            {
                Leader.DesiredFacing = DestinationFacing;
                Leader.MoveTo(Destination.Value);
            }
        }

        // The squad changed shape: the leapfrogger may be dead or promoted, and the destination spots need handing out again
        StopLeapfrog();
        if (_inDestinationFormation)
        {
            AssignDestinationSpots();
        }
    }

    private int CountIn(SquadElement element) => Members.Count(s => s.Element == element);

    private SquadElement SmallestFlank() =>
        CountIn(SquadElement.LeftFlank) <= CountIn(SquadElement.RightFlank) ? SquadElement.LeftFlank : SquadElement.RightFlank;

    private SquadElement LargestFlank() =>
        SmallestFlank() == SquadElement.LeftFlank ? SquadElement.RightFlank : SquadElement.LeftFlank;

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
        ExitDestinationFormation();
        StopLeapfrog();
        ResetLeapfrogCooldown();

        Leader.DesiredFacing = DestinationFacing;
        Leader.MoveTo(Destination.Value);
    }

    /// <summary>
    /// Squad leader returns 0
    /// Element leaders returns 1
    /// Followers then continue 2, 3, 4...
    /// </summary>
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

    private (Vector3 outPosition, Vector3 outHeading) GetPredictedLocationAhead(Soldier inSoldier, float distanceAhead)
    {
        Vector3 outPosition = inSoldier.GlobalPosition;
        Vector3 fallbackHeading = inSoldier.DesiredFacing ?? inSoldier.Forward;
        Vector3 outHeading = fallbackHeading;

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

        return (outPosition, fallbackHeading);
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

        if (_inDestinationFormation || TryEnterDestinationFormation())
        {
            return;
        }

        // Here we need to look ahead of where leader will be instead of just taking current position
        var (anchor, heading) = GetPredictedLocationAhead(Leader, Leader.MoveSpeed * LookAheadSeconds);

        // Softening searches outward from here, so it has to sit on the navmesh
        Vector3 navAnchor = NavUtils.ClosestPoint(this, anchor);

        Formation desiredFormation = GetDesiredFormation();
        CurrentFormation = desiredFormation;

        PredictedLocations.Clear();
        ReferenceLocations.Clear();

        UpdateLeapfrog(anchor, heading, desiredFormation, (float)delta);

        // Where each element's followers line up from
        Dictionary<SquadElement, (Vector3 anchor, Vector3 navAnchor)> elementAnchors = new()
        {
            [SquadElement.Core] = (anchor, navAnchor),
        };

        // element leaders take their slot off the squad leader's predicted position
        foreach (Soldier soldier in Members)
        {
            if (soldier.Role != SquadRole.ElementLeader)
            {
                continue;
            }
            if (soldier == _leapfrogger)
            {
                // Off to cover; its followers line up behind the cover spot instead of the slot
                Vector3 spot = soldier.ReservedSpot.Value;
                elementAnchors[soldier.Element] = (spot, spot);
                continue;
            }
            Vector3 goal = UpdateSoldierSlot(soldier, Leader, anchor, navAnchor, heading, desiredFormation, delta);
            elementAnchors[soldier.Element] = (goal, goal);
        }

        // followers take their slot of their element leader's goal
        foreach (Soldier soldier in Members)
        {
            if (soldier.Role == SquadRole.SquadLeader || soldier.Role == SquadRole.ElementLeader)
            {
                continue;
            }
            Soldier localLeader = GetLocalLeader(soldier);

            // No element leader (shouldn't happen with the current role assignment): fall back to the squad leader
            var (elementAnchor, elementNavAnchor) = elementAnchors.TryGetValue(soldier.Element, out var found)
                ? found
                : (anchor, navAnchor);
            UpdateSoldierSlot(soldier, localLeader, elementAnchor, elementNavAnchor, heading, desiredFormation, delta);
        }
    }

    /// <summary>Wedge offset of a soldier from its local leader, oriented along <paramref name="heading"/>.</summary>
    private Vector3 GetFormationOffset(Soldier soldier, Vector3 heading, Formation formation)
    {
        Vector3 back = -heading;              // +Z points behind the leader
        float fan = Mathf.DegToRad(formation.FanAngle);
        Vector3 offsetVector = soldier.Element switch
        {
            SquadElement.LeftFlank  => back.Rotated(Vector3.Up, -fan),
            SquadElement.RightFlank => back.Rotated(Vector3.Up,  fan),
            _ => back,              // Core: straight behind
        };
        return offsetVector * formation.DesiredOffset * GetSlot(soldier);
    }

    /// <summary>
    /// Moves soldier to its slot behind its local leader's predicted position or goal and returns the softened goal.
    /// </summary>
    private Vector3 UpdateSoldierSlot(Soldier soldier, Soldier localLeader, Vector3 anchor, Vector3 navAnchor,
        Vector3 heading, Formation formation, double delta)
    {
        int slot = GetSlot(soldier);
        Vector3 formationOffset = GetFormationOffset(soldier, heading, formation);

        // Adding "randomized" drift so the soldiers look less robotic
        Vector3 drift = heading * soldier.Drift;

        // The slot might be on the other side of a wall which might cause characters to path around a long way
        // so we pathfind from the local leader's anchor within a small area and pick that location
        float searchRadius = 2.0f * slot;
        Vector3 goal = SoftenSlot(navAnchor, anchor + formationOffset + drift, searchRadius);
        Vector3 referencePosition = localLeader.GlobalPosition + formationOffset;

        PredictedLocations[soldier] = goal;
        ReferenceLocations[soldier] = referencePosition;

        if (soldier.GlobalPosition.DistanceTo(goal) > 1.0f)
        {
            soldier.MoveTo(goal);
            soldier.DesiredFacing = DestinationFacing;
        }

        Vector3 soldierToReference = (referencePosition - soldier.GlobalPosition).Flat();
        float along = soldierToReference.Dot(heading);
        float t = Mathf.Clamp(Mathf.Abs(along) / CatchupDistance, 0f, 1f);
        float speedMultiplierTarget = along > 0
            ? Mathf.Lerp(1f, MaxMovementSpeedMultiplier, t)
            : Mathf.Lerp(1f, MinMovementSpeedMultiplier, t);

        // smoothing out the movement speed up/down
        soldier.SpeedMultiplier = (float)Mathf.MoveToward(soldier.SpeedMultiplier, speedMultiplierTarget, MovementSpeedMultiplierInterpolationSpeed * delta);

        return goal;
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

            if (PredictedLocations.TryGetValue(soldier, out Vector3 predicted))
            {
                DebugDraw.Circle(predicted, 0.5f, Colors.Orange);
            }
            if (ReferenceLocations.TryGetValue(soldier, out Vector3 reference))
            {
                DebugDraw.Circle(reference, 0.5f, Colors.Black.Lightened(0.5f));
            }
        }

        DrawDestinationDebug();
        DrawLeapfrogDebug();
    }
}
