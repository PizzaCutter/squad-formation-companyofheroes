using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Destination formation: once the leader is close to the goal every soldier claims a spot
/// (cover if there is any nearby) and nobody else, from any squad, may claim it until it is given up.
/// </summary>
public partial class Squad
{
    private bool _inDestinationFormation;

    private bool TryEnterDestinationFormation()
    {
        if (Leader.GlobalPosition.Flat().DistanceTo(Destination.Value.Flat()) > DestinationFormationDistance)
        {
            return false;
        }
        StopLeapfrog();
        _inDestinationFormation = true;
        CurrentFormation = DestinationFormation ?? GetDesiredFormation();
        AssignDestinationSpots();
        return true;
    }

    private void ExitDestinationFormation()
    {
        _inDestinationFormation = false;
        ReleaseReservations();
    }

    private void AssignDestinationSpots()
    {
        ReleaseReservations();

        Vector3 destination = Destination.Value;
        Vector3 heading = DestinationFacing.Value;
        Dictionary<SquadElement, Vector3> elementSlots = new() { [SquadElement.Core] = destination };

        // Rank order, so the leader gets first pick of the cover, then element leaders, then followers
        foreach (Soldier soldier in Members.OrderBy(s => s.Role))
        {
            Vector3 offset = GetFormationOffset(soldier, heading, CurrentFormation);
            Vector3 slot = soldier.Role switch
            {
                SquadRole.SquadLeader   => destination,
                SquadRole.ElementLeader => destination + offset,
                _                       => elementSlots[soldier.Element] + offset,
            };
            elementSlots.TryAdd(soldier.Element, slot);

            CoverPoint cover = FindFreeCover(slot, heading);
            soldier.ReservedSpot = cover?.GlobalPosition ?? FindFreeSpot(destination, slot, offset.Normalized());
            if (cover != null)
            {
                cover.ReservedBy = soldier;
            }

            soldier.SpeedMultiplier = 1f;
            soldier.DesiredFacing = cover?.Facing ?? heading;
            soldier.MoveTo(soldier.ReservedSpot.Value);
        }
    }

    /// <summary>Closest unclaimed cover around the slot that protects against the direction we face.</summary>
    private CoverPoint FindFreeCover(Vector3 slot, Vector3 heading)
    {
        CoverPoint best = null;
        float bestDistance = float.MaxValue;
        foreach (CoverPoint cover in CoverPoint.FindInRadius(GetTree(), slot, CoverSearchRadius))
        {
            // Cover has to protect roughly the way we're facing
            bool facesThreat = cover.Facing.Dot(heading) > 0.5f;
            float distance = cover.GlobalPosition.DistanceTo(slot);
            if (facesThreat && distance < bestDistance && IsFree(cover.GlobalPosition))
            {
                best = cover;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>The slot if it's free, otherwise step further out along the formation. Shares the last one if all are taken.</summary>
    private Vector3 FindFreeSpot(Vector3 destination, Vector3 slot, Vector3 outward)
    {
        Vector3 Reachable(Vector3 p) => SoftenSlot(destination, p, 2f * destination.DistanceTo(p));

        Vector3 spot = Reachable(slot);
        for (int step = 1; step <= 3 && !IsFree(spot); step++)
        {
            spot = Reachable(slot + outward * step * ReservationRadius);
        }
        return spot;
    }

    // Checks every soldier in the world, so squads don't pick each other's spots
    private bool IsFree(Vector3 spot)
    {
        foreach (Soldier soldier in GetTree().GetNodesInGroup(Soldier.Group))
        {
            if (soldier.ReservedSpot is Vector3 reserved && reserved.Flat().DistanceTo(spot.Flat()) <= ReservationRadius)
            {
                return false;
            }
        }
        return true;
    }

    private void ReleaseReservations()
    {
        foreach (Soldier soldier in Members)
        {
            soldier.ReservedSpot = null;
        }
        foreach (CoverPoint cover in GetTree().GetNodesInGroup(CoverPoint.Group).Cast<CoverPoint>().Where(c => c.ReservedBy?.Squad == this))
        {
            cover.ReservedBy = null;
        }
    }

    /// <summary>Line from each soldier to its reserved spot, plus the squad's facing at the destination.</summary>
    private void DrawDestinationDebug()
    {
        if (!_inDestinationFormation)
        {
            return;
        }

        Vector3 destination = Destination.Value + Vector3.Up * 0.1f;
        DebugDraw.Arrow(destination, destination + DestinationFacing.Value * 2f, Colors.Cyan);

        foreach (Soldier soldier in Members.Where(s => s.ReservedSpot.HasValue))
        {
            DebugDraw.Circle(soldier.ReservedSpot.Value, ReservationRadius * 0.5f, Colors.Cyan);
            DebugDraw.Line(soldier.GlobalPosition, soldier.ReservedSpot.Value, Colors.Cyan);
        }
    }
}
