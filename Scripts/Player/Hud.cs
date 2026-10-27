using System.Globalization;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// Controls help (top left, H toggles) and live status (top right). The help shows the current
/// playback speed and highlights the view toggles that are on.
/// Key names are shown as they appear on the player's own keyboard layout.
/// </summary>
public partial class Hud : CanvasLayer
{
    [Export] public CommandController Controller { get; set; }

    private const string ActiveColor = "#8dff9c";
    private const string InactiveColor = "#ffffff80";
    private const string FastSlowColor = "#ffd75e";
    private const string PausedColor = "#ff9a4d";

    private RichTextLabel _help;
    private Label _status;
    private string _helpText;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        _help = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(16, 12),
            Size = new Vector2(900, 0),
        };
        _help.AddThemeFontSizeOverride("normal_font_size", 14);
        _help.AddThemeColorOverride("default_color", new Color(1, 1, 1, 0.92f));
        _help.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        _help.AddThemeConstantOverride("outline_size", 5);
        AddChild(_help);

        _status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _status.AddThemeFontSizeOverride("font_size", 14);
        _status.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.92f));
        _status.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        _status.AddThemeConstantOverride("outline_size", 5);
        AddChild(_status);
        _status.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _status.GrowHorizontal = Control.GrowDirection.Begin;
        _status.OffsetLeft = -16;
        _status.OffsetRight = -16;
        _status.OffsetTop = 12;
    }

    public override void _Process(double delta)
    {
        _help.Visible = Controller.ShowHelp;
        if (_help.Visible)
        {
            string text = BuildHelp();
            if (text != _helpText)
            {
                _helpText = text;
                _help.Text = text;
            }
        }

        StringBuilder sb = new();
        sb.AppendLine($"{Engine.GetFramesPerSecond()} fps");
        foreach (Squad squad in Controller.Selected.Where(IsInstanceValid))
        {
            string context = "";
            if (squad.Leader is Soldier leader)
            {
                context = TerrainZone.QueryContext(leader, leader.GlobalPosition).ToString();
            }
            sb.AppendLine($"{squad.DisplayName}: {squad.Members.Count}/{squad.MaxSize}  {context}  {squad.CurrentFormation.Name}");
        }
        _status.Text = sb.ToString();
    }

    private string BuildHelp()
    {
        string pan = K(Key.W) + K(Key.A) + K(Key.S) + K(Key.D);
        return string.Join("\n",
            "CAMERA",
            $"  {pan} / arrows pan    {K(Key.Q)} {K(Key.E)} / MMB drag rotate    wheel zoom",
            "ORDERS",
            $"  LMB select (drag box, Shift adds)    1 2 3 squad    Ctrl+{K(Key.A)} all    Esc none",
            "  RMB move    RMB drag move + facing",
            "SANDBOX",
            $"  {K(Key.K)} kill soldier    {K(Key.R)} reinforce    {K(Key.X)} destroy object under cursor",
            $"  Space pause    {K(Key.Comma)} slower    {K(Key.Period)} faster    {K(Key.Slash)} normal speed    {Speed()}",
            "VIEW",
            "  " + string.Join("    ",
                Toggle("F1 formation debug", DebugDraw.Enabled),
                Toggle("F2 role labels", Soldier.ShowLabels),
                Toggle("F3 navmesh", LevelNavigation.ShowOverlay),
                Toggle("F4 cover points", CoverPoint.ShowMarkers)) +
            $"    {K(Key.H)} help");
    }

    private string Speed()
    {
        string speed = "x" + Controller.TimeScale.ToString("0.##", CultureInfo.InvariantCulture);
        if (Controller.Paused)
        {
            return $"[color={PausedColor}]PAUSED ({speed})[/color]";
        }
        return Mathf.IsEqualApprox(Controller.TimeScale, 1.0)
            ? $"speed {speed}"
            : $"[color={FastSlowColor}]speed {speed}[/color]";
    }

    private static string Toggle(string text, bool on) =>
        $"[color={(on ? ActiveColor : InactiveColor)}]{text}[/color]";

    private static string K(Key physical)
    {
        Key label = DisplayServer.KeyboardGetLabelFromPhysical(physical);
        if (label == Key.None)
        {
            label = physical;
        }
        // Punctuation keys use their character code, so show the symbol rather than "COMMA".
        long code = (long)label;
        if (code is > 32 and < 127 && !char.IsLetterOrDigit((char)code))
        {
            return ((char)code).ToString();
        }
        return OS.GetKeycodeString(label).ToUpper();
    }
}
