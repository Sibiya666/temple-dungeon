using Dialoid.Core.Simulation;
using Dialoid.Core.Explain;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game.Ui.Hud;

// The combat HUD's components (HUD spec §4 items 3, 6–8, §7 «Сейчас»): each one is a data record built from the fight's state
// and a view that only draws it. A component knows neither the room nor the layout; CombatHud (and later the game's layouts A
// and B) builds the records and places the views.

/// <summary>The HUD's colours (HUD spec §9). None of them is the telegraph colour (saturated crimson with a white rim).</summary>
public static class HudColors
{
    /// <summary>Health: dark red.</summary>
    public static readonly Color Life = new(0.56f, 0.08f, 0.07f);

    /// <summary>Mana: blue.</summary>
    public static readonly Color Mana = new(0.18f, 0.36f, 0.85f);

    /// <summary>Fury: amber-ochre (not red like health, not orange like fire).</summary>
    public static readonly Color Fury = new(0.78f, 0.57f, 0.16f);

    /// <summary>Corruption («Скверна»): violet with a dark core.</summary>
    public static readonly Color Corruption = new(0.5f, 0.24f, 0.66f);
    public static readonly Color CorruptionCore = new(0.17f, 0.05f, 0.24f);

    /// <summary>The buff row's neutral iron frame (legacy API name).</summary>
    public static readonly Color Bronze = new("55666d");

    /// <summary>The debuff row's torn frame: dark red.</summary>
    public static readonly Color DebuffFrame = new(0.6f, 0.1f, 0.08f);

    /// <summary>Dark outline of every number on a bar or plate (HUD spec §4 item 17).</summary>
    public static readonly Color NumberOutline = new(0, 0, 0, 0.95f);

    /// <summary>Elite names: champion blue, rare gold (always with their icon).</summary>
    public static readonly Color ChampionName = new(0.45f, 0.6f, 1f);
    public static readonly Color RareName = new(0.98f, 0.82f, 0.32f);
    public static readonly Color NormalName = new(0.95f, 0.82f, 0.72f);

    /// <summary>The fill of a resource by its id (fury, mana, corruption, life).</summary>
    public static Color Resource(string id) => id switch
    {
        "fury" => Fury,
        "corruption" => Corruption,
        "life" => Life,
        _ => Mana,
    };

    /// <summary>Hue in degrees (0–360), for the colour checks.</summary>
    public static float HueDegrees(Color c) => c.H * 360f;
}

/// <summary>Number formats shared by the components.</summary>
public static class HudFormat
{
    /// <summary>Seconds with one decimal under 10, whole above: «2,1», «14».</summary>
    public static string Seconds(double sec) => sec < 9.95 ? RuNumber.Format(sec, "0.0") : RuNumber.Format(Math.Round(sec, MidpointRounding.AwayFromZero), "0");

    /// <summary>«45 / 100»: whole numbers, the value rounded down so a short cost never reads as enough.</summary>
    public static string Pair(double value, double max) =>
        $"{RuNumber.Grouped(Math.Floor(value))} / {RuNumber.Grouped(Math.Round(max, MidpointRounding.AwayFromZero))}";
}

/// <summary>
/// One skill slot (HUD spec §4 item 3): the skill, the key of the active device, cooldown, cost now, the core's resource-short
/// verdict, the queued press and the held channel. Every figure is the core's (<see cref="LiveState"/>).
/// </summary>
public sealed record SkillSlotData(int Number, SlotInfo? Info, string KeyText, string ResourceName, double? CooldownLeft, double? CostNow,
    bool ResourceShort, bool Queued, bool Held)
{
    public static SkillSlotData From(int number, SlotInfo? info, LiveState? state, string resourceName, string keyText) => new(
        number, info, keyText, resourceName,
        info != null && state != null && state.CooldownLeftBySlot.TryGetValue(number, out var left) && left > 0 ? left : null,
        info != null && state != null && state.CostBySlot.TryGetValue(number, out var cost) ? cost : null,
        info != null && state != null && state.ResourceShortSlots.Contains(number),
        state?.QueuedSlot == number,
        state?.HeldSlot == number);
}

/// <summary>A resource bar (HUD spec §4 item 2): «Ярость 45 / 100» and the resource's colour (§9).</summary>
public sealed record ResourceBarData(string Name, string ResourceId, double Value, double Max)
{
    public string Text => ResourceBarView.TextOf(Name, Value, Max);
    public Color Fill => HudColors.Resource(ResourceId);

    /// <summary>The class resource of the fight, or null before the fight has a state.</summary>
    public static ResourceBarData? From(LiveState? state, string name) =>
        state == null ? null : new ResourceBarData(name, state.ResourceId, state.Resource, state.ResourceMax);
}

/// <summary>Which row an effect sits in: the hero's buffs (bronze frame), the hero's ailments and debuffs (torn dark-red frame),
/// or a target's ailments and marks on its plate.</summary>
public enum EffectRowKind { Buffs, Debuffs, Target }

/// <summary>One icon of an effect row: its text («Поджог ×3 · 2,4 с»), its accent (damage type or kind) and the core's effect.</summary>
public sealed record EffectChipData(string Id, string Text, Color Accent, TimedEffect? Effect = null)
{
    /// <summary>The chip of a core effect, named by <paramref name="name"/> (see <see cref="EffectNames"/>).</summary>
    public static EffectChipData From(TimedEffect e, string name) => new(e.Id, EffectChip.TextOf(e, name), EffectChip.ColorOf(e), e);

    public static IReadOnlyList<EffectChipData> From(IEnumerable<TimedEffect> effects, EffectNames names) =>
        effects.Select(e => From(e, names.Of(e))).ToList();
}

/// <summary>Elite rank of a target: the name's colour, always with its icon (HUD spec §4 item 8).</summary>
public enum TargetRank { Normal, Champion, Rare }

/// <summary>
/// The target plate (HUD spec §4 item 8): whose (<see cref="Target"/>, the core's target index), the name and rank, the life bar
/// (the core's life of that target; the room's immortal dummy has a finite stand-in life that drops and refills), its ailments and
/// marks with stacks and timers.
/// </summary>
public sealed record TargetPlateData(int Target, string Name, TargetRank Rank, double Life, double LifeMax, IReadOnlyList<EffectChipData> Effects)
{
    /// <summary>«1 234 / 5 000»; an immortal target («∞») shows only its name of state.</summary>
    public string LifeText => double.IsInfinity(LifeMax) ? "бессмертен" : HudFormat.Pair(Life, LifeMax);

    /// <summary>The plate of <paramref name="target"/> from the fight's state, or null when the state has no such target.</summary>
    public static TargetPlateData? From(LiveState? state, int target, string name, EffectNames names, TargetRank rank = TargetRank.Normal)
    {
        if (state == null || target < 0) return null;
        var effects = target < state.TargetEffects.Count ? state.TargetEffects[target] : Array.Empty<TimedEffect>();
        var life = target < state.TargetLife.Count ? state.TargetLife[target] : double.PositiveInfinity;
        var max = target < state.TargetLifeMax.Count ? state.TargetLifeMax[target] : double.PositiveInfinity;
        return new TargetPlateData(target, name, rank, life, max, EffectChipData.From(effects, names));
    }
}
