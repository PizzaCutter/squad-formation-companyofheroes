using Godot;

[Tool]
[GlobalClass]
public partial class Formation : Resource
{
    [Export]
    public string Name { get; set; }
    [Export]
    public float FanAngle { get; set; } = 15f;

    [Export]
    public float DesiredOffset { get; set; } = 2f;

    public Formation() : this(15f, 2f) { }

    public Formation(float inFanAngle, float inDesiredOffset)
    {
        FanAngle = inFanAngle;
        DesiredOffset = inDesiredOffset;
    }
}