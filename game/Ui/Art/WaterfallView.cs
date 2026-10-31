using Godot;

namespace Dialoid.Game.Ui.Art;

/// <summary>A quiet architectural spout, exact vertical gauge and independent moving-water shader.</summary>
public partial class WaterfallView : Control
{
    public const float Width = 108;
    public const float Height = 176;
    public static readonly Rect2 Channel = new(39, 36, 30, 70);
    private readonly ColorRect _water;
    private readonly ShaderMaterial _material;
    private readonly Label _name;
    private readonly Label _value;
    private readonly Label _percent;
    public WaterfallReading? Reading { get; private set; }
    public float ShaderFraction => _material.GetShaderParameter("level").AsSingle();
    public bool WarningShown => Reading is { Alert: true };

    public WaterfallView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        Size = CustomMinimumSize = new Vector2(Width, Height);
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Ui/Art/waterfall.gdshader") };
        _water = new ColorRect { Position = Channel.Position, Size = Channel.Size, MouseFilter = MouseFilterEnum.Ignore, Material = _material };
        AddChild(_water);
        _name = Caption(112, 22, 16);
        _value = Caption(134, 42, 16);
        _percent = Caption(88, 18, 14);
        _percent.Position = new Vector2(0, 86);
        _percent.Size = new Vector2(36, 18);
        _percent.HorizontalAlignment = HorizontalAlignment.Right;
        _percent.AddThemeColorOverride("font_color", new Color("d2dde0"));
    }

    private Label Caption(float y, float height, int fontSize)
    {
        var label = new Label { Position = new Vector2(0, y), Size = new Vector2(Width, height),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore, ClipText = true };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color("d0d7d5"));
        label.AddThemeColorOverride("font_outline_color", new Color("080e12"));
        label.AddThemeConstantOverride("outline_size", 4);
        AddChild(label);
        return label;
    }

    public void Show(WaterfallReading reading)
    {
        if (reading == Reading) return;
        Reading = reading;
        _name.Text = reading.Alert ? $"! {reading.Name}" : reading.Name;
        _name.AddThemeColorOverride("font_color", reading.Alert ? new Color("d4b6ae") : new Color("d0d7d5"));
        var pair = $"{reading.CurrentText} / {reading.MaxText}";
        var width = _value.GetThemeFont("font").GetStringSize(pair, fontSize: 16).X;
        _value.Text = width < Width - 4 ? pair : $"{reading.CurrentText}\n/ {reading.MaxText}";
        _percent.Text = reading.PercentText;
        _material.SetShaderParameter("level", reading.Fraction);
        _material.SetShaderParameter("water_color", reading.Color);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var dark = new Color("101416");
        var face = new Color("1d2326");
        var light = new Color("5c707a");
        // A tall open arch, deep recess and a few broad lit planes; no texture noise or repeated grain.
        DrawRect(new Rect2(18, 18, 76, 94), new Color(0.015f, 0.025f, 0.035f, .72f));
        DrawColoredPolygon([new(24, 26), new(54, 4), new(84, 26), new(78, 108), new(30, 108)], dark);
        DrawColoredPolygon([new(30, 27), new(54, 10), new(54, 26), new(38, 39), new(38, 108), new(30, 108)], face);
        DrawColoredPolygon([new(54, 10), new(78, 27), new(78, 108), new(70, 108), new(70, 39), new(54, 26)], new Color("151c20"));
        DrawRect(new Rect2(40, 35, 28, 72), new Color("060e14"));
        DrawArc(new Vector2(54, 40), 16, Mathf.Pi, Mathf.Tau, 24, light with { A = .65f }, 1, true);
        DrawLine(new Vector2(38, 40), new Vector2(38, 108), light with { A = .32f }, 1, true);
        DrawLine(new Vector2(70, 40), new Vector2(70, 108), light with { A = .22f }, 1, true);
        DrawLine(new Vector2(29, 27), new Vector2(54, 9), light with { A = .5f }, 1, true);
        // The lip projects in front of the recess; the stream falls freely into a shallow receiving bowl.
        DrawColoredPolygon([new(35, 31), new(73, 31), new(70, 38), new(38, 38)], new Color("30393d"));
        DrawLine(new Vector2(36, 32), new Vector2(72, 32), light with { A = .5f }, 1);
        DrawColoredPolygon([new(24, 106), new(84, 106), new(76, 113), new(32, 113)], new Color("202a30"));
        DrawPolyline([new(24, 106), new(33, 111), new(75, 111), new(84, 106)], light with { A = .6f }, 1, true);
        DrawLine(new Vector2(21, 112), new Vector2(87, 112), light with { A = .45f }, 1);
        for (int i = 1; i < 5; i++)
        {
            var y = Channel.Position.Y + Channel.Size.Y * i / 5;
            DrawLine(new Vector2(34, y), new Vector2(38, y), light with { A = .7f }, 1);
        }
        DrawRect(new Rect2(0, 113, Width, 63), new Color(.02f, .035f, .045f, .64f));
    }
}
