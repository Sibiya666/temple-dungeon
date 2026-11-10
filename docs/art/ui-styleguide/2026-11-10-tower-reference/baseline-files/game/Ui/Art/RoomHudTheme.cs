using Godot;

namespace Dialoid.Game.Ui.Art;

/// <summary>Quiet charcoal and iron surfaces from ART_DIRECTION. No layout, input or combat rules.</summary>
public static class RoomHudTheme
{
    public static readonly Color Parchment = new("d8d5c6");
    public static readonly Color Bronze = new("334147");
    public static readonly Color LitBronze = new("71858c");
    public static readonly Color Focus = new("b7cbd0");
    public static readonly Color Stone = new("0c1114");
    private const string Root = "res://Ui/Art/";

    /// <summary>Each call returns a separate mutable style; no component shares focus state.</summary>
    public static StyleBoxTexture Panel(float margin = 8) => Frame("stone-frame.svg", margin);
    public static StyleBoxTexture Border() => Frame("bronze-border.svg", 0);

    private static StyleBoxTexture Frame(string file, float margin) => new()
    {
        Texture = GD.Load<Texture2D>(Root + file),
        TextureMarginLeft = 16, TextureMarginTop = 16,
        TextureMarginRight = 16, TextureMarginBottom = 16,
        ContentMarginLeft = margin, ContentMarginTop = margin,
        ContentMarginRight = margin, ContentMarginBottom = margin,
    };

    public static Theme Create(Theme? source = null)
    {
        // Keep the host's focus styles, font metrics and field geometry. Only change the visual surface.
        var theme = (Theme)(source ?? ThemeDB.GetDefaultTheme()).Duplicate();
        foreach (var type in new[] { "Label", "Button", "CheckButton", "CheckBox", "OptionButton", "LineEdit", "TextEdit" })
        {
            theme.SetColor("font_color", type, Parchment);
            theme.SetColor("font_hover_color", type, Focus);
            theme.SetColor("font_pressed_color", type, Colors.White);
            theme.SetColor("font_disabled_color", type, new Color("8b8579"));
        }
        foreach (var type in new[] { "Button", "OptionButton", "MenuButton" })
        {
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
            {
                var factor = state switch
                {
                    "hover" => 1.25f,
                    "pressed" or "hover_pressed" => 0.75f,
                    "disabled" => 0.6f,
                    _ => 1f,
                };
                // Small +/- and dropdown buttons must not receive the large ornamental corners.
                var face = new StyleBoxFlat { BgColor = (Stone * factor) with { A = 1 }, BorderColor = (Bronze * factor) with { A = 1 } };
                face.SetBorderWidthAll(1);
                var previous = theme.GetStylebox(state, type);
                foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
                    face.SetContentMargin(side, previous.GetContentMargin(side));
                theme.SetStylebox(state, type, face);
            }
            // Keep the source's focus ring, including its expansion margins and gamepad visibility.
        }
        // Decorative 9-slice borders are overlays: their fixed corners must never become container padding.
        theme.SetStylebox("panel", "TooltipPanel", Panel(12));
        foreach (var state in new[] { "normal", "read_only" })
        {
            var field = new StyleBoxFlat { BgColor = new Color("10100f"), BorderColor = Bronze };
            var oldField = theme.GetStylebox(state, "LineEdit");
            foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
                field.SetContentMargin(side, oldField.GetContentMargin(side));
            field.SetBorderWidthAll(1);
            theme.SetStylebox(state, "LineEdit", field);
        }
        return theme;
    }
}
