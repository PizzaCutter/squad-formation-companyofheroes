using Godot;

/// <summary>The drag rectangle drawn while box selecting.</summary>
public partial class SelectionBox : Control
{
    public Rect2? Box { get; set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (Box is Rect2 box)
        {
            DrawRect(box, new Color(0.4f, 1f, 0.5f, 0.12f));
            DrawRect(box, new Color(0.4f, 1f, 0.5f, 0.9f), false, 1.5f);
        }
    }
}
