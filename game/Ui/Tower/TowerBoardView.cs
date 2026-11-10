using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dialoid.Core.Simulation;
using Dialoid.Core.Tower;
using Godot;

namespace Dialoid.Game.Ui.Tower;

/// <summary>Transient assembly workbench. No saved progress, world, run or rewards.</summary>
public partial class TowerBoardView : Control
{
    public event Action? CloseRequested;
    public TowerData? Data { get; private set; }
    public TowerAssembly? Assembly { get; private set; }
    public Wallet? Wallet { get; private set; }
    public ActivityInventory? Inventory { get; private set; }
    public string? SelectedInventoryId => _selectedInventoryId;
    public bool InventoryTarget => _targetInventory;
    public int? SelectedFloor { get; private set; } = 0;
    public const int TestCharacterLevel = TowerDataLoader.LevelCap;
    public string StatusText => _status.Text;
    private readonly Dictionary<int, TowerAssembly> _assemblies = new();
    private readonly List<Button> _floors = new();
    private readonly Dictionary<string, Button> _inventoryNodes = new(StringComparer.Ordinal);
    private readonly List<Button> _currencies = new();
    private readonly GridContainer _activityGrid = new() { Name = "ActivityGrid", Columns = 3 };
    private readonly GridContainer _currencyGrid = new() { Name = "CurrencyGrid", Columns = 4 };
    private readonly Label _inventoryInfo = new() { Name = "ActivityDescription" };
    private Button _install = null!;
    private Button _removeFloor = null!;
    private string? _selectedInventoryId;
    private bool _targetInventory;
    private readonly Label _status = new();
    private readonly Label _details = new();
    private readonly Label _currencyInfo = new();
    private readonly Label _rankInfo = new();
    private readonly Label _bonus = new();
    private readonly OptionButton _rank = new();
    private readonly OptionButton _biome = new();
    private readonly OptionButton _mod = new();
    private readonly Button _shuffle = new();
    private readonly Button _close = new();
    private Button _wholeTower = null!;
    private string? _describedCurrency;
    private bool _built;
    private bool _refreshing;
    private Control? _wheelFocus;

    /// <summary>Accepts the repository data directory (containing tower/). Repeated calls do not refill currency.</summary>
    public void Setup(string dataDirectory)
    {
        if (Data != null) return;
        Build();
        try
        {
            var result = TowerDataLoader.Load(Path.Combine(dataDirectory, "tower"));
            if (result.Data == null)
            {
                _status.Text = "Не удалось загрузить башню: " + string.Join("; ", result.Errors);
                return;
            }
            Data = result.Data;
            Wallet = new Wallet(Data);
            Inventory = new ActivityInventory(Data);
            foreach (var currency in Data.Currencies)
                Wallet.Add(currency.Id, Data.Rules.Ranks.Count, 20);
            for (int i = 1; i <= Data.Rules.Ranks.Count; i++)
            {
                _rank.AddItem($"Ранг {i}", i);
                foreach (var type in Data.FloorTypes) Inventory.Add(type.Id, i, 5);
            }
            foreach (var biome in Data.Biomes) _biome.AddItem(biome.Name);
            foreach (var currency in Data.Currencies.Where(c => c.Op != CurrencyOp.Shuffle))
            {
                string id = currency.Id;
                var button = AddInventoryNode(_currencyGrid, TowerBoardArt.Currency(id), () => ApplyCurrency(id));
                button.Name = "Currency_" + id;
                button.FocusEntered += () => DescribeCurrency(id);
                button.MouseEntered += () => DescribeCurrency(id);
                _currencies.Add(button);
            }
            for (int i = Data.Rules.Floors - 1; i >= 0; i--)
            {
                int slot = i;
                var floorButton = AddButton(_middle!, "", () => SelectFloor(slot));
                floorButton.Name = "Floor_" + slot;
                _floors.Insert(0, floorButton);
            }
            SwitchRank(1);
            _status.Text = "Выберите экземпляр активности слева и этаж в центре, затем установите. Намазки применяются к выбранной цели. Игровые этажи ещё не реализованы.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _status.Text = "Не удалось загрузить башню: " + ex.Message;
        }
    }

    public override void _Ready()
    {
        Build();
        _close.GrabFocus();
        VisibilityChanged += () => { if (IsVisibleInTree()) _close.GrabFocus(); };
    }

    private VBoxContainer? _left, _middle, _right;
    private void Build()
    {
        if (_built) return;
        _built = true;
        foreach (var option in new[] { _mod, _biome, _rank })
        {
            option.FitToLongestItem = false;
            option.ClipText = true;
        }
        Theme = CreateTheme();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var background = new ColorRect { Color = new Color("0c1114"), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margin = new MarginContainer();
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 16);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);
        var header = new HBoxContainer();
        root.AddChild(header);
        header.AddChild(new TextureRect { Texture = TowerBoardArt.Symbol("tower"),
            CustomMinimumSize = new Vector2(32, 32), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore });
        var title = Text(header, "БАШНЯ  /  Сборка маршрута");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _close.Text = "Закрыть";
        TowerBoardArt.Decorate(_close, TowerBoardArt.Symbol("close"), 24);
        _close.CustomMinimumSize = new Vector2(120, 40);
        _close.Pressed += () => CloseRequested?.Invoke();
        header.AddChild(_close);
        Text(root, "Тестовый набор: по 5 активностей каждого типа/ранга, по 20 расходников · один раз · не сохраняется. Персонаж: уровень " + TestCharacterLevel + ".");
        Text(root, "Esc / F6 / B — закрыть · Tab / стрелки — выбор · Enter / A — применить");
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 12);
        root.AddChild(columns);
        _left = Column(columns, "activities", "АКТИВНОСТИ И РАНГ");
        _middle = Column(columns, "ascent", "ВОСХОЖДЕНИЕ  ↑");
        _middle.AddThemeConstantOverride("separation", 4);
        _right = Column(columns, "inscriptions", "СВОЙСТВА И РАСХОДНИКИ");
        _left.AddChild(_rank);
        _rank.ItemSelected += index => { if (!_refreshing) SwitchRank(_rank.GetItemId((int)index)); };
        _left.AddChild(_rankInfo);
        _rankInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Text(_left, "ИНВЕНТАРЬ АКТИВНОСТЕЙ");
        _left.AddChild(_activityGrid);
        _left.AddChild(_inventoryInfo);
        _inventoryInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _install = AddButton(_left, "Установить на этаж 1", () => InstallSelectedActivity());
        _install.Name = "InstallActivity";
        TowerBoardArt.Decorate(_install, TowerBoardArt.Symbol("confirm"), 24);
        TowerBoardArt.Decorate(_shuffle, TowerBoardArt.Currency("shuffle"));
        _shuffle.CustomMinimumSize = new Vector2(0, 42);
        _shuffle.Name = "ShuffleInventory";
        _shuffle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _shuffle.Pressed += () => ShuffleInventory();
        _shuffle.TooltipText = "Заполнить только пустые этажи экземплярами текущего ранга из инвентаря. Установленные активности и их намазки сохраняются. При отказе ничего не тратится.";
        _left.AddChild(_shuffle);
        _left.AddChild(_bonus);
        _bonus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var check = AddButton(_left, "Проверить вход", () => Report(ValidateForTest(), "Проверка пройдена."));
        TowerBoardArt.Decorate(check, TowerBoardArt.Symbol("confirm"));
        var summit = new PanelContainer();
        var summitFrame = TowerBoardArt.Card();
        summitFrame.ContentMarginTop = summitFrame.ContentMarginBottom = 4;
        summit.AddThemeStyleboxOverride("panel", summitFrame);
        summit.TooltipText = "После пятого этажа. Каталог боссов отсутствует; босс не назначен. Забег пока недоступен.";
        _middle.AddChild(summit);
        var summitContent = new HBoxContainer();
        summit.AddChild(summitContent);
        summitContent.AddChild(new TextureRect { Texture = TowerBoardArt.Symbol("summit"),
            CustomMinimumSize = new Vector2(32, 32), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore });
        var summitText = Text(summitContent, "ВЕРШИНА · Убер-босс\nЗабег пока недоступен");
        summitText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _wholeTower = AddButton(_middle, "Вся башня · цель расходника", () => SelectFloor(null));
        TowerBoardArt.Decorate(_wholeTower, TowerBoardArt.Symbol("tower"));
        _removeFloor = AddButton(_middle, "Снять выбранный этаж", () => RemoveSelectedActivity());
        _removeFloor.Name = "RemoveActivity";
        Text(_right, "ИНВЕНТАРЬ НАМАЗОК");
        _right.AddChild(_currencyGrid);
        _right.AddChild(_details);
        _details.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Text(_right, "Биом (только внешний вид этажа)");
        _right.AddChild(_biome);
        _biome.ItemSelected += index =>
        {
            if (!_refreshing && !_targetInventory && Assembly != null && Data != null && SelectedFloor is int slot)
            { Assembly.SetBiome(slot, Data.Biomes[(int)index].Id); Refresh(); }
        };
        Text(_right, "Свойство для адресного Снятия");
        _right.AddChild(_mod);
        _mod.ItemSelected += _ => RefreshCurrencyDescription();
        _currencyInfo.Name = "CurrencyDescription";
        _currencyInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _currencyInfo.Text = "Расходники: наведите указатель или выберите кнопку клавиатурой / геймпадом — здесь появятся цель, стоимость и последствия.";
        root.AddChild(_currencyInfo);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.CustomMinimumSize = new Vector2(0, 54);
        root.AddChild(_status);
    }

    private static VBoxContainer Column(HBoxContainer parent, string art, string heading)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipContents = true };
        panel.AddThemeStyleboxOverride("panel", TowerBoardArt.Panel());
        parent.AddChild(panel);
        string path = $"res://Ui/Art/TowerPanels/{art}-v2.png";
        if (ResourceLoader.Exists(path))
        {
            var texture = ResourceLoader.Load<Texture2D>(path);
            if (texture != null) panel.AddChild(new TextureRect { Texture = texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = MouseFilterEnum.Ignore });
        }
        var surface = new PanelContainer();
        surface.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.04f, 0.06f, 0.07f, 0.82f),
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = art == "ascent" ? 10 : 14, ContentMarginBottom = art == "ascent" ? 10 : 14 });
        panel.AddChild(surface);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        surface.AddChild(scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(content);
        Text(content, heading);
        return content;
    }

    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
        if (_wheelFocus != null && (input.IsActionPressed("ui_focus_next") || input.IsActionPressed("ui_focus_prev") ||
            input.IsActionPressed("ui_up") || input.IsActionPressed("ui_down") ||
            input.IsActionPressed("ui_left") || input.IsActionPressed("ui_right")))
        {
            // Device switching can release GUI focus; finish that before resuming navigation.
            Dialoid.Game.Input.InputDevice.Instance?.Observe(input);
            if (GetViewport().GuiGetFocusOwner() == null && GodotObject.IsInstanceValid(_wheelFocus) &&
                _wheelFocus.IsVisibleInTree() && _wheelFocus.FocusMode != FocusModeEnum.None)
                _wheelFocus.GrabFocus();
            _wheelFocus = null;
        }
        if (input is not InputEventMouseButton { Pressed: true } mouse ||
            mouse.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight) ||
            !GetGlobalRect().HasPoint(mouse.Position)) return;
        // Explicit mouse scrolling takes over from GUI focus following. Selection is separate;
        // leave the event unhandled so the panel receives its normal wheel input.
        var focus = GetViewport().GuiGetFocusOwner();
        if (focus != null && IsAncestorOf(focus))
        {
            _wheelFocus = focus;
            focus.ReleaseFocus();
        }
    }

    public override void _Process(double delta)
    {
        EnsureFocusedControlVisible();
    }

    // Layout may settle across several frames after the footer changes height.
    // Scroll only when the current focused control actually leaves its viewport.
    private void EnsureFocusedControlVisible()
    {
        if (!IsInsideTree() || !IsVisibleInTree()) return;
        var focus = GetViewport().GuiGetFocusOwner();
        if (focus == null || !IsAncestorOf(focus)) return;
        for (Node? ancestor = focus.GetParent(); ancestor != null && ancestor != this; ancestor = ancestor.GetParent())
        {
            if (ancestor is not ScrollContainer scroll) continue;
            var viewportRect = scroll.GetGlobalRect();
            var focusRect = focus.GetGlobalRect();
            if (focusRect.Position.X < viewportRect.Position.X || focusRect.End.X > viewportRect.End.X ||
                focusRect.Position.Y < viewportRect.Position.Y || focusRect.End.Y > viewportRect.End.Y)
                scroll.EnsureControlVisible(focus);
            break;
        }
    }

    private static Label Text(Node parent, string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeColorOverride("font_color", new Color("d8d5c6"));
        parent.AddChild(label);
        return label;
    }

    private static Button AddButton(Node parent, string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 42), FocusMode = FocusModeEnum.All,
            AutowrapMode = TextServer.AutowrapMode.WordSmart };
        parent.AddChild(button);
        button.AddThemeStyleboxOverride("normal", TowerBoardArt.Card());
        button.AddThemeStyleboxOverride("focus", TowerBoardArt.Focus());
        button.Pressed += action;
        return button;
    }

    private static Button AddInventoryNode(GridContainer grid, Texture2D? icon, Action action, string? activityName = null)
    {
        var button = AddButton(grid, "", action);
        button.CustomMinimumSize = new Vector2(64, 64);
        button.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        button.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var image = new TextureRect { Texture = icon, MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        button.AddChild(image);
        image.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        image.OffsetLeft = image.OffsetTop = -16;
        image.OffsetRight = image.OffsetBottom = 16;
        var count = new Label { Name = "Count", MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        button.AddChild(count);
        count.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        count.OffsetLeft = count.OffsetTop = 4;
        count.OffsetRight = count.OffsetBottom = -5;
        count.AddThemeFontSizeOverride("font_size", 13);
        var rank = new Label { Name = "Rank", MouseFilter = MouseFilterEnum.Ignore };
        button.AddChild(rank);
        rank.Position = new Vector2(5, 3);
        rank.AddThemeFontSizeOverride("font_size", 12);
        if (activityName != null)
        {
            button.CustomMinimumSize = new Vector2(84, 82);
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            image.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
            image.OffsetLeft = -16;
            image.OffsetRight = 16;
            image.OffsetTop = 8;
            image.OffsetBottom = 40;
            count.VerticalAlignment = VerticalAlignment.Top;
            var caption = Text(button, activityName);
            caption.Name = "Type";
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.VerticalAlignment = VerticalAlignment.Center;
            caption.AddThemeFontSizeOverride("font_size", 13);
            caption.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            caption.OffsetLeft = 4;
            caption.OffsetRight = -4;
            caption.OffsetTop = 40;
            caption.OffsetBottom = -4;
        }
        return button;
    }

    private ActivityInventoryGroup? SelectedGroup() => Assembly == null ? null :
        Inventory?.Groups(Assembly.Spec.Rank).FirstOrDefault(g => g.Id == _selectedInventoryId);

    public void SelectInventory(string groupId)
    {
        if (Assembly == null || Inventory == null || !Inventory.Groups(Assembly.Spec.Rank).Any(g => g.Id == groupId)) return;
        _selectedInventoryId = groupId;
        _targetInventory = true;
        Refresh();
    }

    public TowerRefusal InstallSelectedActivity()
    {
        var result = Inventory == null || Assembly == null || SelectedFloor is not int slot || SelectedGroup() == null
            ? TowerRefusal.WrongTarget : Inventory.TryInstall(Assembly, slot, _selectedInventoryId!);
        if (result == TowerRefusal.None) _targetInventory = false;
        Report(result, "Один экземпляр установлен. Если этаж был занят, прежняя активность возвращена в инвентарь.");
        return result;
    }

    public TowerRefusal ShuffleInventory()
    {
        var result = Inventory == null || Assembly == null || Wallet == null
            ? TowerRefusal.WrongTarget : Inventory.FillRandom(Assembly, Wallet);
        Report(result, "Пустые этажи заполнены из инвентаря. Установленные активности и намазки сохранены.");
        return result;
    }

    public TowerRefusal RemoveSelectedActivity()
    {
        var result = Inventory == null || Assembly == null || SelectedFloor is not int slot
            ? TowerRefusal.WrongTarget : Inventory.Remove(Assembly, slot);
        if (result == TowerRefusal.None) _targetInventory = false;
        Report(result, "Экземпляр возвращён в инвентарь вместе с намазками.");
        return result;
    }

    private static string ActivityObjective(string typeId) => typeId switch
    {
        "timed_run" => "Очистите алтари до конца отсчёта, затем победите босса.",
        "trial" => "Выполните задачу испытания: сопровождение, удержание, ритуал или рычаги.",
        "clear" => "Истребите нужное число монстров на этаже.",
        "waves" => "Отбейте все волны монстров на арене.",
        "bosses" => "Победите всех боссов на этаже.",
        _ => ""
    };

    private void RefreshInventory()
    {
        if (Data == null || Inventory == null || Assembly == null) return;
        var groups = Inventory.Groups(Assembly.Spec.Rank);
        var ids = groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        bool restoreFocus = false;
        foreach (var id in _inventoryNodes.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            var old = _inventoryNodes[id];
            restoreFocus |= old.HasFocus();
            _activityGrid.RemoveChild(old);
            old.QueueFree();
            _inventoryNodes.Remove(id);
        }
        if (_selectedInventoryId != null && !ids.Contains(_selectedInventoryId))
        {
            _selectedInventoryId = null;
            _targetInventory = false;
        }
        foreach (var group in groups)
        {
            string id = group.Id;
            if (!_inventoryNodes.TryGetValue(id, out var button))
            {
                button = AddInventoryNode(_activityGrid, TowerBoardArt.Activity(group.TypeId), () => SelectInventory(id), Data.FloorType(group.TypeId).Name);
                button.Name = "Activity_" + id.Replace(':', '_');
                _inventoryNodes.Add(id, button);
            }
            button.GetNode<Label>("Count").Text = group.Count.ToString();
            button.GetNode<Label>("Rank").Text = "Р" + group.Rank;
            button.TooltipText = $"{Data.FloorType(group.TypeId).Name} · ранг {group.Rank} · {group.Count} шт.\n" + ActivityObjective(group.TypeId);
            button.AccessibilityName = (id == _selectedInventoryId ? "Выбрано. " : "") + $"{Data.FloorType(group.TypeId).Name}, ранг {group.Rank}, {group.Count} шт.";
            button.AccessibilityDescription = button.TooltipText;
            button.AddThemeStyleboxOverride("normal", TowerBoardArt.Card(id == _selectedInventoryId));
            button.AddThemeStyleboxOverride("hover", TowerBoardArt.Card(id == _selectedInventoryId));
        }
        var selectedGroup = groups.FirstOrDefault(g => g.Id == _selectedInventoryId);
        if (selectedGroup != null)
        {
            _inventoryInfo.Text = $"Выбрано: {Data.FloorType(selectedGroup.TypeId).Name} · ранг {selectedGroup.Rank} · {selectedGroup.Count} шт.\n" +
                ActivityObjective(selectedGroup.TypeId) +
                (selectedGroup.Variant == null ? "" : "\nРазновидность: " + VariantName(selectedGroup.Variant)) +
                (selectedGroup.EverModified ? "\nОтдельный экземпляр после намазки." : "") +
                (selectedGroup.Mods.Count == 0 ? "\nБез свойств." : "\n" + string.Join("\n", selectedGroup.Mods.Select(m => $"{ModText(m)} · тир {m.Tier}")));
        }
        else _inventoryInfo.Text = groups.Count == 0 ? "В этом ранге нет свободных активностей. Снимите этаж, чтобы вернуть экземпляр." :
            "Выберите активность для установки или намазки.";
        bool installHadFocus = _install.HasFocus();
        bool removeHadFocus = _removeFloor.HasFocus();
        _install.Text = SelectedFloor is not int slot ? "Для установки выберите этаж" : selectedGroup == null
            ? "Для установки выберите активность" : $"Установить на этаж {slot + 1}";
        _install.Disabled = SelectedFloor == null || selectedGroup == null;
        _removeFloor.Disabled = SelectedFloor is not int selected || Assembly.Spec.Floors[selected].TypeId == null;
        _removeFloor.Text = SelectedFloor is not int destination ? "Для снятия выберите этаж" : _removeFloor.Disabled
            ? $"Этаж {destination + 1} пуст" : $"Снять активность этажа {destination + 1}";
        if ((restoreFocus || installHadFocus && _install.Disabled || removeHadFocus && _removeFloor.Disabled) && IsInsideTree())
        {
            if (_selectedInventoryId != null && _inventoryNodes.TryGetValue(_selectedInventoryId, out var selectedNode)) selectedNode.GrabFocus();
            else if (!_install.Disabled) _install.GrabFocus();
            else if (SelectedFloor is int floor) _floors[floor].GrabFocus();
            else _wholeTower.GrabFocus();
        }
    }

    public void SelectFloor(int? slot)
    {
        if (Assembly == null || slot is int i && (i < 0 || i >= Assembly.Spec.Floors.Count)) return;
        SelectedFloor = slot;
        _targetInventory = false;
        Refresh();
    }

    public void SwitchRank(int rank)
    {
        if (Data == null || rank < 1 || rank > Data.Rules.Ranks.Count) return;
        if (!_assemblies.TryGetValue(rank, out var assembly))
            _assemblies[rank] = assembly = new TowerAssembly(Data, rank, RandomSources.Create(SimMode.Roll, System.Random.Shared.Next()));
        Assembly = assembly;
        Refresh();
    }

    public TowerRefusal SetFloorType(string typeId)
    {
        var group = Assembly == null ? null : Inventory?.Groups(Assembly.Spec.Rank).FirstOrDefault(g => g.TypeId == typeId);
        var result = Assembly == null || SelectedFloor == null || Inventory == null ? TowerRefusal.WrongTarget :
            Data != null && !Data.TryGetFloorType(typeId, out _) ? TowerRefusal.UnknownType :
            group == null ? TowerRefusal.EmptySlot : Inventory.TryInstall(Assembly, SelectedFloor.Value, group.Id);
        _targetInventory = false;
        Report(result);
        return result;
    }

    public TowerRefusal ApplyCurrency(string currencyId)
    {
        string? modId = _mod.Selected >= 0 ? _mod.GetItemMetadata(_mod.Selected).AsString() : null;
        var before = TargetMods().ToArray();
        string? variantBefore = _targetInventory ? SelectedGroup()?.Variant : SelectedFloor is int s ? Assembly?.Spec.Floors[s].Variant : null;
        var result = Data == null || Assembly == null || Wallet == null ? TowerRefusal.WrongTarget :
            _targetInventory ? Inventory == null || _selectedInventoryId == null ? TowerRefusal.WrongTarget :
                Inventory.ApplyToInventory(_selectedInventoryId, currencyId, Wallet, modId) :
            SelectedFloor is int destination ? Inventory == null ? TowerRefusal.WrongTarget :
                Inventory.ApplyToFloor(Assembly, destination, currencyId, Wallet, modId) :
            Crafting.Apply(Data, Assembly, Wallet, currencyId, null, modId);
        if (result == TowerRefusal.None && _targetInventory) _selectedInventoryId = Inventory!.LastCraftedGroupId;
        if (Data != null && Assembly != null && Wallet != null && Data.TryGetCurrency(currencyId, out var currency))
        {
            var after = TargetMods();
            var removed = before.Except(after).Select(m => $"{ModText(m)} (тир {m.Tier})");
            var added = after.Except(before).Select(m => $"{ModText(m)} (тир {m.Tier})");
            var changes = new List<string>();
            if (removed.Any()) changes.Add("Удалено: " + string.Join("; ", removed));
            if (added.Any()) changes.Add("Добавлено: " + string.Join("; ", added));
            string? variantAfter = _targetInventory ? SelectedGroup()?.Variant : SelectedFloor is int slot ? Assembly.Spec.Floors[slot].Variant : null;
            if (variantBefore != variantAfter) changes.Add($"Разновидность: {VariantName(variantBefore ?? "—")} → {VariantName(variantAfter ?? "—")}");
            if (changes.Count == 0) changes.Add("Свойства не изменились");
            _status.Text = $"{TargetName()} · {currency.Name}: {TowerTexts.Refusal(result)}. " + string.Join(". ", changes) +
                $". Потрачено: {(result == TowerRefusal.None ? 1 : 0)} шт. Осталось: {Wallet.CountUsable(currencyId, Assembly.Spec.Rank)} шт.";
            Refresh();
        }
        else Report(result);
        return result;
    }

    private IReadOnlyList<ModInstance> TargetMods() => Assembly == null ? Array.Empty<ModInstance>() :
        _targetInventory ? SelectedGroup()?.Mods ?? Array.Empty<ModInstance>() :
        SelectedFloor is int slot ? Assembly.Spec.Floors[slot].Mods : Assembly.Spec.TowerMods;
    private string TargetName() => _targetInventory ? SelectedGroup() is { } group
        ? $"Инвентарь: {Data!.FloorType(group.TypeId).Name} · ранг {group.Rank}" : "Инвентарь: выберите экземпляр" :
        SelectedFloor is int slot ? $"Этаж {slot + 1}" : "Вся башня";
    private void DescribeCurrency(string id)
    {
        _describedCurrency = id;
        RefreshCurrencyDescription();
    }
    private void RefreshCurrencyDescription()
    {
        if (Data == null || Assembly == null || Wallet == null || _describedCurrency == null) return;
        var currency = Data.Currency(_describedCurrency);
        string selected = currency.Op == CurrencyOp.RemoveOne && _mod.Selected >= 0 ? " Выбрано: " + _mod.GetItemText(_mod.Selected) + "." : "";
        _currencyInfo.Text = $"{currency.Name} · цель: {TargetName()}. {OperationText(currency.Op)}{selected} " +
            $"Стоимость: 1 шт. при успехе; доступно {Wallet.CountUsable(currency.Id, Assembly.Spec.Rank)} шт., с ранга {currency.DropsFromRank}. При отказе валюта не тратится.";
    }

    /// <summary>Checks the real Core entry gate against fresh, locked progress; never starts a world or awards anything.</summary>
    public TowerRefusal ValidateForTest()
    {
        if (Data == null || Assembly == null) return TowerRefusal.EmptySlot;
        if (!RankRules.LevelAllows(Data.Rules, Assembly.Spec.Rank, TestCharacterLevel)) return TowerRefusal.RankLocked;
        return TowerRun.TryEnter(Data, Assembly, new TowerProgress(), TestCharacterLevel,
            RandomSources.Create(SimMode.Roll, 1), out _);
    }

    private void Report(TowerRefusal result, string success = "Готово")
    {
        _status.Text = (result == TowerRefusal.None ? success : TowerTexts.Refusal(result)) +
            " Игровые этажи ещё не реализованы; забег и награды не создаются.";
        Refresh();
    }

    private void Refresh()
    {
        if (Data == null || Assembly == null || Wallet == null) return;
        _refreshing = true;
        RefreshInventory();
        var spec = Assembly.Spec;
        _wholeTower.Text = (SelectedFloor == null ? "▶ " : "") + "Вся башня · цель расходника\n" +
            string.Join(" ", Enumerable.Range(0, Data.Rules.TowerSlots).Select(n => n < spec.TowerMods.Count ? "◆" : "◇")) +
            $" · свойств {spec.TowerMods.Count}/{Data.Rules.TowerSlots}";
        var towerFrame = TowerBoardArt.Card(SelectedFloor == null);
        towerFrame.ContentMarginTop = towerFrame.ContentMarginBottom = 4;
        _wholeTower.AddThemeStyleboxOverride("normal", towerFrame);
        _wholeTower.AddThemeStyleboxOverride("hover", towerFrame);
        _rank.Select(spec.Rank - 1);
        var rule = Data.Rules.Ranks[spec.Rank - 1];
        _rankInfo.Text = $"Уровень зоны: {RankRules.ZoneLevel(Data.Rules, spec.Rank, TestCharacterLevel)} · тир до {rule.MaxTier}\n" +
            $"Множитель опыта: {RankRules.XpMultiplier(Data.Rules, spec.Rank, TestCharacterLevel):0.##}\n" +
            "Все ранги доступны для сборки. Проверка входа: настоящий прогресс, открыт только ранг 1.";
        _shuffle.Text = $"Перемешать · цена {Assembly.NextShuffleCost} · доступно {Wallet.CountUsable("shuffle", spec.Rank)}\n" +
            $"Пустых: {spec.Floors.Count(f => f.TypeId == null)} · занятые сохранены";
        _shuffle.AccessibilityName = _shuffle.Text;
        _shuffle.AccessibilityDescription = _shuffle.TooltipText;
        _bonus.Text = Assembly.ShuffleBonusActive ? $"Бонус перемешивания: +{Data.Rules.ShuffleBonus:P0}" : "Бонус перемешивания не активен";
        for (int i = 0; i < _floors.Count; i++)
        {
            var f = spec.Floors[i];
            TowerBoardArt.Decorate(_floors[i], TowerBoardArt.Activity(f.TypeId));
            var floorFrame = TowerBoardArt.Card(SelectedFloor == i);
            floorFrame.ContentMarginTop = floorFrame.ContentMarginBottom = 4;
            _floors[i].AddThemeStyleboxOverride("normal", floorFrame);
            _floors[i].AddThemeStyleboxOverride("hover", floorFrame);
            _floors[i].Text = $"{(SelectedFloor == i ? "▶ " : "")}ЭТАЖ {i + 1} · {(f.TypeId == null ? "Выберите активность" : Data.FloorType(f.TypeId).Name)}\n" +
                $"{(f.BiomeId == null ? "Биом не выбран" : Data.Biomes.First(b => b.Id == f.BiomeId).Name)}" +
                (f.Variant == null ? "" : " · " + VariantName(f.Variant)) + "\n" +
                string.Join(" ", Enumerable.Range(0, Data.Rules.FloorSlots).Select(n => n < f.Mods.Count ? "◆" : "◇")) +
                $" · свойств {f.Mods.Count}/{Data.Rules.FloorSlots}";
        }
        var floor = SelectedFloor is int slot ? spec.Floors[slot] : null;
        var mods = TargetMods();
        _details.Text = "Цель: " + TargetName();
        var quality = SelectedFloor is int q
            ? Rewards.FloorQuality(Data, spec, q, false, Assembly.ShuffleBonusActive)
            : Rewards.SummitQuality(Data, spec, false, Assembly.ShuffleBonusActive);
        if (!_targetInventory) _details.Text += $"\nПрогноз качества {(SelectedFloor == null ? "вершины" : "этажа")}: ×{quality.Total:0.##}" +
            $"\nВысота ×{quality.Height:0.##} · свойства этажа ×{quality.FloorMods:0.##}" +
            $"\nСвойства башни ×{quality.TowerMods:0.##}" +
            $"\nРазнообразие ×{quality.Diversity:0.##} · перемешивание ×{quality.Shuffle:0.##}" +
            "\nБез бонуса за отсутствие смертей. Это прогноз, награды не выдаются.";
        if (_targetInventory && SelectedGroup() is { } group)
            _details.Text += $"\n{group.Count} шт. · " + (group.EverModified ? "отдельный экземпляр" : "стек") +
                (group.Variant == null ? "" : "\nРазновидность: " + VariantName(group.Variant));
        _details.Text += "\n\n" + (mods.Count == 0 ? "Свойств пока нет" : string.Join("\n", mods.Select(m => $"{ModText(m)} · тир {m.Tier}")));
        _biome.Disabled = _targetInventory || floor?.TypeId == null;
        if (floor?.BiomeId != null) _biome.Select(Data.Biomes.ToList().FindIndex(b => b.Id == floor.BiomeId));
        string? selectedMod = _mod.Selected >= 0 ? _mod.GetItemMetadata(_mod.Selected).AsString() : null;
        _mod.Clear();
        foreach (var m in mods)
        {
            _mod.AddItem(ModText(m));
            _mod.SetItemMetadata(_mod.ItemCount - 1, m.ModId);
            if (m.ModId == selectedMod) _mod.Select(_mod.ItemCount - 1);
        }
        _mod.Disabled = mods.Count == 0;
        var currencies = Data.Currencies.Where(c => c.Op != CurrencyOp.Shuffle).ToArray();
        for (int i = 0; i < currencies.Length; i++)
        {
            var currency = currencies[i];
            int count = Wallet.CountUsable(currency.Id, spec.Rank);
            _currencies[i].GetNode<Label>("Count").Text = count.ToString();
            _currencies[i].TooltipText = $"{currency.Name} · {count} шт.\n{OperationText(currency.Op)}\n" +
                $"Цель: {TargetName()}. Доступно с ранга {currency.DropsFromRank}. При успехе тратится 1.";
            _currencies[i].AccessibilityName = $"{currency.Name}, {count} шт.";
            _currencies[i].AccessibilityDescription = _currencies[i].TooltipText;
        }
        _refreshing = false;
        RefreshCurrencyDescription();
    }

    private string ModText(ModInstance mod) => Data!.Mod(mod.ModId).Name.Replace("{v}", mod.Value.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")), StringComparison.Ordinal);

    private static Theme CreateTheme()
    {
        var theme = new Theme { DefaultFontSize = 16 };
        foreach (string type in new[] { "Button", "OptionButton", "PopupMenu", "Label" })
        {
            theme.SetColor("font_color", type, new Color("d8d5c6"));
            theme.SetColor("font_hover_color", type, new Color("f0eee3"));
            theme.SetColor("font_pressed_color", type, new Color("f0eee3"));
            theme.SetColor("font_focus_color", type, new Color("f0eee3"));
            theme.SetColor("font_disabled_color", type, new Color("858e90"));
        }
        static StyleBoxFlat Box(string fill, string edge) => new()
        {
            BgColor = new Color(fill), BorderColor = new Color(edge),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8
        };
        foreach (string type in new[] { "Button", "OptionButton" })
        {
            theme.SetStylebox("normal", type, TowerBoardArt.Card());
            theme.SetStylebox("hover", type, Box("202f35", "7099a8"));
            theme.SetStylebox("pressed", type, Box("293b42", "9cbbc5"));
            theme.SetStylebox("disabled", type, Box("0e1519c9", "29363b"));
            theme.SetStylebox("focus", type, TowerBoardArt.Focus());
        }
        theme.SetStylebox("panel", "PopupMenu", Box("10191d", "526a74"));
        theme.SetStylebox("hover", "PopupMenu", Box("293b42", "7099a8"));
        theme.SetStylebox("panel", "PanelContainer", new StyleBoxEmpty());
        return theme;
    }

    private static string VariantName(string id) => id switch
    { "escort" => "Сопровождение", "hold" => "Удержание", "ritual" => "Ритуал", "levers" => "Рычаги", _ => id };
    private static string OperationText(CurrencyOp op) => op switch
    {
        CurrencyOp.AddMod => "Добавить случайное свойство к подходящей цели.",
        CurrencyOp.AddModBiased => "Добавить случайное свойство со смещением вероятности.",
        CurrencyOp.RerollOne => "Заменить одно случайное свойство.",
        CurrencyOp.RerollValueHigh => "Перебросить число одного свойства в верхней половине диапазона.",
        CurrencyOp.UpgradeTier => "Повысить тир случайного свойства.",
        CurrencyOp.Clear => "Удалить все свойства цели без возврата валюты.",
        CurrencyOp.RemoveOne => "Удалить выбранное выше свойство.",
        CurrencyOp.RerollVariant => "Сменить разновидность Испытания; свойства под тип будут сняты.",
        _ => "Перемешать башню."
    };
}
