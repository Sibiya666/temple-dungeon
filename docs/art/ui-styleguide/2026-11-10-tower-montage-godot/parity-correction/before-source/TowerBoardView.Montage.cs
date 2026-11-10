using System.Linq;
using Dialoid.Game.Ui.Art;
using Godot;

namespace Dialoid.Game.Ui.Tower;

public partial class TowerBoardView
{
    private VBoxContainer _floorList = null!;
    private VBoxContainer _editor = null!;
    private VBoxContainer _sourceDetail = null!;
    private PanelContainer _catalogueLattice = null!;
    private readonly Label _floorTitle = new() { Name = "FloorEditorTitle" };
    private readonly Label _floorIdentity = new() { Name = "FloorInstalledTitle" };
    private readonly TextureRect _floorPortrait = new() { Name = "FloorInstalledPortrait" };
    private readonly Label _floorProperties = new() { Name = "FloorProperties" };
    private readonly Label _sourceRoute = new() { Name = "InstallSourceTarget" };
    private readonly Label _installReason = new() { Name = "InstallReason" };
    private readonly Label _sourceProperties = new() { Name = "SourceProperties" };
    private readonly Label _sourceObjective = new() { Name = "SourceObjective" };
    private readonly Label _routeCount = new() { Name = "RouteCount" };

    private void BuildMontage()
    {
        Theme = CreateTheme();
        TopLevel = true;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var background = new ColorRect { Color = new Color("0c1114"), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var architecture = new TextureRect { Texture = GD.Load<Texture2D>("res://Ui/Art/TowerPanels/ascent-v2.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            Modulate = new Color(.55f, .65f, .7f, .22f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(architecture);
        architecture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margin = new MarginContainer();
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 24);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 20);
        root.AddChild(header);
        header.AddChild(RoomHudTheme.Heading("DIALOID", 22));
        var title = Text(header, "Башня / Сборка маршрута");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.TooltipText = $"Тестовый набор: по 5 активностей каждого типа и ранга, по 20 расходников. Уровень {TestCharacterLevel}. Набор не сохраняется.";
        title.MouseFilter = MouseFilterEnum.Pass;
        _rank.Name = "ActivityRank";
        _rank.CustomMinimumSize = new Vector2(124, 36);
        header.AddChild(_rank);
        _rank.ItemSelected += index => { if (!_refreshing) SwitchRank(_rank.GetItemId((int)index)); };
        _close.Text = "Закрыть";
        _close.TooltipText = "Esc / F6 / B — закрыть";
        _close.CustomMinimumSize = new Vector2(100, 36);
        _close.Pressed += () => CloseRequested?.Invoke();
        header.AddChild(_close);
        root.AddChild(new HSeparator());
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 24);
        root.AddChild(columns);
        var route = new VBoxContainer { Name = "RouteColumn", CustomMinimumSize = new Vector2(184, 0) };
        route.AddThemeConstantOverride("separation", 8);
        columns.AddChild(route);
        var routeHeader = new HBoxContainer();
        route.AddChild(routeHeader);
        Text(routeHeader, "МАРШРУТ").SizeFlagsHorizontal = SizeFlags.ExpandFill;
        routeHeader.AddChild(_routeCount);
        _floorList = new VBoxContainer { Name = "FloorList", SizeFlagsVertical = SizeFlags.ExpandFill };
        _floorList.AddThemeConstantOverride("separation", 7);
        route.AddChild(_floorList);
        _wholeTower = AddButton(route, "Вся башня · цель расходника", () => SelectFloor(null));
        _wholeTower.Name = "WholeTowerTarget";
        TowerBoardArt.Decorate(_wholeTower, TowerBoardArt.Symbol("tower"), 24);

        var work = new VBoxContainer { Name = "MontageWork", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        work.AddThemeConstantOverride("separation", 10);
        columns.AddChild(work);
        var editorPanel = new PanelContainer { Name = "FloorEditor", SizeFlagsVertical = SizeFlags.ExpandFill };
        editorPanel.AddThemeStyleboxOverride("panel", RoomHudTheme.ReferenceSurface(12));
        work.AddChild(editorPanel);
        _editor = new VBoxContainer();
        _editor.AddThemeConstantOverride("separation", 4);
        editorPanel.AddChild(_editor);
        _floorTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _floorTitle.AddThemeFontSizeOverride("font_size", 24);
        _editor.AddChild(_floorTitle);
        var installed = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        installed.AddThemeConstantOverride("separation", 16);
        _editor.AddChild(installed);
        _floorPortrait.CustomMinimumSize = new Vector2(48, 48);
        _floorPortrait.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _floorPortrait.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        _floorPortrait.MouseFilter = MouseFilterEnum.Ignore;
        installed.AddChild(_floorPortrait);
        _floorIdentity.AddThemeFontSizeOverride("font_size", 18);
        installed.AddChild(_floorIdentity);
        var biomeRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        installed.AddChild(biomeRow);
        Text(biomeRow, "Биом").AutowrapMode = TextServer.AutowrapMode.Off;
        _biome.Name = "FloorBiome";
        _biome.CustomMinimumSize = new Vector2(150, 30);
        _biome.AddThemeFontSizeOverride("font_size", 12);
        biomeRow.AddChild(_biome);
        _biome.ItemSelected += index =>
        {
            if (!_refreshing && !_targetInventory && Assembly != null && Data != null && SelectedFloor is int slot)
            { Assembly.SetBiome(slot, Data.Biomes[(int)index].Id); Refresh(); }
        };
        var properties = new ScrollContainer { Name = "FloorPropertiesScroll", CustomMinimumSize = new Vector2(0, 24),
            SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _editor.AddChild(properties);
        properties.AddChild(_floorProperties);
        _floorProperties.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _floorProperties.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _floorProperties.AddThemeFontSizeOverride("font_size", 12);
        _sourceRoute.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _sourceRoute.AddThemeFontSizeOverride("font_size", 12);
        _editor.AddChild(_sourceRoute);
        var actionRow = new HBoxContainer();
        actionRow.AddThemeConstantOverride("separation", 8);
        _editor.AddChild(actionRow);
        _install = AddButton(actionRow, "Для установки выберите активность", () => InstallSelectedActivity());
        _install.Name = "InstallActivity";
        _install.CustomMinimumSize = new Vector2(0, 38);
        _install.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _install.SizeFlagsStretchRatio = 2;
        _install.AddThemeFontSizeOverride("font_size", 14);
        _install.AddThemeStyleboxOverride("normal", RoomHudTheme.ReferenceSurface(8, true));
        _installReason.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _installReason.AddThemeFontSizeOverride("font_size", 12);
        _editor.AddChild(_installReason);
        _removeFloor = AddButton(actionRow, "Снять активность", () => RemoveSelectedActivity());
        _removeFloor.Name = "RemoveActivity";
        _removeFloor.CustomMinimumSize = new Vector2(0, 30);
        _removeFloor.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _removeFloor.AddThemeFontSizeOverride("font_size", 12);
        var statusScroll = new ScrollContainer { Name = "ActionResult", CustomMinimumSize = new Vector2(0, 29),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _editor.AddChild(statusScroll);
        statusScroll.AddChild(_status);
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.AddThemeFontSizeOverride("font_size", 12);
        _status.Visible = false;

        var catalogueHeader = new HBoxContainer();
        work.AddChild(catalogueHeader);
        Text(catalogueHeader, "КАТАЛОГ АКТИВНОСТЕЙ").SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _activityPage.Name = "ActivityPage";
        _activityPage.CustomMinimumSize = new Vector2(80, 28);
        catalogueHeader.AddChild(_activityPage);
        _activityPage.ItemSelected += _ => RefreshInventory();
        var lattice = _catalogueLattice = new PanelContainer { Name = "CatalogueLattice", CustomMinimumSize = new Vector2(0, 112) };
        lattice.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        work.AddChild(lattice);
        _glassWindow.Setup(_activityGrid);
        lattice.AddChild(_glassWindow);
        lattice.AddChild(_activityGrid);
        _activityGrid.AddThemeConstantOverride("h_separation", 6);
        _activityGrid.AddThemeConstantOverride("v_separation", 8);
        _activityGrid.SortChildren += ArrangeActivitySlots;
        var commands = new HBoxContainer();
        commands.AddThemeConstantOverride("separation", 8);
        work.AddChild(commands);
        _shuffle.Name = "ShuffleInventory";
        _shuffle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _shuffle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _shuffle.AddThemeFontSizeOverride("font_size", 11);
        _shuffle.Pressed += () => ShuffleInventory();
        _shuffle.TooltipText = "Заполнить только пустые этажи экземплярами текущего ранга. Занятые этажи и их свойства сохраняются; при отказе ничего не тратится.";
        commands.AddChild(_shuffle);
        var check = AddButton(commands, "Проверить вход", () => Report(ValidateForTest(), "Проверка пройдена."));
        check.Name = "ValidateEntry";
        check.CustomMinimumSize = new Vector2(126, 38);
        check.AddThemeFontSizeOverride("font_size", 12);
        var summit = Text(commands, "Вершина · Убер-босс\nЗабег пока недоступен");
        summit.Name = "TowerSummit";
        summit.AddThemeFontSizeOverride("font_size", 11);
        summit.CustomMinimumSize = new Vector2(130, 0);

        var sidebar = new VBoxContainer { Name = "SourceColumn", CustomMinimumSize = new Vector2(270, 0) };
        columns.AddChild(new VSeparator());
        columns.AddChild(sidebar);
        var rightScroll = new ScrollContainer { Name = "CurrencyTargetScroll", SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        sidebar.AddChild(rightScroll);
        _sourceDetail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _sourceDetail.AddThemeConstantOverride("separation", 9);
        rightScroll.AddChild(_sourceDetail);
        Text(_sourceDetail, "ВЫБРАНО В КАТАЛОГЕ");
        BuildReferenceStyle();
        _sourceDetail.AddChild(_sourceObjective);
        _sourceDetail.AddChild(_sourceProperties);
        _sourceDetail.AddChild(new HSeparator());
        Text(_sourceDetail, "ЦЕЛЬ НАМАЗКИ");
        _sourceDetail.AddChild(_details);
        Text(_sourceDetail, "Свойство для адресного снятия");
        _sourceDetail.AddChild(_mod);
        _mod.Name = "TargetModifier";
        _mod.ItemSelected += _ => RefreshCurrencyDescription();
        Text(_sourceDetail, "РАСХОДНИКИ");
        _sourceDetail.AddChild(_currencyGrid);
        _currencyInfo.Name = "CurrencyDescription";
        sidebar.AddChild(_currencyInfo);
        foreach (var label in new[] { _details, _currencyInfo, _sourceObjective, _sourceProperties, _bonus })
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.AddThemeFontSizeOverride("font_size", 12);
            label.AddThemeColorOverride("font_color", RoomHudTheme.MutedText);
        }
        foreach (var option in new[] { _rank, _mod, _biome }) { option.FitToLongestItem = false; option.ClipText = true; }
        _bonus.AddThemeFontSizeOverride("font_size", 11);
        root.AddChild(_bonus);
    }
}
