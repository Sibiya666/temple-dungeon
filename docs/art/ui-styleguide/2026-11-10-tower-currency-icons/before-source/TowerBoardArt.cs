using System;
using System.Collections.Generic;
using Godot;
using Dialoid.Game.Ui.Art;

namespace Dialoid.Game.Ui.Tower;

/// <summary>Original service glyphs and independent nine-slice iron frames for the tower board.</summary>
public static class TowerBoardArt
{
    private const string Root = "res://Ui/Tower/Art/";
    private static readonly Dictionary<string, Texture2D> Cache = new(StringComparer.Ordinal);

    public static Texture2D? Activity(string? id) => id switch
    {
        null or "empty_floor" => Symbol("empty_floor"),
        "timed_run" or "ritual" or "escort" or "hold" or "levers" or "clear" or "waves" or "bosses" => Load("activities/" + id + ".png"),
        _ => null,
    };

    public static Texture2D? Currency(string id) => id switch
    {
        "imbue" or "imbue_type" or "imbue_tower" or "clear" or "shuffle" or "reroll" or
        "reroll_variant" or "upgrade" or "remove" or "biased_rare_elite" or "biased_rare_reward" or
        "biased_epic_elite" or "biased_epic_reward" or "bias_value" => Load("currencies/" + id + ".svg"),
        _ => null,
    };

    public static Texture2D? Symbol(string id) => id switch
    {
        "empty_floor" or "summit" or "tower" or "confirm" or "close" => Load("symbols/" + id + ".svg"),
        _ => null,
    };

    /// <summary>Retains the caller's text, focus and input. Null art leaves a text-only button.</summary>
    public static void Decorate(Button button, Texture2D? icon, int size = 28)
    {
        button.Icon = icon;
        button.ExpandIcon = true;
        button.AddThemeConstantOverride("icon_max_width", Math.Max(1, size));
        button.AddThemeConstantOverride("h_separation", 8);
    }

    public static StyleBox Panel() => new StyleBoxEmpty();
    public static StyleBox Card(bool selected = false) => RoomHudTheme.ReferenceSurface(10, selected);
    public static StyleBox Focus() => RoomHudTheme.ReferenceFocus();

    private static StyleBoxTexture Frame(string id, int margin) => new()
    {
        Texture = Load("frames/" + id + ".svg"),
        TextureMarginLeft = 16, TextureMarginTop = 16,
        TextureMarginRight = 16, TextureMarginBottom = 16,
        ContentMarginLeft = margin, ContentMarginTop = margin,
        ContentMarginRight = margin, ContentMarginBottom = margin,
        DrawCenter = id != "focus",
    };

    private static Texture2D? Load(string relative)
    {
        if (Cache.TryGetValue(relative, out var cached)) return cached;
        var path = Root + relative;
        if (!ResourceLoader.Exists(path)) return null;
        var texture = GD.Load<Texture2D>(path);
        if (texture != null) Cache[relative] = texture;
        return texture;
    }
}
