using Godot;

namespace Dialoid.Game.Ui.Hud;

/// <summary>
/// The target plate (HUD spec §4 item 8): the name (an elite's colour always with its icon: a diamond for a champion, a star for
/// a rare), the life bar in health's dark red with «1 234 / 5 000» outlined in dark, and the target's ailments and marks with
/// stacks and timers (an <see cref="EffectRowView"/> of kind <see cref="EffectRowKind.Target"/>). No target — no plate. Which
/// target it shows and where the plate stands is the placer's business.
/// </summary>
public partial class TargetPlateView : PanelContainer
{
    public const float MinWidth = 240;
    public const float LifeHeight = 16;

    private readonly Label _name;
    private readonly Control _icon;
    private readonly Control _life;
    private readonly Label _lifeText;

    public EffectRowView Effects { get; }

    /// <summary>The record drawn now, or null (hidden).</summary>
    public TargetPlateData? Data { get; private set; }

    public string NameText => _name.Text;
    public string LifeText => _lifeText.Text;
    public Color NameColor { get; private set; } = HudColors.NormalName;

    /// <summary>The rank icon is drawn (champions and rares).</summary>
    public bool IconShown => _icon.Visible;

    public TargetPlateView(int maxEffects = 8)
    {
        Name = "TargetPlate";
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        var style = new StyleBoxFlat { BgColor = new Color(0.06f, 0.055f, 0.05f, 0.9f), BorderColor = new Color(0.55f, 0.27f, 0.22f), ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 5 };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(3);
        AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 3);
        AddChild(box);

        var head = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        head.AddThemeConstantOverride("separation", 5);
        box.AddChild(head);
        _icon = new Control { Name = "RankIcon", CustomMinimumSize = new Vector2(12, 12), MouseFilter = MouseFilterEnum.Ignore, Visible = false, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _icon.Draw += DrawIcon;
        head.AddChild(_icon);
        _name = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _name.AddThemeColorOverride("font_color", HudColors.NormalName);
        _name.AddThemeFontSizeOverride("font_size", 15);
        head.AddChild(_name);

        _life = new Control { Name = "Life", CustomMinimumSize = new Vector2(0, LifeHeight), MouseFilter = MouseFilterEnum.Ignore };
        _life.Draw += DrawLife;
        box.AddChild(_life);
        _lifeText = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        _lifeText.AddThemeFontSizeOverride("font_size", 13);
        _lifeText.AddThemeColorOverride("font_color", new Color(0.97f, 0.94f, 0.9f));
        _lifeText.AddThemeColorOverride("font_outline_color", HudColors.NumberOutline);
        _lifeText.AddThemeConstantOverride("outline_size", 4);
        _lifeText.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        (_lifeText.OffsetTop, _lifeText.OffsetBottom) = (-3, 3); // the 13 px text with its outline sits centred on the 16 px bar
        _life.AddChild(_lifeText);

        Effects = new EffectRowView(EffectRowKind.Target, maxEffects) { Name = "Effects" };
        box.AddChild(Effects);
    }

    /// <summary>The name's colour by rank (always shown with the rank icon for an elite).</summary>
    public static Color ColorOf(TargetRank rank) => rank switch
    {
        TargetRank.Champion => HudColors.ChampionName,
        TargetRank.Rare => HudColors.RareName,
        _ => HudColors.NormalName,
    };

    public void Show(TargetPlateData? data)
    {
        Data = data;
        if (data == null)
        {
            if (Visible) Visible = false;
            return;
        }
        if (!Visible) Visible = true;
        if (_name.Text != data.Name) _name.Text = data.Name;
        var color = ColorOf(data.Rank);
        if (color != NameColor)
        {
            NameColor = color;
            _name.AddThemeColorOverride("font_color", color);
        }
        var elite = data.Rank != TargetRank.Normal;
        if (_icon.Visible != elite) _icon.Visible = elite;
        _icon.QueueRedraw();
        if (_lifeText.Text != data.LifeText) _lifeText.Text = data.LifeText;
        _life.QueueRedraw();
        Effects.Show(data.Effects);
    }

    /// <summary>The share of the life bar that is filled (1 for an immortal target).</summary>
    public static float LifeFraction(TargetPlateData data) =>
        double.IsInfinity(data.LifeMax) || data.LifeMax <= 0 ? 1f : (float)Math.Clamp(data.Life / data.LifeMax, 0, 1);

    private void DrawLife()
    {
        var size = _life.Size;
        var rect = new Rect2(Vector2.Zero, size);
        _life.DrawRect(rect, new Color(0.04f, 0.03f, 0.03f, 0.95f));
        if (Data is { } data)
        {
            var fill = new Rect2(1, 1, (size.X - 2) * LifeFraction(data), size.Y - 2);
            _life.DrawRect(fill, HudColors.Life);
            if (fill.Size.X > 2) _life.DrawLine(fill.Position, fill.Position + new Vector2(fill.Size.X, 0), HudColors.Life.Lightened(0.3f), 1);
        }
        _life.DrawRect(rect.Grow(-0.5f), new Color(0.3f, 0.12f, 0.1f), filled: false, width: 1);
    }

    private void DrawIcon()
    {
        if (Data is not { Rank: not TargetRank.Normal } data) return;
        var c = _icon.Size / 2;
        var r = Mathf.Min(c.X, c.Y);
        var color = ColorOf(data.Rank);
        Vector2[] points;
        if (data.Rank == TargetRank.Champion)
            points = [c + new Vector2(0, -r), c + new Vector2(r, 0), c + new Vector2(0, r), c + new Vector2(-r, 0)];
        else
            points = Enumerable.Range(0, 10).Select(i =>
            {
                var a = -Mathf.Pi / 2 + i * Mathf.Pi / 5;
                var len = i % 2 == 0 ? r : r * 0.45f;
                return c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * len;
            }).ToArray();
        _icon.DrawColoredPolygon(points, color);
        _icon.DrawPolyline([.. points, points[0]], new Color(0, 0, 0, 0.8f), 1);
    }
}
