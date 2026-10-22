/// <summary>The three elements a squad is split into.</summary>
public enum SquadElement
{
    Unassigned,
    Core,
    LeftFlank,
    RightFlank,
}

public enum SquadRole
{
    Unassigned,
    /// <summary>Leader of the core element, and therefore of the whole squad.</summary>
    SquadLeader,
    ElementLeader,
    Follower,
}
