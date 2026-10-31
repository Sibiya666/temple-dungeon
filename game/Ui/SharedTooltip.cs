using Godot;

namespace Dialoid.Game.Ui;

/// <summary>
/// The room's one tooltip (spec §4 «Подсказки»), the same panel for the mouse and the gamepad. Godot's own tooltips are off
/// (gui/timers/tooltip_delay_sec). On the mouse the text of the control under it shows after <see cref="HoverDelaySec"/>,
/// below-right of the cursor, and a press hides it. On the gamepad the focused control's text shows outside its window, but only
/// where it keeps <see cref="HeroClearance"/> px on both sides of the hero free; otherwise it goes inside the window, at the spot
/// (under or over the focused control, at the bottom or the top) that covers the fewest controls and never the focused one or the
/// neighbour the stick goes to next. Both read the same source (<see cref="TipOf"/>).
/// </summary>
public partial class SharedTooltip : PanelContainer
{
    public const double HoverDelaySec = 0.5;
    public const float MaxWidth = 360;

    /// <summary>Pixels kept free on each side of the hero (the fight stays readable).</summary>
    public const float HeroClearance = 250;

    private readonly Label _label;
    private Control? _hovered;
    private Vector2 _hoverAt;
    private double _hoverFor;

    public SharedTooltip()
    {
        Name = "SharedTooltip";
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        var style = new StyleBoxFlat { BgColor = new Color(0.07f, 0.065f, 0.06f, 0.97f), BorderColor = WindowHost.Border, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 5, ContentMarginBottom = 6 };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(3);
        AddThemeStyleboxOverride("panel", style);
        _label = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        _label.AddThemeColorOverride("font_color", new Color(0.94f, 0.91f, 0.85f));
        AddChild(_label);
    }

    /// <summary>The text shown now, or null.</summary>
    public string? Shown => Visible ? _label.Text : null;

    /// <summary>The control under the mouse whose text is shown now, or null.</summary>
    public Control? HoverControl => Visible ? _hovered : null;

    /// <summary>The text a control shows on hover and on focus (a tree node, a bar row, a number field by its box).</summary>
    public static string TipOf(Control control, Vector2? at = null) => control switch
    {
        IPadControl pad when at == null => pad.PadTooltip(),
        LineEdit { } edit when edit.GetParent() is SpinBox spin => spin.GetTooltip(spin.Size / 2),
        _ => control.GetTooltip(at ?? control.Size / 2),
    };

    /// <summary>Shows <paramref name="text"/> on top of its parent; returns the size (zero and hidden for an empty text).</summary>
    public Vector2 Show(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            Visible = false;
            return Vector2.Zero;
        }
        if (_label.Text != text)
        {
            _label.Text = text;
            var font = _label.GetThemeFont("font");
            var fontSize = _label.GetThemeFontSize("font_size");
            var widest = text.Split('\n').Max(l => font.GetStringSize(l, HorizontalAlignment.Left, -1, fontSize).X);
            _label.CustomMinimumSize = new Vector2(Mathf.Min(widest + 2, MaxWidth), 0);
            Size = Vector2.Zero;
        }
        if (!Visible)
        {
            Visible = true;
            GetParent()?.MoveChild(this, GetParent().GetChildCount() - 1);
        }
        var size = GetCombinedMinimumSize();
        Size = size;
        return size;
    }

    /// <summary>Puts the panel at a screen point, kept inside the screen and below <paramref name="top"/>.</summary>
    public void PlaceAt(Vector2 pos, Vector2 size, Vector2 screen, float top)
    {
        pos.X = Mathf.Clamp(pos.X, 0, Mathf.Max(0, screen.X - size.X));
        pos.Y = Mathf.Clamp(pos.Y, top, Mathf.Max(top, screen.Y - size.Y));
        Position = pos - (GetParent() as Control)?.GlobalPosition ?? Vector2.Zero;
    }

    /// <summary>
    /// The gamepad: the focused control's text beside its window when that keeps the hero's sides free, else inside the window in
    /// the spot covering the fewest controls (neighbours weigh ten times); under the top bar for the bar.
    /// </summary>
    public void ShowForFocus(Control root, Control? focus, bool topBar, float? heroX, Rect2? inside, Vector2 screen, float top, IReadOnlyList<Rect2>? avoid = null)
    {
        var size = Show(focus == null ? "" : TipOf(focus));
        if (size == Vector2.Zero) return;
        var area = root.GetGlobalRect();
        var at = focus!.GetGlobalRect();
        if (topBar)
        {
            PlaceAt(new Vector2(at.Position.X, area.End.Y + 34), size, screen, top);
            return;
        }
        bool Clear(float x) => heroX is not { } h || x + size.X < h - HeroClearance || x > h + HeroClearance;
        var right = area.End.X + 8;
        var left = area.Position.X - 8 - size.X;
        if (right + size.X <= screen.X && Clear(right)) PlaceAt(new Vector2(right, at.Position.Y), size, screen, top);
        else if (left >= 0 && Clear(left)) PlaceAt(new Vector2(left, at.Position.Y), size, screen, top);
        else
        {
            // Inside the window: under the focused control, over it, at the bottom or at the top — whichever covers the fewest
            // controls, the neighbours the stick goes to next (within a row or two) counting ten times.
            var box = inside ?? area;
            var tip = new Vector2(Mathf.Min(size.X, box.Size.X - 8), size.Y);
            var spots = new[] { at.End.Y + 4, at.Position.Y - tip.Y - 4, box.End.Y - tip.Y - 4, box.Position.Y + 4 }
                .Where(y => y >= box.Position.Y && y + tip.Y <= box.End.Y + 0.5f)
                .Select(y => new Rect2(box.Position.X + 4, y, tip))
                .Where(r => !r.Intersects(at))
                .ToList();
            float Cost(Rect2 r) => (avoid ?? []).Where(a => a.Intersects(r))
                .Sum(a => Mathf.Abs(a.GetCenter().Y - at.GetCenter().Y) < 80 ? 10f : 1f);
            var best = spots.Count > 0 ? spots.MinBy(Cost) : new Rect2(box.Position.X + 4, box.Position.Y + 4, tip);
            PlaceAt(best.Position, tip, screen, top);
        }
    }

    /// <summary>The mouse, every frame in keyboard-and-mouse mode.</summary>
    public void UpdateHover(double delta, Vector2 screen, float top)
    {
        var viewport = GetViewport();
        var hovered = viewport.GuiGetHoveredControl();
        var mouse = viewport.GetMousePosition();
        if (hovered != _hovered || mouse.DistanceTo(_hoverAt) > 3 && !Visible)
        {
            _hovered = hovered;
            _hoverAt = mouse;
            _hoverFor = 0;
            if (Visible) Visible = false;
        }
        if (_hovered == null || !IsInstanceValid(_hovered) || !_hovered.IsVisibleInTree()) return;
        _hoverFor += delta;
        if (_hoverFor < HoverDelaySec) return;
        var size = Show(TipOf(_hovered, _hovered.GetLocalMousePosition()));
        if (size != Vector2.Zero) PlaceAt(mouse + new Vector2(14, 20), size, screen, top);
    }

    /// <summary>A mouse press: hidden until the mouse rests again.</summary>
    public void ResetHover()
    {
        _hoverFor = 0;
        _hoverAt = GetViewport().GetMousePosition();
        Visible = false;
    }
}
