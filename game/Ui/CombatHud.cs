using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Explain;
using Dialoid.Core.Simulation;
using Dialoid.Game.Room;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>
/// The combat screen (spec §4 «Экран боя»): without it the owner cannot see why a skill did not fire. Bottom centre: the hero's
/// buffs and mechanic stacks with timers, the resource bar («Мана 45 / 100»), the slot bar 1–5 (skill name, key, cooldown
/// shade with seconds, red frame and shade when the resource is short, a highlight for the queued press and the held channel, a
/// blink on a refusal; the tooltip gives cost, cooldown and use time from <see cref="SlotInfo"/>). Over the dummy: a plate with
/// the preset name and the target's ailments and debuffs with stacks and timers. Everything is read from
/// <see cref="TrainingRoom.State"/> every frame; nothing here decides a rule. Numbers go through <see cref="RuNumber"/> (none of
/// them has a NumberLine). Only the slots take the mouse (for their tooltips); the rest lets clicks through to the room.
/// </summary>
public partial class CombatHud : Control
{
    public const float SlotWidth = 112;
    public const float SlotHeight = 72;
    public const float SlotGap = 6;
    public const float BottomMargin = 12;
    public const double BlinkSec = 0.28;

    /// <summary>Pixels the combat screen takes at the bottom (full-height windows end above it).</summary>
    public const float ReservedHeight = BottomMargin + SlotHeight + 6 + ResourceBar.BarHeight + 6 + 30 + 8;

    public static float BarWidth => 5 * SlotWidth + 4 * SlotGap;

    private CombatDriver _driver = null!;
    private SandboxSession _session = null!;
    private EffectNames _names = null!;
    private Func<Vector3, Vector2?> _project = null!;
    private readonly List<SlotView> _slots = new();
    private readonly List<EffectChip> _heroChips = new();
    private readonly List<EffectChip> _plateChips = new();
    private HFlowContainer _heroRow = null!;
    private PanelContainer _plate = null!;
    private Label _plateName = null!;
    private HFlowContainer _plateRow = null!;
    private Label _refusal = null!;

    public ResourceBar Resource { get; private set; } = null!;
    public IReadOnlyList<SlotView> Slots => _slots;

    /// <summary>Texts of the hero's buff and stack icons shown now, in order.</summary>
    public IEnumerable<string> HeroTexts => _heroChips.Where(c => c.Visible).Select(c => c.Text);

    /// <summary>The effects behind the hero's icons shown now.</summary>
    public IEnumerable<TimedEffect> HeroEffects => _heroChips.Where(c => c.Visible).Select(c => c.Effect!);

    /// <summary>The dummy plate: shown, its preset name, its icon texts, and its rect on screen.</summary>
    public bool PlateShown => _plate.Visible;
    public string PlateName => _plateName.Text;
    public IEnumerable<string> PlateTexts => _plateChips.Where(c => c.Visible).Select(c => c.Text);
    public Rect2 PlateRect => _plate.GetGlobalRect();
    public Control Plate => _plate;

    /// <summary>The refusal text shown over the hero now, or null.</summary>
    public string? RefusalShown => _refusal.Visible ? _refusal.Text : null;
    public Rect2 RefusalRect => new(_refusal.Position, _refusal.Size);

    public CombatHud()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>
    /// Builds the screen. <paramref name="project"/> turns a point of the room into a point of this control's screen (null when
    /// it is behind the camera).
    /// </summary>
    public void Setup(CombatDriver driver, SandboxSession session, Func<Vector3, Vector2?> project)
    {
        _driver = driver;
        _session = session;
        _names = new EffectNames(session.Data);
        _project = project;
        Name = "CombatHud";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var column = new VBoxContainer { Name = "Bottom", MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.End };
        column.AddThemeConstantOverride("separation", 6);
        AddChild(column);
        column.AnchorLeft = column.AnchorRight = 0.5f;
        column.AnchorTop = column.AnchorBottom = 1f;
        column.OffsetLeft = -BarWidth / 2;
        column.OffsetRight = BarWidth / 2;
        column.OffsetBottom = -BottomMargin;
        column.OffsetTop = -ReservedHeight + 8;
        column.GrowVertical = GrowDirection.Begin;

        _heroRow = new HFlowContainer { Name = "Buffs", MouseFilter = MouseFilterEnum.Ignore, Alignment = FlowContainer.AlignmentMode.Center, SizeFlagsVertical = SizeFlags.ShrinkEnd };
        _heroRow.AddThemeConstantOverride("h_separation", 4);
        _heroRow.AddThemeConstantOverride("v_separation", 4);
        column.AddChild(_heroRow);

        Resource = new ResourceBar { Name = "Resource" };
        column.AddChild(Resource);

        var bar = new HBoxContainer { Name = "Slots", MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", (int)SlotGap);
        column.AddChild(bar);
        for (int i = 1; i <= Dialoid.Sandbox.SandboxSession.SlotCount; i++)
        {
            var slot = new SlotView(i) { Name = "Slot" + i };
            _slots.Add(slot);
            bar.AddChild(slot);
        }

        _plate = new PanelContainer { Name = "DummyPlate", MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        var plateStyle = new StyleBoxFlat { BgColor = new Color(0.06f, 0.055f, 0.05f, 0.9f), BorderColor = new Color(0.55f, 0.27f, 0.22f), ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 4 };
        plateStyle.SetBorderWidthAll(1);
        plateStyle.SetCornerRadiusAll(3);
        _plate.AddThemeStyleboxOverride("panel", plateStyle);
        var plateBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        plateBox.AddThemeConstantOverride("separation", 3);
        _plate.AddChild(plateBox);
        _plateName = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _plateName.AddThemeColorOverride("font_color", new Color(0.95f, 0.82f, 0.72f));
        _plateName.AddThemeFontSizeOverride("font_size", 15);
        plateBox.AddChild(_plateName);
        _plateRow = new HFlowContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = FlowContainer.AlignmentMode.Center, CustomMinimumSize = new Vector2(0, 0) };
        _plateRow.AddThemeConstantOverride("h_separation", 3);
        _plateRow.AddThemeConstantOverride("v_separation", 3);
        plateBox.AddChild(_plateRow);
        AddChild(_plate);

        // The refusal text over the hero is drawn here, after the plate, so a plate next to the hero never covers it.
        _refusal = new Label { Name = "Refusal", MouseFilter = MouseFilterEnum.Ignore, Visible = false, HorizontalAlignment = HorizontalAlignment.Center };
        _refusal.AddThemeFontSizeOverride("font_size", 26);
        _refusal.AddThemeColorOverride("font_color", HeroView.RefusalColor);
        _refusal.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _refusal.AddThemeConstantOverride("outline_size", 4);
        _refusal.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        _refusal.AddThemeConstantOverride("shadow_offset_x", 2);
        _refusal.AddThemeConstantOverride("shadow_offset_y", 2);
        AddChild(_refusal);
        driver.Hero.DrawRefusal3D = false;
        driver.NumberLift = LiftAbovePlate;

        driver.Refused += e =>
        {
            if (e.Slot is { } s && s >= 1 && s <= _slots.Count) _slots[s - 1].Blink();
        };
    }

    public override void _Process(double delta) => Refresh(delta);

    /// <summary>Reads the fight's state into the screen (the self-test calls it after stepping the fight by hand).</summary>
    public void Refresh(double delta = 0)
    {
        if (_driver == null) return;
        var training = _driver.Training;
        var state = training.State;
        for (int i = 0; i < _slots.Count; i++)
            _slots[i].Show(i < training.Slots.Count ? training.Slots[i] : null, state, training.ResourceName, delta);
        Resource.Show(state, training.ResourceName);
        Fill(_heroRow, _heroChips, state?.HeroEffects ?? Array.Empty<TimedEffect>());

        var target = _driver.Dummy.RingTarget ?? 0;
        var effects = state != null && target < state.TargetEffects.Count ? state.TargetEffects[target] : Array.Empty<TimedEffect>();
        _plateName.Text = BreakdownText.DummyName(_session.Data, training.DummyId);
        Fill(_plateRow, _plateChips, effects);
        PlacePlate();
        PlaceRefusal();
    }

    /// <summary>The hero's refusal text («Перезарядка 2,1 с») where the hero shows it, fading as it does.</summary>
    private void PlaceRefusal()
    {
        var hero = _driver.Hero;
        if (hero.RefusalText is not { } text || _project(hero.RefusalAnchor) is not { } p)
        {
            _refusal.Visible = false;
            return;
        }
        _refusal.Visible = true;
        _refusal.Text = text;
        _refusal.Modulate = new Color(1, 1, 1, hero.RefusalAlpha);
        var size = _refusal.GetCombinedMinimumSize();
        _refusal.Size = size;
        // Over the head; when that would touch the dummy plate (the hero beside the dummy), under the feet below the target ring;
        // at chest level (across the figures) only as the last resort.
        var plate = _plate.Visible ? new Rect2(_plate.Position, _plate.Size).Grow(4) : new Rect2();
        var chest = _project(hero.Position + new Vector3(0, HeroView.Height * 0.6f, 0));
        var feet = _project(hero.Position);
        var spots = new List<Vector2> { p - size / 2 };
        if (feet is { } f) spots.Add(new Vector2(f.X - size.X / 2, f.Y + FeetGap));
        if (chest is { } c) spots.Add(c - size / 2);
        _refusal.Position = spots.FirstOrDefault(s => !plate.Intersects(new Rect2(s, size)), spots[^1]);
    }

    private void Fill(HFlowContainer row, List<EffectChip> chips, IReadOnlyList<TimedEffect> effects)
    {
        while (chips.Count < effects.Count)
        {
            var chip = new EffectChip();
            chips.Add(chip);
            row.AddChild(chip);
        }
        for (int i = 0; i < chips.Count; i++)
        {
            if (i < effects.Count) chips[i].Show(effects[i], _names.Of(effects[i]));
            else chips[i].Visible = false;
        }
    }

    /// <summary>Over the dummy's head (the highest figure, the pack's centre), centred; hidden when behind the camera.</summary>
    private void PlacePlate()
    {
        var dummy = _driver.Dummy;
        if (dummy.FigureCount == 0)
        {
            _plate.Visible = false;
            return;
        }
        var centre = Vector3.Zero;
        var top = 0f;
        for (int i = 0; i < dummy.FigureCount; i++)
        {
            var feet = dummy.TargetPosition(i);
            centre += feet;
            top = Mathf.Max(top, feet.Y + dummy.Figures[i].Model.TargetHeight);
        }
        centre /= dummy.FigureCount;
        var anchor = _project(new Vector3(centre.X, top + 0.05f, centre.Z));
        if (anchor is not { } p)
        {
            _plate.Visible = false;
            return;
        }
        _plate.Visible = true;
        var size = _plate.GetCombinedMinimumSize();
        var width = Mathf.Max(size.X, 150);
        _plate.Size = new Vector2(width, size.Y);
        _plate.Position = new Vector2(p.X - width / 2, p.Y - size.Y);
    }

    /// <summary>Pixels between the hero's feet and a refusal text placed under them (clear of the target ring).</summary>
    public const float FeetGap = 20;

    /// <summary>Pixels a floating number starts above the plate's top edge.</summary>
    public const float NumberGap = 8;

    /// <summary>
    /// Where a floating number starts: a start the plate would cover (at or below its top edge, beside it within 60 px) is lifted
    /// straight up so it starts <see cref="NumberGap"/> above the plate and floats up from there.
    /// </summary>
    public Vector3 LiftAbovePlate(Vector3 at)
    {
        if (!_plate.Visible || _project(at) is not { } p || _project(at + Vector3.Up) is not { } up) return at;
        var rect = new Rect2(_plate.Position, _plate.Size);
        var perMetre = p.Y - up.Y; // screen pixels per metre of height
        var limit = rect.Position.Y - NumberGap;
        if (perMetre < 0.1f || p.Y <= limit || p.X < rect.Position.X - 60 || p.X > rect.End.X + 60) return at;
        return at + Vector3.Up * ((p.Y - limit) / perMetre);
    }

    /// <summary>Seconds with one decimal under 10, whole above: «2,1», «14».</summary>
    public static string Seconds(double sec) => sec < 9.95 ? RuNumber.Format(sec, "0.0") : RuNumber.Format(Math.Round(sec, MidpointRounding.AwayFromZero), "0");
}

/// <summary>
/// One slot of the bar (UI review round 1): the key top-left; the cooldown seconds centred in the upper part with a dark curtain
/// coming down from the top by the remaining fraction; the name below (never under the seconds, at least 80 % bright on
/// cooldown); a red frame plus a dark-red shade only when the resource is short (the core's <see cref="LiveState.ResourceShortSlots"/>);
/// a yellow frame for the queued press and a blue one for the held channel; a refused press flashes the whole slot light yellow
/// for <see cref="CombatHud.BlinkSec"/> and shakes it ±3 px (no red: red means the resource).
/// </summary>
public partial class SlotView : Control
{
    public static readonly Color Background = new(0.1f, 0.095f, 0.09f, 0.94f);
    public static readonly Color EdgeColor = new(0.42f, 0.35f, 0.25f);
    public static readonly Color ShortColor = new(0.9f, 0.15f, 0.12f);
    public static readonly Color QueuedColor = new(0.98f, 0.82f, 0.3f);
    public static readonly Color HeldColor = new(0.45f, 0.85f, 1f);
    public static readonly Color FlashColor = new(1f, 0.96f, 0.78f);
    public const float ShakePixels = 3;

    private readonly Control _inner;
    private readonly Label _name;
    private readonly Label _key;
    private readonly Label _cooldown;
    private readonly Control _frame;
    private string _tooltipFor = "";
    private bool? _empty;

    public int Number { get; }
    public SlotInfo? Info { get; private set; }

    /// <summary>Cooldown seconds shown (null when the skill is ready) and its text.</summary>
    public double? CooldownLeft { get; private set; }
    public string CooldownText => _cooldown.Visible ? _cooldown.Text : "";

    /// <summary>A press would be refused for the resource now (red frame and shade), as the core says.</summary>
    public bool ResourceShort { get; private set; }

    /// <summary>What one use costs now (the core's <see cref="LiveState.CostBySlot"/>), or null for an empty slot.</summary>
    public double? CostNow { get; private set; }
    public bool Queued { get; private set; }
    public bool Held { get; private set; }
    public double BlinkLeft { get; private set; }
    public string NameText => _name.Text;

    /// <summary>The colour of the frame drawn now (red only for a short resource).</summary>
    public Color FrameColor { get; private set; }

    /// <summary>Horizontal shake of the slot's face now, pixels.</summary>
    public float Shake => _inner.Position.X;

    public SlotView(int number)
    {
        Number = number;
        CustomMinimumSize = new Vector2(CombatHud.SlotWidth, CombatHud.SlotHeight);
        MouseFilter = MouseFilterEnum.Stop;

        _inner = new Control { Name = "Face", MouseFilter = MouseFilterEnum.Ignore };
        _inner.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _inner.Draw += () => _inner.DrawRect(new Rect2(Vector2.Zero, _inner.Size), Background);
        AddChild(_inner);

        _frame = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _frame.Draw += DrawFrame;
        _inner.AddChild(_frame);

        _name = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _name.AddThemeFontSizeOverride("font_size", 14);
        _name.AddThemeConstantOverride("line_spacing", -2);
        _name.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        (_name.OffsetLeft, _name.OffsetRight, _name.OffsetTop, _name.OffsetBottom) = (5, -5, 30, -3);
        _inner.AddChild(_name);

        _key = new Label { Text = number.ToString(), MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(6, 1) };
        _key.AddThemeFontSizeOverride("font_size", 14);
        _key.AddThemeColorOverride("font_color", new Color(0.95f, 0.8f, 0.45f));
        _inner.AddChild(_key);

        // The seconds are centred in the upper part, the name sits below them: they never cover each other.
        _cooldown = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _cooldown.AddThemeFontSizeOverride("font_size", 23);
        _cooldown.AddThemeColorOverride("font_color", Colors.White);
        _cooldown.AddThemeColorOverride("font_outline_color", Colors.Black);
        _cooldown.AddThemeConstantOverride("outline_size", 6);
        _cooldown.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        (_cooldown.OffsetLeft, _cooldown.OffsetRight, _cooldown.OffsetTop, _cooldown.OffsetBottom) = (18, -18, 0, 32);
        _inner.AddChild(_cooldown);
    }

    /// <summary>A refused press on this slot: the slot flashes and shakes for <see cref="CombatHud.BlinkSec"/>.</summary>
    public void Blink() => BlinkLeft = CombatHud.BlinkSec;

    public void Show(SlotInfo? info, LiveState? state, string resourceName, double delta)
    {
        Info = info;
        BlinkLeft = Math.Max(0, BlinkLeft - delta);
        var fade = (float)(BlinkLeft / CombatHud.BlinkSec);
        _inner.Position = new Vector2(BlinkLeft > 0 ? ShakePixels * fade * Mathf.Sin((float)(BlinkLeft * 70)) : 0, 0);
        _name.Text = info?.Name ?? "пусто";
        if (_empty != (info == null)) // a theme override relayouts: only when it changes
        {
            _empty = info == null;
            _name.AddThemeColorOverride("font_color", info == null ? new Color(0.5f, 0.47f, 0.42f) : new Color(0.93f, 0.9f, 0.84f));
        }
        CooldownLeft = info != null && state != null && state.CooldownLeftBySlot.TryGetValue(Number, out var left) && left > 0 ? left : null;
        CostNow = info != null && state != null && state.CostBySlot.TryGetValue(Number, out var cost) ? cost : null;
        ResourceShort = info != null && state != null && state.ResourceShortSlots.Contains(Number);
        Queued = state?.QueuedSlot == Number;
        Held = state?.HeldSlot == Number;
        _cooldown.Visible = CooldownLeft != null;
        if (CooldownLeft is { } cd) _cooldown.Text = CombatHud.Seconds(cd);
        _name.Modulate = CooldownLeft != null || ResourceShort ? new Color(0.82f, 0.82f, 0.82f) : Colors.White;
        FrameColor = ResourceShort ? ShortColor : Held ? HeldColor : Queued ? QueuedColor : EdgeColor;

        var costKey = CostNow is { } c ? RuNumber.Format(c, "0.#") : "";
        var tipKey = info == null ? "" : $"{info.SkillId}|{info.Cost}|{info.CostPerSec}|{info.CooldownSec}|{info.UseTimeSec}|{resourceName}|{costKey}";
        if (tipKey != _tooltipFor)
        {
            _tooltipFor = tipKey;
            TooltipText = Tooltip(Number, info, resourceName, CostNow);
        }
        _frame.QueueRedraw();
    }

    /// <summary>The slot's tooltip: name, key, cost (and what it costs now, when buffs change it), cooldown, use time.</summary>
    public static string Tooltip(int number, SlotInfo? info, string resourceName, double? costNow = null)
    {
        if (info == null) return $"Слот {number} пуст: скилл ставится в окне «Скиллы» (K)";
        var resource = ResourceGenitive(resourceName);
        var lines = new List<string> { $"{info.Name} — клавиша {number}" };
        if (info.IsChannel)
            lines.Add(info.CostPerSec > 0
                ? $"Стоимость: {RuNumber.Format(info.CostPerSec, "0.##")} {resource} в секунду ({RuNumber.Format(info.Cost, "0.##")} за тик)"
                : "Стоимость: нет");
        else
            lines.Add(info.Cost > 0 ? $"Стоимость: {RuNumber.Format(info.Cost, "0.##")} {resource}" : "Стоимость: нет");
        if (costNow is { } now && Math.Abs(now - info.Cost) > 1e-6)
            lines.Add($"Сейчас стоит: {RuNumber.Format(now, "0.##")} {resource}{(info.IsChannel ? " за тик" : "")}");
        lines.Add(info.CooldownSec > 0 ? $"Перезарядка: {RuNumber.Format(info.CooldownSec, "0.##")} с" : "Перезарядка: нет");
        lines.Add(info.IsChannel
            ? $"Канал: тик {RuNumber.Format(info.UseTimeSec, "0.##")} с, пока клавиша зажата"
            : $"Время применения: {RuNumber.Format(info.UseTimeSec, "0.##")} с");
        return string.Join("\n", lines);
    }

    /// <summary>«Мана» → «маны», «Ярость» → «ярости» (the cost line).</summary>
    public static string ResourceGenitive(string name) => name switch
    {
        "Мана" => "маны",
        "Ярость" => "ярости",
        "" => "ресурса",
        _ => name.ToLowerInvariant(),
    };

    private void DrawFrame()
    {
        var size = _frame.Size;
        var rect = new Rect2(Vector2.Zero, size);
        if (Info != null && CooldownLeft is { } left)
        {
            // A curtain from the top: its height is the cooldown still left.
            var fraction = Info.CooldownSec > 0 ? (float)Math.Clamp(left / Info.CooldownSec, 0, 1) : 1f;
            _frame.DrawRect(new Rect2(0, 0, size.X, size.Y * fraction), new Color(0, 0, 0, 0.6f));
        }
        if (ResourceShort) _frame.DrawRect(rect, new Color(0.3f, 0, 0, 0.45f));
        if (BlinkLeft > 0)
        {
            var a = (float)(BlinkLeft / CombatHud.BlinkSec);
            _frame.DrawRect(rect, FlashColor with { A = 0.5f * a });
        }
        var width = FrameColor == EdgeColor ? 1f : 3f;
        _frame.DrawRect(rect.Grow(-width / 2), FrameColor, filled: false, width: width);
    }
}

/// <summary>The resource bar over the slots: fill and «Мана 45 / 100».</summary>
public partial class ResourceBar : Control
{
    public const float BarHeight = 22;

    private readonly Label _label;
    private double _value;
    private double _max;
    private string _resourceId = "";

    public string Text => _label.Text;

    public ResourceBar()
    {
        CustomMinimumSize = new Vector2(CombatHud.BarWidth, BarHeight);
        MouseFilter = MouseFilterEnum.Ignore;
        _label = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _label.AddThemeFontSizeOverride("font_size", 14);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 4);
        _label.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_label);
    }

    /// <summary>The text of the bar: «Мана 45 / 100» (whole numbers, rounded down so a short cost never reads as enough).</summary>
    public static string TextOf(string name, double value, double max) =>
        $"{name} {RuNumber.Grouped(Math.Floor(value))} / {RuNumber.Grouped(Math.Round(max, MidpointRounding.AwayFromZero))}";

    public void Show(LiveState? state, string name)
    {
        Visible = state != null;
        if (state == null) return;
        _value = state.Resource;
        _max = state.ResourceMax;
        _resourceId = state.ResourceId;
        _label.Text = TextOf(name, state.Resource, state.ResourceMax);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, new Color(0.05f, 0.045f, 0.04f, 0.92f));
        var fill = _resourceId == "fury" ? new Color(0.72f, 0.2f, 0.1f) : new Color(0.18f, 0.36f, 0.85f);
        var fraction = _max > 0 ? (float)Math.Clamp(_value / _max, 0, 1) : 0f;
        DrawRect(new Rect2(1, 1, (Size.X - 2) * fraction, Size.Y - 2), fill);
        DrawRect(rect.Grow(-0.5f), SlotView.EdgeColor, filled: false, width: 1);
    }
}

/// <summary>An icon of the combat screen: a buff, stack, ailment or debuff with its stacks and timer, as a small colored chip.</summary>
public partial class EffectChip : PanelContainer
{
    private readonly Label _label;
    private readonly StyleBoxFlat _style;

    public TimedEffect? Effect { get; private set; }
    public string Text => _label.Text;

    public EffectChip()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _style = new StyleBoxFlat { BgColor = new Color(0.07f, 0.065f, 0.06f, 0.92f), ContentMarginLeft = 7, ContentMarginRight = 6, ContentMarginTop = 1, ContentMarginBottom = 2 };
        _style.SetBorderWidthAll(1);
        _style.BorderWidthLeft = 4;
        _style.SetCornerRadiusAll(2);
        AddThemeStyleboxOverride("panel", _style);
        _label = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _label.AddThemeFontSizeOverride("font_size", 14);
        _label.AddThemeColorOverride("font_color", new Color(0.94f, 0.91f, 0.85f));
        AddChild(_label);
    }

    /// <summary>«Боевой клич 6,2 с», «Поджог ×3 · 2,4 с», «Статический заряд: 4».</summary>
    public static string TextOf(TimedEffect e, string name)
    {
        if (name.Length > 0) name = char.ToUpperInvariant(name[0]) + name[1..]; // ailment names are lowercase («поджог»)
        if (e.Kind == TimedEffectKind.MechanicStack) return $"{name}: {RuNumber.Format(e.Stacks, "0")}";
        var text = e.Stacks > 1 ? $"{name} ×{RuNumber.Format(e.Stacks, "0")}" : name;
        if (e.TimeLeft is not { } t || double.IsInfinity(t)) return text;
        // Stacks and timer are two numbers: a dot keeps «×2» and «3,1 с» apart.
        return e.Stacks > 1 ? $"{text} · {CombatHud.Seconds(t)} с" : $"{text} {CombatHud.Seconds(t)} с";
    }

    public void Show(TimedEffect effect, string name)
    {
        Visible = true;
        Effect = effect;
        _label.Text = TextOf(effect, name);
        var color = ColorOf(effect);
        if (_style.BorderColor != color) _style.BorderColor = color;
    }

    /// <summary>Buffs green, stacks gold, ailments by their damage colour (spec §4 «Читаемость боя»), debuffs grey.</summary>
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
}

/// <summary>
/// Russian names of the combat screen's icons. Ailments and mechanic stacks have their names in the sandbox's texts; a buff is
/// named after the skill of the same id («Боевой клич»), else after the skill-tree, passive or ascendancy node that applies it;
/// a mechanic's buff or debuff after the node that carries the mechanic. An unknown id is shown as is.
/// </summary>
public sealed class EffectNames
{
    private readonly Dictionary<string, string> _buffs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _mechanics = new(StringComparer.Ordinal);

    public EffectNames(GameData data)
    {
        var nodes = data.Skills.Values.SelectMany(s => s.Tree.Select(n => (n.Name, n.Effects)))
            .Concat(data.Passives.Nodes.Values.Select(n => (n.Name, n.Effects)))
            .Concat(data.Ascendancies.Values.SelectMany(a => a.Notables.Prepend(a.Start)).Select(n => (n.Name, n.Effects)));
        foreach (var (name, effects) in nodes)
            foreach (var e in effects)
                switch (e)
                {
                    case GrantProcEffect { Action: ProcAction.ApplyBuff buff }: _buffs.TryAdd(buff.BuffId, name); break;
                    case MechanicEffect mechanic: _mechanics.TryAdd(mechanic.MechanicId, name); break;
                }
        foreach (var (id, skill) in data.Skills) _buffs[id] = skill.Name; // a skill's own buff carries the skill's name
    }

    public string Of(TimedEffect e) => e.Kind switch
    {
        TimedEffectKind.Ailment => BreakdownText.AilmentName(e.Id),
        TimedEffectKind.MechanicStack => BreakdownText.MechanicStackName(e.Id) ?? e.Id,
        TimedEffectKind.Debuff => BreakdownText.MechanicStackName(e.Id) ?? _mechanics.GetValueOrDefault(e.Id, e.Id),
        _ when e.Id.StartsWith("mechanic:", StringComparison.Ordinal) => _mechanics.GetValueOrDefault(e.Id["mechanic:".Length..], e.Id),
        _ => _buffs.GetValueOrDefault(e.Id, e.Id),
    };
}
