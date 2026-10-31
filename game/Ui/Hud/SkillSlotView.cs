using Dialoid.Core.Simulation;
using Dialoid.Core.Explain;
using Dialoid.Sandbox;
using Godot;
using Dialoid.Game.Ui.Art;

namespace Dialoid.Game.Ui.Hud;

/// <summary>
/// One skill slot (HUD spec §4 item 3; UI review of task 10 round 1): the key of the active device top-left; the cooldown seconds
/// centred in the upper part with a dark curtain coming down from the top by the remaining fraction; the name below (never under
/// the seconds, at least 80 % bright on cooldown); a red frame plus a dark-red shade only when the resource is short (the core's
/// <see cref="LiveState.ResourceShortSlots"/>); a yellow frame for the queued press and a blue one for the held channel; a
/// refused press flashes the whole slot light yellow for <see cref="BlinkSec"/> and shakes it ±3 px (no red: red means the
/// resource). It draws a <see cref="SkillSlotData"/> and nothing else; the tooltip gives cost, cooldown and use time.
/// </summary>
public partial class SkillSlotView : Control
{
    public const float Width = 112;
    public const float Height = 72;
    public const double BlinkSec = 0.28;

    public static readonly Color Background = new(0.1f, 0.095f, 0.09f, 0.94f);
    public static readonly Color EdgeColor = new(0.42f, 0.35f, 0.25f);
    public static readonly Color ShortColor = new(0.9f, 0.15f, 0.12f);
    public static readonly Color QueuedColor = new(0.98f, 0.82f, 0.3f);
    public static readonly Color HeldColor = new(0.45f, 0.85f, 1f);
    public static readonly Color FlashColor = new(1f, 0.96f, 0.78f);
    public const float ShakePixels = 3;

    private readonly Control _inner;
    private readonly TextureRect _icon;
    private readonly Label _name;
    private readonly Label _key;
    private readonly Label _cooldown;
    private readonly Control _frame;
    private string _tooltipFor = "";
    private bool? _empty;

    public int Number { get; }

    /// <summary>The record drawn now.</summary>
    public SkillSlotData? Data { get; private set; }

    public SlotInfo? Info => Data?.Info;

    /// <summary>Cooldown seconds shown (null when the skill is ready) and its text.</summary>
    public double? CooldownLeft => Data?.CooldownLeft;
    public string CooldownText => _cooldown.Visible ? _cooldown.Text : "";

    /// <summary>A press would be refused for the resource now (red frame and shade), as the core says.</summary>
    public bool ResourceShort => Data?.ResourceShort ?? false;

    /// <summary>What one use costs now (the core's <see cref="LiveState.CostBySlot"/>), or null for an empty slot.</summary>
    public double? CostNow => Data?.CostNow;
    public bool Queued => Data?.Queued ?? false;
    public bool Held => Data?.Held ?? false;
    public double BlinkLeft { get; private set; }
    public string NameText => _name.Text;

    /// <summary>The colour of the frame drawn now (red only for a short resource).</summary>
    public Color FrameColor { get; private set; }

    /// <summary>Horizontal shake of the slot's face now, pixels.</summary>
    public float Shake => _inner.Position.X;

    public SkillSlotView(int number)
    {
        Number = number;
        CustomMinimumSize = new Vector2(Width, Height);
        MouseFilter = MouseFilterEnum.Stop;

        _inner = new Control { Name = "Face", MouseFilter = MouseFilterEnum.Ignore };
        _inner.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _inner.Draw += () => _inner.DrawRect(new Rect2(Vector2.Zero, _inner.Size), Background);
        AddChild(_inner);
        _icon = new TextureRect
        {
            Name = "SkillIcon", Position = new Vector2((Width - 32) / 2, 0), Size = new Vector2(32, 32),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _inner.AddChild(_icon);

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

    /// <summary>The key caption now («1» or «[X]»).</summary>
    public string KeyText => _key.Text;

    /// <summary>Changes only the key caption (the device switched between two refreshes).</summary>
    public void ShowKey(string text)
    {
        _key.Text = text;
        if (Data != null) Data = Data with { KeyText = text };
    }

    /// <summary>A refused press on this slot: the slot flashes and shakes for <see cref="BlinkSec"/>.</summary>
    public void Blink() => BlinkLeft = BlinkSec;

    /// <summary>Draws <paramref name="data"/>; <paramref name="delta"/> runs the refusal blink.</summary>
    public void Show(SkillSlotData data, double delta)
    {
        Data = data;
        var info = data.Info;
        _icon.Texture = GameIcons.Skill(info?.SkillId);
        BlinkLeft = Math.Max(0, BlinkLeft - delta);
        var fade = (float)(BlinkLeft / BlinkSec);
        _inner.Position = new Vector2(BlinkLeft > 0 ? ShakePixels * fade * Mathf.Sin((float)(BlinkLeft * 70)) : 0, 0);
        if (_key.Text != data.KeyText) _key.Text = data.KeyText;
        _name.Text = info?.Name ?? "пусто";
        if (_empty != (info == null)) // a theme override relayouts: only when it changes
        {
            _empty = info == null;
            _name.AddThemeColorOverride("font_color", info == null ? new Color(0.5f, 0.47f, 0.42f) : new Color(0.93f, 0.9f, 0.84f));
        }
        _cooldown.Visible = data.CooldownLeft != null;
        if (data.CooldownLeft is { } cd) _cooldown.Text = HudFormat.Seconds(cd);
        _name.Modulate = data.CooldownLeft != null || data.ResourceShort ? new Color(0.82f, 0.82f, 0.82f) : Colors.White;
        FrameColor = data.ResourceShort ? ShortColor : data.Held ? HeldColor : data.Queued ? QueuedColor : EdgeColor;

        var costKey = data.CostNow is { } c ? RuNumber.Format(c, "0.#") : "";
        var tipKey = info == null ? "" : $"{info.SkillId}|{info.Cost}|{info.CostPerSec}|{info.CooldownSec}|{info.UseTimeSec}|{data.ResourceName}|{costKey}";
        if (tipKey != _tooltipFor)
        {
            _tooltipFor = tipKey;
            TooltipText = Tooltip(Number, info, data.ResourceName, data.CostNow);
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
        "Скверна" => "скверны",
        "" => "ресурса",
        _ => name.ToLowerInvariant(),
    };

    private void DrawFrame()
    {
        var size = _frame.Size;
        var rect = new Rect2(Vector2.Zero, size);
        if (Info is { } info && CooldownLeft is { } left)
        {
            // A curtain from the top: its height is the cooldown still left.
            var fraction = info.CooldownSec > 0 ? (float)Math.Clamp(left / info.CooldownSec, 0, 1) : 1f;
            _frame.DrawRect(new Rect2(0, 0, size.X, size.Y * fraction), new Color(0, 0, 0, 0.6f));
        }
        if (ResourceShort) _frame.DrawRect(rect, new Color(0.3f, 0, 0, 0.45f));
        if (BlinkLeft > 0)
        {
            var a = (float)(BlinkLeft / BlinkSec);
            _frame.DrawRect(rect, FlashColor with { A = 0.5f * a });
        }
        var width = FrameColor == EdgeColor ? 1f : 3f;
        _frame.DrawRect(rect.Grow(-width / 2), FrameColor, filled: false, width: width);
    }
}
