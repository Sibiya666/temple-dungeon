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
    private Control _floorSocket = null!;
    private readonly Control _portraitGap = new() { CustomMinimumSize = new Vector2(0, 14) };
    private readonly Control _metaGap = new() { CustomMinimumSize = new Vector2(0, 8) };
    private readonly Control _identityGap = new() { CustomMinimumSize = new Vector2(0, 10) };
    private readonly Label _floorTitle = new() { Name = "FloorEditorTitle" };
    private readonly Label _floorIdentity = new() { Name = "FloorInstalledTitle" };
    private readonly TextureRect _floorPortrait = new() { Name = "FloorInstalledPortrait" };
    private readonly Label _floorProperties = new() { Name = "FloorProperties" };
    private readonly Label _sourceRoute = new() { Name = "InstallSourceTarget" };
    private readonly Label _installReason = new() { Name = "InstallReason" };
    private readonly Label _sourceProperties = new() { Name = "SourceProperties" };
    private readonly Label _sourceObjective = new() { Name = "SourceObjective" };
    private readonly Label _routeCount = new() { Name = "RouteCount" };
    private readonly Label _floorSlots = new() { Name = "FloorPropertySlots" };
    private readonly Label _floorMeta = new() { Name = "FloorMeta" };
    private readonly Label _currencyTarget = new() { Name = "CurrencyTargetSummary" };
    private readonly Label _fullStatus = new() { Name = "FullActionResult" };
    private readonly SystemFont _montageSerif = new() { FontNames = new[] { "Georgia", "Times New Roman" } };

    private static StyleBoxFlat MontageSurface(float opacity, bool selected = false, int padding = 0)
    {
        var box = RoomHudTheme.ReferenceSurface(padding, selected);
        box.BgColor = new Color(.047f, .067f, .078f, opacity);
        return box;
    }

    private partial class MontageSocket : Control
    {
        public override void _Draw()
        {
            float w = Size.X, h = Size.Y, cut = Mathf.Min(w, h) * .18f;
            Vector2[] points = [new(cut, 0), new(w-cut, 0), new(w, cut), new(w, h-cut),
                new(w-cut, h), new(cut, h), new(0, h-cut), new(0, cut), new(cut, 0)];
            DrawColoredPolygon(points.Take(8).ToArray(), new Color(.05f, .09f, .11f, .78f));
            DrawPolyline(points, new Color("354b55"), 1, true);
        }
        public override void _Notification(int what) { if (what == NotificationResized) QueueRedraw(); }
    }

    private static Control PortraitSocket(TextureRect portrait, int size)
    {
        var socket = new MontageSocket { CustomMinimumSize = Vector2.One * size, MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        socket.AddChild(portrait);
        portrait.MouseFilter = MouseFilterEnum.Ignore;
        portrait.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        portrait.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        portrait.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        portrait.AnchorLeft = portrait.AnchorTop = .12f;
        portrait.AnchorRight = portrait.AnchorBottom = .88f;
        return socket;
    }

    private void BuildMontage()
    {
        Theme = CreateTheme();
        TopLevel = true;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var background = new ColorRect { Color = new Color("0c1114"), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var image = GD.Load<Texture2D>("res://Ui/Tower/Art/activity-window.png");
        var atlas = new AtlasTexture { Atlas = image };
        var architecture = new TextureRect { Name = "ArchitectureBackdrop", Texture = atlas,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore };
        AddChild(architecture);
        architecture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        architecture.Resized += () =>
        {
            var original = image.GetSize();
            float scale = Mathf.Max(architecture.Size.X / original.X, architecture.Size.Y / original.Y);
            if (scale <= 0) return;
            var crop = architecture.Size / scale;
            atlas.Region = new Rect2(new Vector2((original.X - crop.X) / 2, (original.Y - crop.Y) * .38f), crop);
        };
        var shade = new TextureRect { MouseFilter = MouseFilterEnum.Ignore, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Texture = new GradientTexture2D { Width = 256, Height = 1, FillFrom = Vector2.Zero, FillTo = Vector2.Right,
                Gradient = new Gradient { Offsets = new[] { 0f, .55f, 1f }, Colors = new[] {
                    new Color(.047f,.067f,.078f,.96f), new Color(.047f,.067f,.078f,.74f), new Color(.047f,.067f,.078f,.93f) } } } };
        AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margin = new MarginContainer();
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 24);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 14);
        margin.AddChild(root);
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 24);
        root.AddChild(header);
        var brand = Text(header, "DIALOID");
        brand.AddThemeFontOverride("font", _montageSerif);
        brand.AddThemeFontSizeOverride("font_size", 22);
        brand.AutowrapMode = TextServer.AutowrapMode.Off;
        var title = Text(header, "Башня / Сборка маршрута");
        title.AutowrapMode = TextServer.AutowrapMode.Off;
        title.AddThemeFontSizeOverride("font_size", 14);
        title.TooltipText = $"Тестовый набор: по 5 активностей каждого типа и ранга, по 20 расходников. Уровень {TestCharacterLevel}. Набор не сохраняется.";
        title.MouseFilter = MouseFilterEnum.Pass;
        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _rank.Name = "ActivityRank";
        _rank.CustomMinimumSize = new Vector2(96, 28);
        _rank.AddThemeFontSizeOverride("font_size", 13);
        _rank.AddThemeStyleboxOverride("normal", MontageSurface(.5f, padding: 4));
        header.AddChild(_rank);
        _rank.ItemSelected += index => { if (!_refreshing) SwitchRank(_rank.GetItemId((int)index)); };
        _close.Text = "×";
        _close.TooltipText = "Esc / F6 / B — закрыть";
        _close.AccessibilityName = "Закрыть Башню";
        _close.CustomMinimumSize = new Vector2(28, 28);
        _close.AddThemeFontSizeOverride("font_size", 13);
        _close.AddThemeStyleboxOverride("normal", MontageSurface(.5f, padding: 4));
        _close.Pressed += () => CloseRequested?.Invoke();
        header.AddChild(_close);
        root.AddChild(new HSeparator());
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 24);
        root.AddChild(columns);
        var route = new VBoxContainer { Name = "RouteColumn", CustomMinimumSize = new Vector2(176, 0) };
        route.AddThemeConstantOverride("separation", 8);
        columns.AddChild(route);
        var routeHeader = new HBoxContainer { CustomMinimumSize = new Vector2(0, 26) };
        route.AddChild(routeHeader);
        var routeHeading = Text(routeHeader, "МАРШРУТ");
        routeHeading.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        routeHeading.AddThemeFontSizeOverride("font_size", 12);
        _routeCount.AddThemeFontSizeOverride("font_size", 11);
        routeHeader.AddChild(_routeCount);
        _floorList = new VBoxContainer { Name = "FloorList", SizeFlagsVertical = SizeFlags.ExpandFill };
        _floorList.AddThemeConstantOverride("separation", 8);
        route.AddChild(_floorList);
        _wholeTower = AddButton(route, "Вся башня · цель", () => SelectFloor(null));
        _wholeTower.Name = "WholeTowerTarget";
        _wholeTower.AddThemeFontSizeOverride("font_size", 11);
        _wholeTower.CustomMinimumSize = new Vector2(0, 36);

        var work = new VBoxContainer { Name = "MontageWork", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        work.AddThemeConstantOverride("separation", 10);
        columns.AddChild(work);
        var editorPanel = new PanelContainer { Name = "FloorEditor", SizeFlagsVertical = SizeFlags.ExpandFill };
        var editorFace = MontageSurface(.32f, padding: 8);
        editorFace.BorderWidthLeft = editorFace.BorderWidthRight = 0;
        editorPanel.AddThemeStyleboxOverride("panel", editorFace);
        work.AddChild(editorPanel);
        _editor = new VBoxContainer();
        _editor.AddThemeConstantOverride("separation", 2);
        editorPanel.AddChild(_editor);
        var eyebrow = Text(_editor, "ВЫБРАННЫЙ ЭТАЖ");
        eyebrow.HorizontalAlignment = HorizontalAlignment.Center;
        eyebrow.AddThemeFontSizeOverride("font_size", 10);
        _floorTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _floorTitle.AddThemeFontOverride("font", _montageSerif);
        _floorTitle.AddThemeFontSizeOverride("font_size", 32);
        _editor.AddChild(_floorTitle);
        _editor.AddChild(_portraitGap);
        _floorSocket = PortraitSocket(_floorPortrait, 86);
        _editor.AddChild(_floorSocket);
        _editor.AddChild(_identityGap);
        _floorIdentity.HorizontalAlignment = HorizontalAlignment.Center;
        _floorIdentity.AddThemeFontSizeOverride("font_size", 17);
        _editor.AddChild(_floorIdentity);
        _editor.AddChild(_metaGap);
        var biomeRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        biomeRow.AddThemeConstantOverride("separation", 12);
        _editor.AddChild(biomeRow);
        _biome.Name = "FloorBiome";
        _biome.CustomMinimumSize = new Vector2(120, 24);
        _biome.AddThemeFontSizeOverride("font_size", 11);
        biomeRow.AddChild(_biome);
        biomeRow.AddChild(_floorMeta);
        _floorMeta.AddThemeFontSizeOverride("font_size", 11);
        _biome.ItemSelected += index =>
        {
            if (!_refreshing && !_targetInventory && Assembly != null && Data != null && SelectedFloor is int slot)
            { Assembly.SetBiome(slot, Data.Biomes[(int)index].Id); Refresh(); }
        };
        _floorSlots.HorizontalAlignment = HorizontalAlignment.Center;
        _floorSlots.AddThemeFontSizeOverride("font_size", 12);
        _floorSlots.AddThemeColorOverride("font_color", new Color("91aeb8"));
        _editor.AddChild(_floorSlots);
        var actionCenter = new CenterContainer();
        _editor.AddChild(actionCenter);
        var actions = new VBoxContainer { Name = "FloorActions", CustomMinimumSize = new Vector2(380, 0) };
        actions.AddThemeConstantOverride("separation", 4);
        actionCenter.AddChild(actions);
        _install = AddButton(actions, "Для установки выберите активность", () => InstallSelectedActivity());
        _install.Name = "InstallActivity";
        _install.CustomMinimumSize = new Vector2(0, 38);
        _install.AddThemeFontSizeOverride("font_size", 13);
        var primary = MontageSurface(.95f, true, 8);
        primary.BgColor = new Color("29434f");
        primary.BorderColor = new Color("91aeb8");
        _install.AddThemeStyleboxOverride("normal", primary);
        _removeFloor = AddButton(actions, "Снять активность", () => RemoveSelectedActivity());
        _removeFloor.Name = "RemoveActivity";
        _removeFloor.CustomMinimumSize = new Vector2(0, 26);
        _removeFloor.AddThemeFontSizeOverride("font_size", 11);
        _removeFloor.AddThemeStyleboxOverride("normal", MontageSurface(.2f, padding: 4));
        var result = new ScrollContainer { Name = "ActionResult", CustomMinimumSize = new Vector2(0, 26),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        actions.AddChild(result);
        var resultLines = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        resultLines.AddThemeConstantOverride("separation", 0);
        result.AddChild(resultLines);
        resultLines.AddChild(_installReason);
        _installReason.AutowrapMode = TextServer.AutowrapMode.Off;
        _installReason.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _installReason.AddThemeFontSizeOverride("font_size", 11);
        resultLines.AddChild(_status);
        _status.AutowrapMode = TextServer.AutowrapMode.Off;
        _status.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _status.AddThemeFontSizeOverride("font_size", 11);
        _status.Visible = false;
        _editor.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        var catalogueHeader = new HBoxContainer();
        work.AddChild(catalogueHeader);
        var catalogueTitle = Text(catalogueHeader, "АКТИВНОСТИ");
        catalogueTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        catalogueTitle.AddThemeFontSizeOverride("font_size", 12);
        _activityPage.Name = "ActivityPage";
        _activityPage.CustomMinimumSize = new Vector2(72, 24);
        _activityPage.AddThemeFontSizeOverride("font_size", 11);
        catalogueHeader.AddChild(_activityPage);
        _activityPage.ItemSelected += _ => RefreshInventory();
        var lattice = _catalogueLattice = new PanelContainer { Name = "CatalogueLattice", CustomMinimumSize = new Vector2(0, 114) };
        lattice.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        work.AddChild(lattice);
        _glassWindow.Setup(_activityGrid);
        lattice.AddChild(_glassWindow);
        lattice.AddChild(_activityGrid);
        _activityGrid.AddThemeConstantOverride("h_separation", 6);
        _activityGrid.AddThemeConstantOverride("v_separation", 8);
        _activityGrid.SortChildren += ArrangeActivitySlots;
        var commands = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        commands.AddThemeConstantOverride("separation", 8);
        work.AddChild(commands);
        _shuffle.Name = "ShuffleInventory";
        _shuffle.CustomMinimumSize = new Vector2(210, 30);
        _shuffle.AutowrapMode = TextServer.AutowrapMode.Off;
        _shuffle.AddThemeFontSizeOverride("font_size", 10);
        TowerBoardArt.Decorate(_shuffle, TowerBoardArt.Currency("shuffle"), 24);
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            var compact = (StyleBoxFlat)_shuffle.GetThemeStylebox(state).Duplicate();
            compact.ContentMarginTop = compact.ContentMarginBottom = 3;
            compact.ContentMarginLeft = compact.ContentMarginRight = 4;
            _shuffle.AddThemeStyleboxOverride(state, compact);
        }
        _shuffle.Pressed += () => ShuffleInventory();
        _shuffle.TooltipText = "Заполнить только пустые этажи экземплярами текущего ранга. Занятые этажи и их свойства сохраняются; при отказе ничего не тратится.";
        commands.AddChild(_shuffle);
        var check = AddButton(commands, "Проверить вход", () => Report(ValidateForTest(), "Проверка пройдена."));
        check.Name = "ValidateEntry";
        check.CustomMinimumSize = new Vector2(108, 30);
        check.AddThemeFontSizeOverride("font_size", 11);
        check.AutowrapMode = TextServer.AutowrapMode.Off;
        check.AddThemeStyleboxOverride("normal", MontageSurface(.5f, padding: 6));
        var summitRow = new HBoxContainer();
        summitRow.AddThemeConstantOverride("separation", 6);
        commands.AddChild(summitRow);
        summitRow.AddChild(new TextureRect { Name = "SummitIcon", Texture = TowerBoardArt.Symbol("summit"),
            CustomMinimumSize = new Vector2(24, 24), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore });
        var summit = Text(summitRow, "Вершина · Убер-босс\nЗабег пока недоступен");
        summit.Name = "TowerSummit";
        summit.AddThemeFontSizeOverride("font_size", 10);
        summit.CustomMinimumSize = new Vector2(130, 0);

        var sidebar = new VBoxContainer { Name = "SourceColumn", CustomMinimumSize = new Vector2(250, 0) };
        columns.AddChild(new VSeparator());
        columns.AddChild(sidebar);
        var rightScroll = new ScrollContainer { Name = "CurrencyTargetScroll", SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        sidebar.AddChild(rightScroll);
        _sourceDetail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _sourceDetail.AddThemeConstantOverride("separation", 7);
        rightScroll.AddChild(_sourceDetail);
        Text(_sourceDetail, "ВЫБРАННАЯ АКТИВНОСТЬ").AddThemeFontSizeOverride("font_size", 11);
        BuildReferenceStyle();
        _sourceDetail.AddChild(_sourceObjective);
        _sourceDetail.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });
        _sourceDetail.AddChild(_sourceProperties);
        _sourceDetail.AddChild(new HSeparator());
        _sourceDetail.AddChild(_currencyTarget);
        var more = AddButton(_sourceDetail, "Цель / качество / свойства ▸", () => { });
        more.Name = "ToggleTargetDetails";
        more.CustomMinimumSize = new Vector2(0, 24);
        more.AddThemeFontSizeOverride("font_size", 11);
        more.AddThemeStyleboxOverride("normal", MontageSurface(.2f, padding: 4));
        var advanced = new VBoxContainer { Name = "TargetDetails", Visible = false };
        _sourceDetail.AddChild(advanced);
        advanced.AddChild(_fullStatus);
        advanced.AddChild(_floorProperties);
        advanced.AddChild(_details);
        Text(advanced, "Свойство для адресного снятия").AddThemeFontSizeOverride("font_size", 11);
        advanced.AddChild(_mod);
        _mod.Name = "TargetModifier";
        _mod.ItemSelected += _ => RefreshCurrencyDescription();
        more.Pressed += () => { advanced.Visible = !advanced.Visible; more.Text = advanced.Visible ? "Цель / качество / свойства ▾" : "Цель / качество / свойства ▸"; };
        _sourceDetail.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
        Text(_sourceDetail, "РАСХОДНИКИ").AddThemeFontSizeOverride("font_size", 12);
        _sourceDetail.AddChild(_currencyGrid);
        _currencyInfo.Name = "CurrencyDescription";
        sidebar.AddChild(_currencyInfo);
        foreach (var label in new[] { _details, _floorProperties, _fullStatus, _currencyInfo, _sourceObjective, _sourceProperties, _currencyTarget, _bonus })
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.AddThemeFontSizeOverride("font_size", 11);
            label.AddThemeColorOverride("font_color", RoomHudTheme.MutedText);
        }
        _sourceObjective.AddThemeFontSizeOverride("font_size", 12);
        _sourceObjective.CustomMinimumSize = new Vector2(0, 36);
        foreach (var option in new[] { _rank, _mod, _biome }) { option.FitToLongestItem = false; option.ClipText = true; }
        var footer = new HBoxContainer();
        root.AddChild(footer);
        _sourceRoute.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _sourceRoute.AddThemeFontSizeOverride("font_size", 10);
        _sourceRoute.AutowrapMode = TextServer.AutowrapMode.Off;
        _sourceRoute.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        footer.AddChild(_sourceRoute);
        _bonus.AddThemeFontSizeOverride("font_size", 10);
        _bonus.AutowrapMode = TextServer.AutowrapMode.Off;
        footer.AddChild(_bonus);
    }

    private void ConfigureMontageFloor(Button button, int slot)
    {
        button.CustomMinimumSize = new Vector2(176, 91);
        var number = new Label { Name = "FloorNumber", Text = $"{slot + 1:00}", Position = new Vector2(8, 18), MouseFilter = MouseFilterEnum.Ignore };
        number.AddThemeFontOverride("font", _montageSerif);
        number.AddThemeFontSizeOverride("font_size", 20);
        number.AddThemeColorOverride("font_color", new Color("8ba6b4"));
        button.AddChild(number);
        var icon = new TextureRect { Name = "FloorMotif" };
        var socket = PortraitSocket(icon, 28);
        button.AddChild(socket);
        socket.Position = new Vector2(40, 17);
        socket.Size = new Vector2(28, 28);
        var title = new Label { Name = "FloorName", Text = $"Этаж {slot + 1}", Position = new Vector2(76, 13), MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeFontSizeOverride("font_size", 12);
        button.AddChild(title);
        var copy = new Label { Name = "FloorActivity", Position = new Vector2(76, 34), Size = new Vector2(96, 28),
            AutowrapMode = TextServer.AutowrapMode.Word, MouseFilter = MouseFilterEnum.Ignore };
        copy.AddThemeFontSizeOverride("font_size", 10);
        button.AddChild(copy);
        var meta = new Label { Name = "FloorMetadata", Position = new Vector2(40, 65), MouseFilter = MouseFilterEnum.Ignore };
        meta.AddThemeFontSizeOverride("font_size", 10);
        button.AddChild(meta);
    }
}
