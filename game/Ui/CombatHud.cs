using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Explain;
using Dialoid.Core.Simulation;
using Dialoid.Game.Room;
using Dialoid.Game.Ui.Hud;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>
/// The room's combat screen (room spec §4 «Экран боя»), assembled from the HUD's reusable components (HUD spec §4 items 3, 6–8,
/// §7 «Сейчас»): bottom centre, from the top — the hero's ailments and debuffs (<see cref="EffectRowView"/>, torn dark-red frame;
/// empty in the room, the dummy never hits), the hero's buffs and stacks (bronze frame), the resource bar
/// (<see cref="ResourceBarView"/>) and the slot bar 1–5 (<see cref="SkillSlotView"/>). The target plate
/// (<see cref="TargetPlateView"/>) floats over the head of the target's figure: with the mouse the figure under the cursor, else
/// the figure the last skill hit; with the gamepad the target RB chose, else the last hit (<see cref="ChooseTarget"/>). Each frame
/// this class builds the components' records from <see cref="TrainingRoom.State"/> and places the views; nothing here decides a
/// rule. Only the slots and the rows' «+N» take the mouse (for their tooltips); the rest lets clicks through to the room.
/// </summary>
public partial class CombatHud : Control
{
    public const float SlotWidth = SkillSlotView.Width;
    public const float SlotHeight = SkillSlotView.Height;
    public const float SlotGap = 6;
    public const float BottomMargin = 12;
    public const double BlinkSec = SkillSlotView.BlinkSec;

    /// <summary>Icons a hero row shows at most before it folds the rest into «+N».</summary>
    public const int HeroRowMax = 9;

    /// <summary>Icons the target plate shows at most before «+N».</summary>
    public const int PlateRowMax = 6;

    /// <summary>Pixels the combat screen takes at the bottom (full-height windows end above it).</summary>
    public const float ReservedHeight = BottomMargin + SlotHeight + 6 + ResourceBarView.BarHeight + 6 + 30 + 8;

    public static float BarWidth => 5 * SlotWidth + 4 * SlotGap;

    /// <summary>The combat screen's left edge on a screen <paramref name="width"/> wide (its bottom column is centred).</summary>
    public static float LeftEdge(float width) => width / 2 - BarWidth / 2;

    /// <summary>Pixels kept between a window and the combat screen.</summary>
    public const float WindowGap = 8;

    /// <summary>The bottom column (rows, resource, slots) on screen.</summary>
    public Rect2 BottomRect => _column.GetGlobalRect();

    private VBoxContainer _column = null!;
    private Label _toast = null!;
    private double _toastLeft;

    /// <summary>The device message shown now («Геймпад отключен»), or null.</summary>
    public string? ToastShown => _toast.Visible ? _toast.Text : null;

    public const double ToastSec = 3;

    private CombatDriver _driver = null!;
    private SandboxSession _session = null!;
    private EffectNames _names = null!;
    private Func<Vector3, Vector2?> _project = null!;
    private readonly List<SkillSlotView> _slots = new();
    private Label _refusal = null!;
    private Control _marker = null!;
    private SharedTooltip _focusTip = null!;
    private Vector2? _mouse;
    private bool _gamepad;
    private IReadOnlyList<EffectChipData>? _previewBuffs, _previewDebuffs;

    public ResourceBarView Resource { get; private set; } = null!;
    public IReadOnlyList<SkillSlotView> Slots => _slots;
    public EffectRowView HeroBuffs { get; private set; } = null!;
    public EffectRowView HeroDebuffs { get; private set; } = null!;
    public TargetPlateView TargetPlate { get; private set; } = null!;

    /// <summary>The figure under a point of this control's screen, or null (set by the scene: it knows the room's camera).</summary>
    public Func<Vector2, int?>? FigureAt { get; set; }

    /// <summary>Texts of the hero's buff and stack icons shown now, in order («+N» last).</summary>
    public IEnumerable<string> HeroTexts => HeroBuffs.ShownTexts;

    /// <summary>The effects behind the hero's buff icons shown now.</summary>
    public IEnumerable<TimedEffect> HeroEffects => HeroBuffs.ShownEffects.Where(e => e.Effect != null).Select(e => e.Effect!);

    /// <summary>The target plate: shown, whose (core target), its name, its icon texts, and its rect on screen.</summary>
    public bool PlateShown => TargetPlate.Visible;
    public int? PlateTarget => TargetPlate.Visible ? TargetPlate.Data?.Target : null;
    public string PlateName => TargetPlate.NameText;
    public IEnumerable<string> PlateTexts => TargetPlate.Effects.ShownTexts;
    public Rect2 PlateRect => TargetPlate.GetGlobalRect();
    public Control Plate => TargetPlate;

    /// <summary>The refusal text shown over the hero now, or null.</summary>
    public string? RefusalShown => _refusal.Visible ? _refusal.Text : null;
    public Rect2 RefusalRect => new(_refusal.Position, _refusal.Size);

    /// <summary>The full list shown for a focused «+N», or null.</summary>
    public string? FocusTooltip => _focusTip.Shown;
    public Rect2 FocusTooltipRect => _focusTip.GetGlobalRect();

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
        _column = column;

        HeroDebuffs = new EffectRowView(EffectRowKind.Debuffs, HeroRowMax) { Name = "Debuffs", SizeFlagsVertical = SizeFlags.ShrinkEnd, Visible = false };
        column.AddChild(HeroDebuffs);
        HeroBuffs = new EffectRowView(EffectRowKind.Buffs, HeroRowMax) { Name = "Buffs", SizeFlagsVertical = SizeFlags.ShrinkEnd };
        column.AddChild(HeroBuffs);

        Resource = new ResourceBarView(BarWidth) { Name = "Resource" };
        column.AddChild(Resource);

        var bar = new HBoxContainer { Name = "Slots", MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", (int)SlotGap);
        column.AddChild(bar);
        for (int i = 1; i <= SandboxSession.SlotCount; i++)
        {
            var slot = new SkillSlotView(i) { Name = "Slot" + i };
            _slots.Add(slot);
            bar.AddChild(slot);
        }

        TargetPlate = new TargetPlateView(PlateRowMax) { Name = "DummyPlate" };
        AddChild(TargetPlate);
        _marker = new Control { Name = "PlateMarker", MouseFilter = MouseFilterEnum.Ignore, Visible = false, Size = new Vector2(2 * MarkerHeight, MarkerHeight) };
        _marker.Draw += () =>
        {
            var pts = new[] { Vector2.Zero, new Vector2(2 * MarkerHeight, 0), new Vector2(MarkerHeight, MarkerHeight) };
            _marker.DrawColoredPolygon(pts, new Color(1f, 0.93f, 0.82f, 0.9f));
            _marker.DrawPolyline([.. pts, pts[0]], new Color(0, 0, 0, 0.8f), 1);
        };
        AddChild(_marker);

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
        driver.Numbers.KeepOut = PlateKeepOut;

        driver.Refused += e =>
        {
            if (e.Slot is { } s && s >= 1 && s <= _slots.Count) _slots[s - 1].Blink();
        };

        // The full list of a focused «+N» (the shared tooltip component; the hover one is the windows' own).
        _focusTip = new SharedTooltip { Name = "HudFocusTip" };
        AddChild(_focusTip);

        // Device messages («Геймпад отключен») at the top centre for a few seconds; slot keys follow the device.
        _toast = new Label { Name = "Toast", Visible = false, MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
        _toast.AddThemeFontSizeOverride("font_size", 20);
        _toast.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.62f));
        _toast.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _toast.AddThemeConstantOverride("outline_size", 5);
        AddChild(_toast);
        _toast.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        _toast.OffsetTop = TopBar.Height + 16;
        if (Dialoid.Game.Input.InputDevice.Instance is { } device)
        {
            device.Message += ShowToast;
            device.Changed += kind => ShowDevice(kind == Dialoid.Game.Input.InputKind.Gamepad);
            ShowDevice(device.Active == Dialoid.Game.Input.InputKind.Gamepad);
        }
    }

    /// <summary>Slot captions: digits on the keyboard, the gamepad's buttons («[X]» … «[RT]») on the gamepad.</summary>
    public void ShowDevice(bool gamepad)
    {
        _gamepad = gamepad;
        if (gamepad) _mouse = null; // the hidden cursor hovers nothing
        foreach (var slot in _slots) slot.ShowKey(KeyOf(slot.Number, gamepad));
    }

    public static string KeyOf(int slot, bool gamepad) => gamepad ? ButtonPrompts.SlotGlyph(slot) : slot.ToString();

    public void HideToast()
    {
        _toastLeft = 0;
        _toast.Visible = false;
    }

    public void ShowToast(string text)
    {
        _toast.Text = text;
        _toast.Visible = true;
        _toast.Modulate = Colors.White;
        _toast.ResetSize();
        _toast.Position = new Vector2((Size.X - _toast.Size.X) / 2, _toast.Position.Y);
        _toastLeft = ToastSec;
    }

    /// <summary>
    /// Shows <paramref name="buffs"/> and <paramref name="debuffs"/> in the hero's rows instead of the fight's (the self-test's
    /// stand-in data: in the room the hero never has debuffs); null for a row gives it back to the fight.
    /// </summary>
    public void PreviewHeroRows(IReadOnlyList<EffectChipData>? buffs, IReadOnlyList<EffectChipData>? debuffs)
    {
        _previewBuffs = buffs;
        _previewDebuffs = debuffs;
        Refresh();
    }

    public override void _Input(InputEvent e)
    {
        // The cursor, for the figure under it (the room's input may never see a motion over the interface).
        if (e is InputEventMouse mouse) _mouse = mouse.Position;
    }

    public override void _Notification(int what)
    {
        // A cursor that left the window hovers nothing (code review, task 14 round 1).
        if (what is (int)NotificationWMMouseExit or (int)NotificationWMWindowFocusOut) _mouse = null;
    }

    /// <summary>Forgets the cursor (it left the room).</summary>
    public void ClearHover() => _mouse = null;

    /// <summary>The «+N» icons the gamepad can visit now, top to bottom: the plate's, the debuffs', the buffs' (folded rows only).</summary>
    public IReadOnlyList<Control> PadTargets() =>
        new[] { TargetPlate.Effects, HeroDebuffs, HeroBuffs }.Where(r => r.Overflow.Visible && r.Overflow.IsVisibleInTree()).Select(r => (Control)r.Overflow).ToList();

    public override void _Process(double delta)
    {
        Refresh(delta);
        if (_toastLeft > 0)
        {
            _toastLeft -= delta;
            _toast.Modulate = new Color(1, 1, 1, (float)Math.Clamp(_toastLeft / 0.6, 0, 1));
            if (_toastLeft <= 0) _toast.Visible = false;
        }
    }

    /// <summary>The records of the components from the fight's state now (the self-test compares them with the state).</summary>
    public SkillSlotData SlotData(int number) =>
        SkillSlotData.From(number, number - 1 < _driver.Training.Slots.Count ? _driver.Training.Slots[number - 1] : null, _driver.Training.State,
            _driver.Training.ResourceName, KeyOf(number, _gamepad));

    public ResourceBarData? ResourceData() => ResourceBarData.From(_driver.Training.State, _driver.Training.ResourceName);

    public IReadOnlyList<EffectChipData> BuffData() =>
        _previewBuffs ?? EffectChipData.From(_driver.Training.State?.HeroEffects ?? Array.Empty<TimedEffect>(), _names);

    /// <summary>The hero's ailments and debuffs: none in the room (the dummy never hits) unless the self-test shows stand-ins.</summary>
    public IReadOnlyList<EffectChipData> DebuffData() => _previewDebuffs ?? Array.Empty<EffectChipData>();

    public TargetPlateData? PlateData(int? target) => target is { } t ? TargetPlateData.From(_driver.Training.State, t, PlateNameOf(t), _names) : null;

    /// <summary>The preset's name; on a pack with the log's numbering: «Рой · цель 3».</summary>
    public string PlateNameOf(int target)
    {
        var name = BreakdownText.DummyName(_session.Data, _driver.Training.DummyId);
        return _driver.Dummy.FigureCount > 1 ? $"{name} · цель {target + 1}" : name; // the log numbers targets from 1 (Texts.LogTarget)
    }

    /// <summary>
    /// Whose plate (HUD spec §4 item 8, the ring's target): with the mouse the figure under the cursor, else the ring's figure —
    /// the newer of the last hit and the Tab choice; with the gamepad the ring's figure, which holds the RB target until the next
    /// RB (<see cref="CombatDriver"/>). In the room the fight always runs against the pack's first target, so before any hit the
    /// plate is that target's (the room's builder decision: «no target» only when there is no figure; the tower's world layer
    /// needs a real no-target state).
    /// </summary>
    public static int? ChooseTarget(bool gamepad, int? hovered, int? ring, int figures)
    {
        if (figures <= 0) return null;
        var target = gamepad ? ring : hovered ?? ring;
        return Math.Clamp(target ?? 0, 0, figures - 1);
    }

    /// <summary>The figure under the cursor now (keyboard-and-mouse mode, the cursor over the room, not over the interface).</summary>
    public int? HoveredFigure()
    {
        if (_gamepad || _mouse is not { } at || FigureAt == null) return null;
        var over = GetViewport().GuiGetHoveredControl();
        if (over != null && over is not SubViewportContainer) return null;
        return FigureAt(at);
    }

    /// <summary>Reads the fight's state into the screen (the self-test calls it after stepping the fight by hand).</summary>
    public void Refresh(double delta = 0)
    {
        if (_driver == null) return;
        for (int i = 0; i < _slots.Count; i++) _slots[i].Show(SlotData(i + 1), delta);
        Resource.Show(ResourceData());
        var buffs = BuffData();
        HeroBuffs.Show(buffs);
        var debuffs = DebuffData();
        if (HeroDebuffs.Visible != debuffs.Count > 0) HeroDebuffs.Visible = debuffs.Count > 0;
        HeroDebuffs.Show(debuffs);

        var dummy = _driver.Dummy;
        var target = ChooseTarget(_gamepad, HoveredFigure(), dummy.RingTarget, dummy.FigureCount);
        TargetPlate.Show(PlateData(target));
        PlacePlate(target);
        _driver.Numbers.KeepClear(); // numbers the plate just moved onto rise above it in this same frame
        PlaceRefusal();
        UpdateFocusTip();
    }

    /// <summary>A focused «+N» shows its row's full list over the row.</summary>
    private void UpdateFocusTip()
    {
        var focused = new[] { HeroBuffs, HeroDebuffs, TargetPlate.Effects }.FirstOrDefault(r => r.Overflow.HasFocus() && r.Overflow.IsVisibleInTree());
        if (focused == null)
        {
            if (_focusTip.Visible) _focusTip.Visible = false;
            return;
        }
        var size = _focusTip.Show(focused.Overflow.TooltipText);
        var at = focused.Overflow.GetGlobalRect();
        if (focused == TargetPlate.Effects)
        {
            // Beside the plate, its top level with the plate's.
            var plate = TargetPlate.GetGlobalRect();
            var right = plate.End.X + 8;
            var x = right + size.X <= Size.X ? right : plate.Position.X - 8 - size.X;
            _focusTip.PlaceAt(new Vector2(x, plate.Position.Y), size, Size, TopBar.Height + 4);
            return;
        }
        // Beside the bottom column (it never covers the rows' other icons), its bottom level with the «+N».
        _focusTip.PlaceAt(new Vector2(Mathf.Max(at.End.X, BottomRect.End.X) + 8, at.End.Y - size.Y), size, Size, 0);
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
        // Over the head; when that would touch the target plate (the hero beside the dummy), under the feet below the target ring;
        // at chest level (across the figures) only as the last resort.
        var plate = TargetPlate.Visible ? new Rect2(TargetPlate.Position, TargetPlate.Size).Grow(4) : new Rect2();
        var chest = _project(hero.Position + new Vector3(0, HeroView.Height * 0.6f, 0));
        var feet = _project(hero.Position);
        var spots = new List<Vector2> { p - size / 2 };
        if (feet is { } f) spots.Add(new Vector2(f.X - size.X / 2, f.Y + FeetGap));
        if (chest is { } c) spots.Add(c - size / 2);
        _refusal.Position = spots.FirstOrDefault(s => !plate.Intersects(new Rect2(s, size)), spots[^1]);
    }

    /// <summary>Over the head of the target's figure, centred; hidden when there is no target or it is behind the camera.</summary>
    private void PlacePlate(int? target)
    {
        var dummy = _driver.Dummy;
        // No plate, no chevron: a hidden plate never leaves its marker behind (task 14 review).
        if (target is not { } t || t >= dummy.FigureCount || !TargetPlate.Visible)
        {
            TargetPlate.Visible = false;
            _marker.Visible = false;
            return;
        }
        var feet = dummy.TargetPosition(t);
        var top = feet.Y + dummy.Figures[dummy.FigureOf(t)].Model.TargetHeight;
        if (_project(new Vector3(feet.X, top + 0.05f, feet.Z)) is not { } p)
        {
            TargetPlate.Visible = false;
            _marker.Visible = false;
            return;
        }
        var size = TargetPlate.GetCombinedMinimumSize();
        var width = Mathf.Max(size.X, TargetPlateView.MinWidth);
        TargetPlate.Size = new Vector2(width, size.Y);
        // On a pack a small pale chevron over the plate's figure tells which one it is (UI review, task 14 round 1).
        var pack = dummy.FigureCount > 1;
        var lift = pack ? MarkerHeight + 2 : 0;
        // Never under the top bar (a far figure's head near the top edge): then the plate covers the head instead.
        var y = p.Y - size.Y - lift;
        var clamped = y < TopBar.Height + 4;
        TargetPlate.Position = new Vector2(p.X - width / 2, clamped ? TopBar.Height + 4 : y);
        _marker.Visible = pack && !clamped;
        if (_marker.Visible) _marker.Position = new Vector2(p.X - MarkerHeight, p.Y - MarkerHeight);
    }

    /// <summary>Height of the chevron over the plate's figure on a pack, pixels.</summary>
    public const float MarkerHeight = 8;

    /// <summary>The chevron over the plate's figure is shown (packs only).</summary>
    public bool MarkerShown => _marker.Visible;
    public Vector2 MarkerTip => _marker.Position + new Vector2(MarkerHeight, MarkerHeight);

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
        // The start's centre only; every frame PlateKeepOut also keeps the whole number box off the plate.
        if (!TargetPlate.Visible || _project(at) is not { } p || _project(at + Vector3.Up) is not { } up) return at;
        var rect = new Rect2(TargetPlate.Position, TargetPlate.Size);
        var perMetre = p.Y - up.Y; // screen pixels per metre of height
        var limit = rect.Position.Y - NumberGap;
        if (perMetre < 0.1f || p.Y <= limit || p.X < rect.Position.X - 60 || p.X > rect.End.X + 60) return at;
        return at + Vector3.Up * ((p.Y - limit) / perMetre);
    }

    /// <summary>Keep the entire projected number inside the viewport below the toolbar, then clear the target plate.
    /// Called at spawn, throughout the rise, and after the plate moves. Camera screen axes preserve depth.</summary>
    public Vector3 PlateKeepOut(Vector3 centre, Vector3 halfRight, Vector3 halfUp)
    {
        var right = halfRight.Normalized();
        var up = halfUp.Normalized();
        if (_project(centre) is not { } c || _project(centre + right) is not { } r || _project(centre + up) is not { } u) return Vector3.Zero;
        var px = r.X - c.X;
        var py = c.Y - u.Y;
        if (px < 0.1f || py < 0.1f) return Vector3.Zero;
        var min = c;
        var max = c;
        foreach (var corner in new[] { halfRight + halfUp, halfRight - halfUp, -halfRight + halfUp, -halfRight - halfUp })
        {
            if (_project(centre + corner) is not { } q) return Vector3.Zero;
            min = min.Min(q);
            max = max.Max(q);
        }
        var safe = new Rect2(4, TopBar.Height + 4, Size.X - 8, Size.Y - TopBar.Height - 8);
        var extent = max - min;
        var pos = new Vector2(Mathf.Clamp(min.X, safe.Position.X, safe.End.X - extent.X),
            Mathf.Clamp(min.Y, safe.Position.Y, safe.End.Y - extent.Y));
        if (TargetPlate.Visible)
        {
            var plate = new Rect2(TargetPlate.Position, TargetPlate.Size).Grow(NumberGap);
            if (new Rect2(pos, extent).Intersects(plate))
            {
                // Prefer above; near the toolbar use the nearest side that fits, otherwise below.
                var above = plate.Position.Y - extent.Y;
                var left = plate.Position.X - extent.X;
                var rightEdge = plate.End.X;
                if (above >= safe.Position.Y) pos.Y = above;
                else if (left >= safe.Position.X && (rightEdge + extent.X > safe.End.X || pos.X - left <= rightEdge - pos.X)) pos.X = left;
                else if (rightEdge + extent.X <= safe.End.X) pos.X = rightEdge;
                else pos.Y = Mathf.Min(plate.End.Y, safe.End.Y - extent.Y);
            }
        }
        var delta = pos - min;
        return right * (delta.X / px) - up * (delta.Y / py);
    }
    /// <summary>Seconds with one decimal under 10, whole above: «2,1», «14».</summary>
    public static string Seconds(double sec) => HudFormat.Seconds(sec);
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
