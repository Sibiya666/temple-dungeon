using Dialoid.Core.Passives;
using Godot;

namespace Dialoid.Game.Ui.Art;

/// <summary>Approved, individual Berserker portraits; other existing ascendancies retain their documented legacy art.</summary>
public static class AscendancyIcons
{
    public const string Root = "res://Ui/Art/Icons/ascendancy/berserker/";
    public static readonly IReadOnlySet<string> ApprovedIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "berserk_frenzy", "insatiable", "blood_feast", "reckless", "whirl_rage", "flesh_rend", "battle_trance",
    };

    public static Texture2D? For(AscendancyNode node) => ApprovedIds.Contains(node.Id)
        ? GD.Load<Texture2D>(Root + node.Id + ".png")
        : GameIcons.Perk(node.Id, node.Effects);
}
