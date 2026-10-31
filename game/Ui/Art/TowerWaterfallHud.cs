using Dialoid.Game.Ui;
using Dialoid.Game.Ui.Hud;
using Godot;

namespace Dialoid.Game.Ui.Art;

/// <summary>Two live readings flanking the unchanged skill bar. No simulated animation values enter game state.</summary>
public partial class TowerWaterfallHud : Control
{
    public const string EnableArg = "--tower-hud";
    public const float Gap = 12;
    public const float Bottom = 12;
    public const float ReservedHeight = WaterfallView.Height + Bottom;
    private Main _main = null!;
    public WaterfallView Health { get; private set; } = null!;
    public WaterfallView Resource { get; private set; } = null!;

    public static float LeftEdge(float width) => CombatHud.LeftEdge(width) - Gap - WaterfallView.Width;

    public static TowerWaterfallHud Install(Main main)
    {
        if (main.Hud!.GetNodeOrNull<TowerWaterfallHud>("TowerWaterfalls") is { } existing) return existing;
        RoomHudSkin.Apply(main);
        var hud = new TowerWaterfallHud { _main = main, Name = "TowerWaterfalls", MouseFilter = MouseFilterEnum.Ignore };
        main.Hud.AddChild(hud);
        hud.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        hud.Health = new WaterfallView { Name = "HealthWaterfall" };
        hud.Resource = new WaterfallView { Name = "ResourceWaterfall" };
        hud.AddChild(hud.Health);
        hud.AddChild(hud.Resource);
        // Keep the old data view alive for compatibility, but remove its duplicate horizontal drawing.
        main.Hud.Resource.Modulate = new Color(1, 1, 1, 0);
        main.Windows.BottomInset = () => Mathf.Max(CombatHud.ReservedHeight, ReservedHeight);
        main.Windows.LeftBottomRightLimit = screen => LeftEdge(screen.X) - CombatHud.WindowGap;
        hud.Refresh();
        return hud;
    }

    public override void _Process(double delta) => Refresh();

    public void Refresh()
    {
        Health.Position = new Vector2(LeftEdge(Size.X), Size.Y - ReservedHeight);
        Resource.Position = new Vector2(CombatHud.LeftEdge(Size.X) + CombatHud.BarWidth + Gap, Size.Y - ReservedHeight);
        var state = _main.Training!.State;
        // Apostate can deliberately stay low: no generic low-life warning for corruption; zero remains explicit.
        var healthAlert = state is { LifeMax: > 0 } && (state.Life <= 0 || (state.ResourceId != "corruption" && state.Life / state.LifeMax < .3));
        Health.Show(new WaterfallReading("Здоровье", state?.Life ?? double.NaN, state?.LifeMax ?? 0, new Color("73352f"), healthAlert));
        Resource.Show(new WaterfallReading(string.IsNullOrEmpty(_main.Training.ResourceName) ? "Ресурс" : _main.Training.ResourceName,
            state?.Resource ?? double.NaN, state?.ResourceMax ?? 0, ResourceColor(state?.ResourceId ?? "mana"), state?.ResourceShortSlots.Count > 0));
    }

    public static Color ResourceColor(string id) => id switch
    {
        "mana" => new Color("7099a8"),
        "fury" => new Color("9b8654"),
        "corruption" => new Color("8c789f"),
        "life" => new Color("73352f"),
        _ => HudColors.Resource(id),
    };
}
