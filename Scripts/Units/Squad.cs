using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Squad plumbing: spawning, membership, deaths, reinforcements, selection and receiving
/// move orders. The formation behaviour itself lives in Squad.Formation.cs.
/// </summary>
public partial class Squad : Node3D
{
    public const string Group = "squads";

    [Export] public PackedScene SoldierScene { get; set; }
    [Export] public string DisplayName { get; set; } = "Squad";
    [Export(PropertyHint.Range, "1,12")] public int InitialSize { get; set; } = 6;
    [Export(PropertyHint.Range, "1,12")] public int MaxSize { get; set; } = 9;
    [Export(PropertyHint.Range, "0,10")] public float DesiredSoldierOffset { get; set; } = 2;
    [Export(PropertyHint.Range, "0,90")] public float FanAngle { get; set; } = 30f;
    [Export(PropertyHint.Range, "0,10")] public float LookAheadSeconds { get; set; } = 2f;

    public IReadOnlyList<Soldier> Members => _members;

    /// <summary>
    /// The squad leader. Set this in AssignRoles. Falls back to the first member so selection
    /// and the HUD work before roles exist.
    /// </summary>
    public Soldier Leader
    {
        get => IsInstanceValid(_leader) && _leader.IsAlive ? _leader : _members.FirstOrDefault();
        protected set => _leader = value;
    }

    public bool IsSelected { get; private set; }

    /// <summary>Last ordered destination, already snapped to the navmesh. Null before the first order.</summary>
    public Vector3? Destination { get; private set; }

    /// <summary>Facing the player dragged out with the right mouse button. Null for a plain click.</summary>
    public Vector3? DestinationFacing { get; private set; }

    private readonly List<Soldier> _members = new();
    private Soldier _leader;

    public override void _Ready()
    {
        AddToGroup(Group);

        // Spawn in loose rows of three behind the squad node, facing its -Z.
        for (int i = 0; i < InitialSize; i++)
        {
            int row = i / 3;
            int column = i % 3;
            Vector3 local = new((column - 1) * 1.6f, 0f, row * 1.6f);
            SpawnSoldier(ToGlobal(local), GlobalRotation.Y);
        }
        OnMembersChanged();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_members.Count == 0)
        {
            return;
        }

        UpdateFormation(delta);

        if (DebugDraw.Enabled)
        {
            DrawFormationDebug();
        }
    }

    // ---------------------------------------------------------------- orders

    /// <summary>Called by the player controller. <paramref name="destination"/> is on the navmesh.</summary>
    public void IssueMoveOrder(Vector3 destination, Vector3 facing)
    {
        Destination = destination;
        DestinationFacing = facing;
        if (_members.Count > 0)
        {
            OnMoveOrder(destination, facing);
        }
    }

    // ---------------------------------------------------------------- membership

    public bool Reinforce()
    {
        if (_members.Count >= MaxSize || SoldierScene == null)
        {
            return false;
        }

        // Arrive from behind the leader.
        Vector3 spawn = GlobalPosition;
        float yaw = GlobalRotation.Y;
        if (Leader is Soldier leader)
        {
            spawn = NavUtils.ClosestPoint(this, leader.GlobalPosition - leader.Forward * 3f);
            yaw = leader.GlobalRotation.Y;
        }
        SpawnSoldier(spawn, yaw);
        OnMembersChanged();
        return true;
    }

    public void KillRandomMember()
    {
        if (_members.Count > 0)
        {
            _members[GD.RandRange(0, _members.Count - 1)].Kill();
        }
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        foreach (Soldier soldier in _members)
        {
            soldier.SetSelected(selected);
        }
    }

    private Soldier SpawnSoldier(Vector3 position, float yaw)
    {
        Soldier soldier = SoldierScene.Instantiate<Soldier>();
        soldier.Squad = this;
        soldier.Name = $"Soldier{_members.Count}";
        AddChild(soldier);
        soldier.GlobalPosition = position;
        soldier.GlobalRotation = new Vector3(0f, yaw, 0f);
        soldier.SetSelected(IsSelected);
        soldier.Died += OnSoldierDied;
        _members.Add(soldier);
        return soldier;
    }

    private void OnSoldierDied(Soldier soldier)
    {
        _members.Remove(soldier);
        OnMembersChanged();
    }

    private void OnMembersChanged()
    {
        if (_members.Count > 0)
        {
            AssignRoles();
        }
    }
}
