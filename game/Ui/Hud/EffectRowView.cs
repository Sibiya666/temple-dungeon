using Dialoid.Core.Simulation;
using Dialoid.Core.Explain;
using Godot;
using Dialoid.Game.Ui.Art;

namespace Dialoid.Game.Ui.Hud;

/// <summary>The frame of an effect chip: the shape tells the rows apart, not only the colour (HUD spec §3 anti-goals).</summary>
public enum ChipFrame
{
    /// <summary>The hero's buffs and stacks: a straight matte-bronze frame.</summary>
    Bronze,
    /// <summary>The hero's ailments and debuffs: a torn (jagged) dark-red frame.</summary>
    Torn,
    /// <summary>A target's ailments and marks: a thin frame with a thick accent edge in the damage type's colour.</summary>
    Accent,
}

/// <summary>
/// A row of effect icons (HUD spec §4 items 6–8) in one of three kinds: the hero's buffs (bronze frame), the hero's ailments and
/// debuffs (torn dark-red frame), a target's ailments and marks (on its plate). Past <see cref="MaxVisible"/> icons the row keeps
/// the first <c>MaxVisible − 1</c> and folds the rest into one «+N» icon; its tooltip (the room's shared tooltip on hover, the
/// row's own on focus) lists every effect of the row. The row draws a list of <see cref="EffectChipData"/> and nothing else.
/// </summary>
public partial class EffectRowView : HFlowContainer
{
    private readonly List<EffectChip> _chips = new();
    private readonly EffectChip _more;
    private IReadOnlyList<EffectChipData> _effects = Array.Empty<EffectChipData>();

    public EffectRowKind Kind { get; }

    /// <summary>Icons shown at most, the «+N» included.</summary>
    public int MaxVisible { get; }

    /// <summary>Every effect of the row (shown or folded).</summary>
    public IReadOnlyList<EffectChipData> Effects => _effects;

    /// <summary>The «+N» icon (hidden while everything fits).</summary>
    public EffectChip Overflow => _more;

    /// <summary>Effects folded into «+N» (0 when everything fits).</summary>
    public int Folded => _effects.Count > MaxVisible ? _effects.Count - (MaxVisible - 1) : 0;

    /// <summary>Texts of the icons shown now, in order («+N» last).</summary>
    public IEnumerable<string> ShownTexts => _chips.Where(c => c.Visible).Select(c => c.Text).Concat(_more.Visible ? new[] { _more.Text } : Array.Empty<string>());

    /// <summary>The effects behind the icons shown now (without «+N»).</summary>
    public IEnumerable<EffectChipData> ShownEffects => _chips.Where(c => c.Visible).Select(c => c.Data!);

    public ChipFrame Frame => FrameOf(Kind);

    public static ChipFrame FrameOf(EffectRowKind kind) => kind switch
    {
        EffectRowKind.Buffs => ChipFrame.Bronze,
        EffectRowKind.Debuffs => ChipFrame.Torn,
        _ => ChipFrame.Accent,
    };

    public EffectRowView(EffectRowKind kind, int maxVisible)
    {
        Kind = kind;
        MaxVisible = Math.Max(2, maxVisible);
        MouseFilter = MouseFilterEnum.Ignore;
        Alignment = AlignmentMode.Center;
        AddThemeConstantOverride("h_separation", kind == EffectRowKind.Target ? 3 : 4);
        AddThemeConstantOverride("v_separation", kind == EffectRowKind.Target ? 3 : 4);
        _more = new EffectChip(FrameOf(kind)) { Name = "More", Visible = false };
        _more.MakeOverflow();
        AddChild(_more);
    }

    /// <summary>Header of the full list: «Баффы (15)», «Недуги и дебаффы (3)», «Эффекты цели (6)».</summary>
    public static string Header(EffectRowKind kind, int count) => kind switch
    {
        EffectRowKind.Buffs => $"Баффы ({count})",
        EffectRowKind.Debuffs => $"Недуги и дебаффы ({count})",
        _ => $"Эффекты цели ({count})",
    };

    /// <summary>The full list of the row: the header, then one effect per line.</summary>
    public static string FullList(EffectRowKind kind, IReadOnlyList<EffectChipData> effects) =>
        string.Join("\n", effects.Select(e => e.Text).Prepend(Header(kind, effects.Count) + ":"));

    public void Show(IReadOnlyList<EffectChipData> effects)
    {
        _effects = effects;
        var fold = effects.Count > MaxVisible;
        var shown = fold ? MaxVisible - 1 : effects.Count;
        while (_chips.Count < shown)
        {
            var chip = new EffectChip(Frame);
            _chips.Add(chip);
            AddChild(chip);
            MoveChild(_more, GetChildCount() - 1);
        }
        for (int i = 0; i < _chips.Count; i++)
        {
            if (i < shown) _chips[i].Show(effects[i]);
            else if (_chips[i].Visible) _chips[i].Visible = false;
        }
        if (_more.Visible != fold) _more.Visible = fold;
        if (fold) _more.ShowOverflow(effects.Count - shown, ListFor(effects));
        else if (_more.HasFocus()) _more.ReleaseFocus();
    }

    private string[] _listTexts = Array.Empty<string>();
    private string _list = "";

    /// <summary>The full list, rebuilt only when a text changed (the rows refresh every frame; code review, task 14 round 1).</summary>
    private string ListFor(IReadOnlyList<EffectChipData> effects)
    {
        var same = _listTexts.Length == effects.Count;
        for (int i = 0; same && i < effects.Count; i++) same = _listTexts[i] == effects[i].Text;
        if (same) return _list;
        _listTexts = effects.Select(e => e.Text).ToArray();
        return _list = FullList(Kind, effects);
    }

    /// <summary>Puts the focus on «+N» (its full list shows; the game's gamepad layout reaches the row this way). False when
    /// nothing is folded.</summary>
    public bool FocusOverflow()
    {
        if (!_more.Visible) return false;
        _more.FocusMode = FocusModeEnum.All;
        _more.GrabFocus();
        return _more.HasFocus();
    }
}

/// <summary>
/// One icon of an effect row: a buff, stack, ailment or debuff with its stacks and timer as a small chip; or the row's «+N».
/// The frame is the row's (<see cref="ChipFrame"/>); the accent edge is the effect's kind or damage type.
/// </summary>
public partial class EffectChip : PanelContainer
{
    private readonly Label _label;
    private readonly TextureRect _icon;
    private readonly StyleBoxFlat _style;
    private Color _accent;

    public ChipFrame Frame { get; }

    /// <summary>The record drawn now (null for «+N»).</summary>
    public EffectChipData? Data { get; private set; }

    public TimedEffect? Effect => Data?.Effect;
    public string Text => _label.Text;

    /// <summary>The row's «+N» icon: it takes the mouse and can take the focus, its tooltip is the row's full list.</summary>
    public bool IsOverflow { get; private set; }

    public EffectChip(ChipFrame frame)
    {
        Frame = frame;
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        var torn = frame == ChipFrame.Torn;
        _style = new StyleBoxFlat
        {
            BgColor = torn ? new Color(0.1f, 0.04f, 0.035f, 0.93f) : new Color(0.07f, 0.065f, 0.06f, 0.92f),
            ContentMarginLeft = torn ? 9 : 7, ContentMarginRight = torn ? 8 : 6, ContentMarginTop = torn ? 4 : 1, ContentMarginBottom = torn ? 5 : 2,
        };
        switch (frame)
        {
            case ChipFrame.Bronze:
                _style.SetBorderWidthAll(2); // a visible bronze frame all round, no kind colour (UI review, task 14 round 1)
                _style.BorderWidthLeft = 4;
                _style.BorderColor = HudColors.Bronze;
                _style.SetCornerRadiusAll(2);
                break;
            case ChipFrame.Accent:
                _style.SetBorderWidthAll(1);
                _style.BorderWidthLeft = 4;
                _style.SetCornerRadiusAll(2);
                break;
            case ChipFrame.Torn: // the frame is drawn in _Draw (jagged edges)
                break;
        }
        AddThemeStyleboxOverride("panel", _style);
        _label = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _label.AddThemeFontSizeOverride("font_size", 14);
        _label.AddThemeColorOverride("font_color", new Color(0.94f, 0.91f, 0.85f));
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _icon = new TextureRect
        {
            Name = "EffectIcon", CustomMinimumSize = new Vector2(20, 20), Visible = false,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        row.AddChild(_icon); row.AddChild(_label); AddChild(row);
        FocusExited += () =>
        {
            if (IsOverflow) FocusMode = FocusModeEnum.None;
            QueueRedraw();
        };
        FocusEntered += QueueRedraw;
    }

    /// <summary>«Боевой клич 6,2 с», «Поджог ×3 · 2,4 с», «Статический заряд: 4».</summary>
    public static string TextOf(TimedEffect e, string name)
    {
        if (name.Length > 0) name = char.ToUpperInvariant(name[0]) + name[1..]; // ailment names are lowercase («поджог»)
        if (e.Kind == TimedEffectKind.MechanicStack) return $"{name}: {RuNumber.Format(e.Stacks, "0")}";
        var text = e.Stacks > 1 ? $"{name} ×{RuNumber.Format(e.Stacks, "0")}" : name;
        // A timer about to end (under 0,05 s) is not shown: no «Заморозка 0,0 с».
        if (e.TimeLeft is not { } t || double.IsInfinity(t) || t < 0.05) return text;
        // Stacks and timer are two numbers: a dot keeps «×2» and «3,1 с» apart.
        return e.Stacks > 1 ? $"{text} · {HudFormat.Seconds(t)} с" : $"{text} {HudFormat.Seconds(t)} с";
    }

    /// <summary>Buffs green, stacks gold, ailments by their damage colour (room spec §4 «Читаемость боя»), debuffs grey.</summary>
    public static Color ColorOf(TimedEffect e) => e.Kind switch
    {
        TimedEffectKind.Buff => new Color(0.42f, 0.78f, 0.38f),
        TimedEffectKind.MechanicStack => new Color(0.9f, 0.72f, 0.3f),
        TimedEffectKind.Debuff => new Color(0.72f, 0.72f, 0.72f),
        _ => e.Id switch
        {
            "ignite" => new Color(1f, 0.52f, 0.15f),
            "poison" => new Color(0.42f, 0.85f, 0.28f),
            "bleed" => new Color(0.85f, 0.2f, 0.2f),
            "shock" => new Color(0.62f, 0.72f, 1f),
            "chill" => new Color(0.55f, 0.85f, 1f),
            "freeze" => new Color(0.75f, 0.95f, 1f),
            _ => new Color(0.8f, 0.6f, 0.5f),
        },
    };

    public void Show(EffectChipData data)
    {
        if (!Visible) Visible = true;
        Data = data;
        _icon.Texture = data.Effect?.Kind == TimedEffectKind.Ailment ? GameIcons.Ailment(data.Id)
            : data.Effect != null ? GameIcons.Buff(data.Id) : null;
        _icon.Visible = _icon.Texture != null;
        if (_label.Text != data.Text) _label.Text = data.Text;
        SetAccent(data.Accent);
    }

    internal void MakeOverflow()
    {
        IsOverflow = true;
        MouseFilter = MouseFilterEnum.Stop; // the full list on hover
        SetAccent(Frame == ChipFrame.Torn ? HudColors.DebuffFrame : HudColors.Bronze);
    }

    internal void ShowOverflow(int folded, string fullList)
    {
        var text = $"+{folded}";
        if (_label.Text != text) _label.Text = text;
        if (TooltipText != fullList) TooltipText = fullList;
    }

    private void SetAccent(Color color)
    {
        if (_accent == color) return;
        _accent = color;
        switch (Frame)
        {
            case ChipFrame.Accent:
                _style.BorderColor = color;
                break;
            case ChipFrame.Bronze:
                // The bronze frame stays; the kind is not coloured (a green edge read like poison's).
                _style.BorderColor = HudColors.Bronze;
                break;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        if (Frame == ChipFrame.Torn)
        {
            DrawPolyline(TornOutline(size), HudColors.DebuffFrame, TornWidth, antialiased: true);
            DrawRect(new Rect2(3, 4, 3, size.Y - 8), IsOverflow ? HudColors.DebuffFrame : _accent);
        }
        if (HasFocus()) DrawRect(new Rect2(Vector2.Zero, size).Grow(-1), WindowHost.FocusFrame, filled: false, width: 2);
    }

    /// <summary>Torn frame: teeth every <see cref="TornStep"/> px, <see cref="TornDepth"/> px deep, a <see cref="TornWidth"/> px line.</summary>
    public const float TornStep = 6, TornDepth = 3, TornWidth = 2;

    /// <summary>The torn frame: top and bottom edges jagged, the sides with one notch each.</summary>
    public static Vector2[] TornOutline(Vector2 size)
    {
        const float tooth = TornStep, depth = TornDepth, inset = 1.5f;
        var points = new List<Vector2>();
        float l = inset, r = size.X - inset, t = inset, b = size.Y - inset;
        int teeth = Math.Max(2, (int)((r - l) / tooth));
        for (int i = 0; i <= teeth; i++) points.Add(new Vector2(l + (r - l) * i / teeth, t + (i % 2 == 1 ? depth : 0)));
        var mid = (t + b) / 2;
        points.Add(new Vector2(r, mid - 2));
        points.Add(new Vector2(r - depth, mid));
        points.Add(new Vector2(r, mid + 2));
        for (int i = teeth; i >= 0; i--) points.Add(new Vector2(l + (r - l) * i / teeth, b - (i % 2 == 0 ? depth : 0)));
        points.Add(new Vector2(l, mid + 2));
        points.Add(new Vector2(l + depth, mid));
        points.Add(new Vector2(l, mid - 2));
        points.Add(points[0]);
        return points.ToArray();
    }
}
