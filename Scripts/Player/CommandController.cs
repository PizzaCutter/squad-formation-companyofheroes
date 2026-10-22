using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Player input: selection, move orders and the sandbox / recording hotkeys.
///
///   LMB click / drag      select squad / box select   (Shift adds)
///   RMB click             move
///   RMB drag              move and face the dragged direction
///   1-3, Ctrl+A, Esc      select squad N / all / none
///   K / R                 kill a soldier / reinforce   (selected squads)
///   X                     destroy the destructible under the cursor
///   Space , . /           pause, slower, faster, reset time scale
///   F1-F4, H              debug, labels, navmesh, cover, help
/// </summary>
public partial class CommandController : Node
{
    [Export] public RtsCamera Camera { get; set; }

    public IReadOnlyList<Squad> Selected => _selected;
    public bool ShowHelp { get; private set; } = true;

    private static readonly double[] TimeScales = { 0.1, 0.25, 0.5, 1.0, 2.0, 4.0 };
    private const int DefaultTimeScaleIndex = 3;

    private readonly List<Squad> _selected = new();
    private SelectionBox _selectionBox;
    private CanvasLayer _overlayLayer;

    private Vector2? _leftDownAt;
    private Vector2? _rightDownAt;
    private Vector3 _rightDownWorld;

    private int _timeScaleIndex = DefaultTimeScaleIndex;
    private bool _paused;

    private const float DragThresholdPixels = 6f;
    private const float FacingDragMinMeters = 1.0f;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        _overlayLayer = new CanvasLayer { Layer = 5 };
        AddChild(_overlayLayer);
        _selectionBox = new SelectionBox();
        _overlayLayer.AddChild(_selectionBox);

        // Start with the first squad selected so you can click right away.
        CallDeferred(MethodName.SelectIndex, 0, false);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } left:
                HandleLeftButton(left);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } right:
                HandleRightButton(right);
                break;
            case InputEventMouseMotion motion when _leftDownAt is Vector2 start:
                if (start.DistanceTo(motion.Position) > DragThresholdPixels)
                {
                    _selectionBox.Box = new Rect2(start, motion.Position - start).Abs();
                }
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key);
                break;
        }
    }

    public override void _Process(double delta)
    {
        // Facing preview while dragging a move order.
        if (_rightDownAt is Vector2 && GroundUnderMouse() is Vector3 current)
        {
            Vector3 start = _rightDownWorld + Vector3.Up * 0.1f;
            Vector3 end = current.Flat() + Vector3.Up * 0.1f;
            DebugDraw.Circle(start, 0.6f, new Color(0.4f, 1f, 0.5f));
            if (start.DistanceTo(end) > FacingDragMinMeters)
            {
                DebugDraw.Arrow(start, end, new Color(0.4f, 1f, 0.5f), 0.8f);
            }
        }
    }

    // ---------------------------------------------------------------- selection

    private void HandleLeftButton(InputEventMouseButton button)
    {
        if (button.Pressed)
        {
            _leftDownAt = button.Position;
            return;
        }
        if (_leftDownAt is not Vector2 start)
        {
            return;
        }
        _leftDownAt = null;
        _selectionBox.Box = null;

        bool additive = button.ShiftPressed;
        if (start.DistanceTo(button.Position) > DragThresholdPixels)
        {
            BoxSelect(new Rect2(start, button.Position - start).Abs(), additive);
        }
        else
        {
            ClickSelect(button.Position, additive);
        }
    }

    private void ClickSelect(Vector2 screen, bool additive)
    {
        Squad squad = null;
        if (Raycast(screen, Layers.Soldiers)?.collider is Soldier soldier)
        {
            squad = soldier.Squad;
        }

        if (!additive)
        {
            ClearSelection();
        }
        if (squad != null)
        {
            ToggleOrAdd(squad, additive);
        }
    }

    private void BoxSelect(Rect2 rect, bool additive)
    {
        if (!additive)
        {
            ClearSelection();
        }
        foreach (Squad squad in AllSquads())
        {
            bool inside = squad.Members.Any(m =>
            {
                Vector3 p = m.GlobalPosition + Vector3.Up * 0.9f;
                return !Camera.Camera.IsPositionBehind(p) && rect.HasPoint(Camera.Camera.UnprojectPosition(p));
            });
            if (inside && !_selected.Contains(squad))
            {
                Select(squad);
            }
        }
    }

    private void SelectIndex(int index, bool additive)
    {
        List<Squad> squads = AllSquads();
        if (!additive)
        {
            ClearSelection();
        }
        if (index >= 0 && index < squads.Count)
        {
            ToggleOrAdd(squads[index], additive);
        }
    }

    private void ToggleOrAdd(Squad squad, bool additive)
    {
        if (additive && _selected.Contains(squad))
        {
            _selected.Remove(squad);
            squad.SetSelected(false);
        }
        else if (!_selected.Contains(squad))
        {
            Select(squad);
        }
    }

    private void Select(Squad squad)
    {
        _selected.Add(squad);
        squad.SetSelected(true);
    }

    private void ClearSelection()
    {
        foreach (Squad squad in _selected.Where(IsInstanceValid))
        {
            squad.SetSelected(false);
        }
        _selected.Clear();
    }

    // ---------------------------------------------------------------- orders

    private void HandleRightButton(InputEventMouseButton button)
    {
        if (button.Pressed)
        {
            if (GroundUnderMouse() is Vector3 world)
            {
                _rightDownAt = button.Position;
                _rightDownWorld = world;
            }
            return;
        }
        if (_rightDownAt is null)
        {
            return;
        }
        _rightDownAt = null;

        Vector3 destination = _rightDownWorld;
        Vector3? facing = null;
        if (GroundUnderMouse() is Vector3 release && release.Flat().DistanceTo(destination.Flat()) > FacingDragMinMeters)
        {
            facing = (release - destination).Flat().Normalized();
        }
        IssueOrder(destination, facing);
    }

    private void IssueOrder(Vector3 destination, Vector3? facing)
    {
        if (_selected.Count == 0)
        {
            return;
        }

        Color color = new(0.4f, 1f, 0.5f);
        Vector3 marker = destination + Vector3.Up * 0.1f;
        DebugDraw.Circle(marker, 0.6f, color, 24, 0.8f);
        DebugDraw.Circle(marker, 0.3f, color, 16, 0.8f);
        if (facing is Vector3 f)
        {
            DebugDraw.Arrow(marker, marker + f * 2.5f, color, 0.8f, 0.8f);
        }

        foreach (Squad squad in _selected)
        {
            squad.IssueMoveOrder(destination, facing);
        }
    }

    /// <summary>
    /// The navmesh point under the cursor. Clicking a building or wall resolves to the nearest
    /// walkable ground next to it.
    /// </summary>
    private Vector3? GroundUnderMouse()
    {
        Vector2 screen = GetViewport().GetMousePosition();
        Vector3? point = Raycast(screen, Layers.World | Layers.Obstacles)?.position;
        point ??= Camera.MouseOnPlane(screen);
        if (point is not Vector3 p)
        {
            return null;
        }
        return NavUtils.ClosestPoint(Camera, new Vector3(p.X, 0f, p.Z));
    }

    // ---------------------------------------------------------------- hotkeys

    private void HandleKey(InputEventKey key)
    {
        switch (key.PhysicalKeycode)
        {
            case Key.Key1: SelectIndex(0, key.ShiftPressed); break;
            case Key.Key2: SelectIndex(1, key.ShiftPressed); break;
            case Key.Key3: SelectIndex(2, key.ShiftPressed); break;
            case Key.A when key.CtrlPressed:
                ClearSelection();
                AllSquads().ForEach(Select);
                break;
            case Key.Escape: ClearSelection(); break;

            case Key.K:
                foreach (Squad squad in _selected) squad.KillRandomMember();
                break;
            case Key.R:
                foreach (Squad squad in _selected) squad.Reinforce();
                break;
            case Key.X:
                if (Raycast(GetViewport().GetMousePosition(), Layers.Obstacles)?.collider is BoxObstacle obstacle)
                {
                    obstacle.Destroy();
                }
                break;

            case Key.Space:
                _paused = !_paused;
                ApplyTimeScale();
                break;
            case Key.Comma:
                _timeScaleIndex = Mathf.Max(0, _timeScaleIndex - 1);
                ApplyTimeScale();
                break;
            case Key.Period:
                _timeScaleIndex = Mathf.Min(TimeScales.Length - 1, _timeScaleIndex + 1);
                ApplyTimeScale();
                break;
            case Key.Slash:
                _timeScaleIndex = DefaultTimeScaleIndex;
                ApplyTimeScale();
                break;

            case Key.F1: DebugDraw.Enabled = !DebugDraw.Enabled; break;
            case Key.F2: Soldier.SetShowLabels(GetTree(), !Soldier.ShowLabels); break;
            case Key.F3: LevelNavigation.SetShowOverlay(!LevelNavigation.ShowOverlay); break;
            case Key.F4: CoverPoint.ShowMarkers = !CoverPoint.ShowMarkers; break;
            case Key.H: ShowHelp = !ShowHelp; break;
        }
    }

    public bool Paused => _paused;

    /// <summary>The selected playback speed. Still reports the speed to resume at while paused.</summary>
    public double TimeScale => TimeScales[_timeScaleIndex];

    private void ApplyTimeScale() => Engine.TimeScale = _paused ? 0.0 : TimeScales[_timeScaleIndex];

    // ---------------------------------------------------------------- helpers

    private List<Squad> AllSquads() => GetTree().GetNodesInGroup(Squad.Group).OfType<Squad>().ToList();

    private (Vector3 position, GodotObject collider)? Raycast(Vector2 screen, uint mask)
    {
        Camera3D camera = Camera.Camera;
        Vector3 from = camera.ProjectRayOrigin(screen);
        Vector3 to = from + camera.ProjectRayNormal(screen) * 500f;
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, to, mask);
        var hit = camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0)
        {
            return null;
        }
        return (hit["position"].AsVector3(), hit["collider"].AsGodotObject());
    }
}
