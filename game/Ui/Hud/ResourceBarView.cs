using Godot;

namespace Dialoid.Game.Ui.Hud;

/// <summary>
/// A horizontal resource bar (HUD spec §4 items 2 and 17, §9): the fill in the resource's colour — fury amber-ochre, mana blue,
/// corruption violet with a dark core, health dark red — and «Ярость 45 / 100» with a dark outline over it. The orb of layout A
/// comes later; it will draw the same <see cref="ResourceBarData"/>. The width is the placer's (its minimum size).
/// </summary>
public partial class ResourceBarView : Control
{
    public const float BarHeight = 22;

    private readonly Label _label;

    /// <summary>The record drawn now, or null (hidden).</summary>
    public ResourceBarData? Data { get; private set; }

    public string Text => _label.Text;

    /// <summary>The fill colour drawn now.</summary>
    public Color FillColor => Data?.Fill ?? HudColors.Mana;

    public ResourceBarView(float width)
    {
        CustomMinimumSize = new Vector2(width, BarHeight);
        MouseFilter = MouseFilterEnum.Ignore;
        _label = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _label.AddThemeFontSizeOverride("font_size", 14);
        _label.AddThemeColorOverride("font_color", new Color(0.97f, 0.95f, 0.9f));
        _label.AddThemeColorOverride("font_outline_color", HudColors.NumberOutline);
        _label.AddThemeConstantOverride("outline_size", 5);
        _label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_label);
    }

    /// <summary>The text of the bar: «Мана 45 / 100» (whole numbers, rounded down so a short cost never reads as enough).</summary>
    public static string TextOf(string name, double value, double max) => $"{name} {HudFormat.Pair(value, max)}";

    public void Show(ResourceBarData? data)
    {
        Data = data;
        Visible = data != null;
        if (data == null) return;
        var text = data.Text;
        if (_label.Text != text) _label.Text = text;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, new Color(0.05f, 0.045f, 0.04f, 0.92f));
        if (Data is { } data)
        {
            var fraction = data.Max > 0 ? (float)Math.Clamp(data.Value / data.Max, 0, 1) : 0f;
            var fill = new Rect2(1, 1, (Size.X - 2) * fraction, Size.Y - 2);
            DrawRect(fill, data.Fill);
            if (data.ResourceId == "corruption" && fill.Size.X > 0)
            {
                // A dark core along the middle of the fill (§9: violet with a dark core).
                var core = new Rect2(fill.Position.X, fill.Position.Y + fill.Size.Y * 0.3f, fill.Size.X, fill.Size.Y * 0.4f);
                DrawRect(core, HudColors.CorruptionCore with { A = 0.85f });
            }
            // A light top edge on the fill so the level reads against the dark track.
            if (fill.Size.X > 2) DrawLine(fill.Position, fill.Position + new Vector2(fill.Size.X, 0), data.Fill.Lightened(0.3f), 1);
        }
        DrawRect(rect.Grow(-0.5f), SkillSlotView.EdgeColor, filled: false, width: 1);
    }
}
