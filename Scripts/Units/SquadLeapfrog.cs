using Godot;

/// <summary>
/// Every so often a flank's element leader leaves its slot, runs to cover just ahead,
/// holds there for a moment and then rejoins
/// </summary>
public partial class Squad
{
    private Soldier _leapfrogger;
    private CoverPoint _leapfrogCover;
    private float _leapfrogHoldTimer;
    private float _leapfrogCooldown;
    private SquadElement _nextLeapfrogFlank = SquadElement.LeftFlank;

    // Debug only: where the last cover search looked
    private Vector3 _leapfrogSearchCenter;
    private Vector3 _leapfrogSearchHeading;

    private void UpdateLeapfrog(Vector3 anchor, Vector3 heading, Formation formation, float delta)
    {
        if (_leapfrogger == null)
        {
            _leapfrogCooldown -= delta;
            if (_leapfrogCooldown <= 0f)
            {
                TryStartLeapfrog(anchor, heading, formation);
            }
            return;
        }

        if (!_leapfrogCover.IsValid)
        {
            // Cover got blown up so we stop the leapfrog immediatelly
            StopLeapfrog();
            return;
        }

        if (_leapfrogger.HasArrived)
        {
            _leapfrogHoldTimer -= delta;

            // Don't keep holding when the squad has moved to far away from us
            Vector3 reference = Leader.GlobalPosition + GetFormationOffset(_leapfrogger, heading, formation);
            bool leftBehind = _leapfrogger.GlobalPosition.Flat().DistanceTo(reference.Flat()) > CatchupDistance;

            if (_leapfrogHoldTimer <= 0f || leftBehind)
            {
                StopLeapfrog();
            }
        }
    }

    private void TryStartLeapfrog(Vector3 anchor, Vector3 heading, Formation formation)
    {
        SquadElement flank = _nextLeapfrogFlank;
        Soldier elementLeader = GetElementLeader(flank);

        // Not worth it this close to the goal, the destination formation takes over soon
        bool nearGoal = Leader.GlobalPosition.Flat().DistanceTo(Destination.Value.Flat()) < LeapfrogMinDistanceToGoal;
        if (elementLeader == null || nearGoal)
        {
            return;
        }

        Vector3 slot = anchor + GetFormationOffset(elementLeader, heading, formation);
        _leapfrogSearchCenter = slot + heading * LeapfrogLookAhead;
        _leapfrogSearchHeading = heading;
        CoverPoint cover = FindFreeCover(_leapfrogSearchCenter, heading);
        if (cover == null)
        {
            _nextLeapfrogFlank = OtherFlank(flank);
            return;
        }

        _leapfrogger = elementLeader;
        _leapfrogCover = cover;
        _leapfrogHoldTimer = LeapfrogHoldTime;

        cover.ReservedBy = elementLeader;
        elementLeader.ReservedSpot = cover.GlobalPosition;
        elementLeader.SpeedMultiplier = 1f;
        elementLeader.DesiredFacing = cover.Facing;
        elementLeader.MoveTo(cover.GlobalPosition);
    }

    /// <summary>Ends the current leapfrog. The leader walks back to its slot on the next formation update.</summary>
    private void StopLeapfrog()
    {
        if (_leapfrogger == null)
        {
            return;
        }

        if (_leapfrogCover.IsValid)
        {
            _leapfrogCover.ReservedBy = null;
        }
        _leapfrogger.ReservedSpot = null;

        _nextLeapfrogFlank = OtherFlank(_leapfrogger.Element);
        _leapfrogger = null;
        _leapfrogCover = null;
        ResetLeapfrogCooldown();
    }

    private void ResetLeapfrogCooldown() =>
        _leapfrogCooldown = (float)GD.RandRange(LeapfrogCooldownMin, LeapfrogCooldownMax);

    private static SquadElement OtherFlank(SquadElement flank) =>
        flank == SquadElement.LeftFlank ? SquadElement.RightFlank : SquadElement.LeftFlank;

    /// <summary>
    /// Debug while searching for leapfrog: 
    /// grey circle is the search area, cover inside it is green if it faces the right way
    /// otherwise invalid leapfrog position so
    /// </summary>
    private void DrawLeapfrogDebug()
    {
        if (_leapfrogger == null)
        {
            if (_leapfrogCooldown > 0f || _inDestinationFormation)
            {
                DebugDraw.Text(Leader.GlobalPosition + Vector3.Up * 2f, $"leapfrog in {_leapfrogCooldown:F1}", Colors.Gray);
                return;
            }

            DebugDraw.Circle(_leapfrogSearchCenter, CoverSearchRadius, Colors.Gray);
            foreach (CoverPoint cover in CoverPoint.FindInRadius(GetTree(), _leapfrogSearchCenter, CoverSearchRadius))
            {
                bool facesThreat = cover.Facing.Dot(_leapfrogSearchHeading) > 0.5f;
                DebugDraw.Circle(cover.GlobalPosition, 0.3f, facesThreat ? Colors.Green : Colors.Red);
            }
            return;
        }

        Vector3 spot = _leapfrogger.ReservedSpot.Value;
        DebugDraw.Line(_leapfrogger.GlobalPosition, spot, Colors.Yellow);
        DebugDraw.Circle(spot, 0.4f, Colors.Yellow);
        DebugDraw.Text(spot + Vector3.Up * 1.5f, $"hold {_leapfrogHoldTimer:F1}", Colors.Yellow);
    }
}
