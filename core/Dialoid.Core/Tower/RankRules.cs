namespace Dialoid.Core.Tower;

/// <summary>Rank rules (spec 6.1–6.3): zone level, XP penalty for overlevelled characters, allowed mods, entry level.</summary>
public static class RankRules
{
    /// <summary>Ranks 1–5: the character level clamped into the rank band. Ranks 6–15: 60 + (rank − 5).</summary>
    public static int ZoneLevel(TowerRules r, int rank, int characterLevel)
    {
        var def = Def(r, rank);
        return def.ZoneLevel > 0 ? def.ZoneLevel : Math.Clamp(characterLevel, def.LevelMin, def.LevelMax);
    }

    /// <summary>
    /// 1 − min(cap, perLevel × levels above the band top). A rank is "below the character" only when its top is strictly
    /// lower than the character level. Ranks 6–15 are played at the cap, so always 1.
    /// </summary>
    public static double XpMultiplier(TowerRules r, int rank, int characterLevel)
    {
        var def = Def(r, rank);
        if (def.ZoneLevel > 0) return 1.0;
        int over = Math.Max(0, characterLevel - def.LevelMax);
        return 1.0 - Math.Min(r.XpPenaltyCap, r.XpPenaltyPerLevel * over);
    }

    /// <summary>The rank allows mods of this kind and this tier (1..MaxTier).</summary>
    public static bool CanApply(TowerRules r, int rank, ModKind kind, int tier)
    {
        var def = Def(r, rank);
        return def.ModKinds.Contains(kind) && tier >= 1 && tier <= def.MaxTier;
    }

    /// <summary>The character may enter the rank: level ≥ the band bottom.</summary>
    public static bool LevelAllows(TowerRules r, int rank, int characterLevel) => characterLevel >= Def(r, rank).LevelMin;

    internal static RankDef Def(TowerRules r, int rank) =>
        rank >= 1 && rank <= r.Ranks.Count
            ? r.Ranks[rank - 1]
            : throw new ArgumentOutOfRangeException(nameof(rank), rank, $"Ранга {rank} нет: ранги от 1 до {r.Ranks.Count}");
}
