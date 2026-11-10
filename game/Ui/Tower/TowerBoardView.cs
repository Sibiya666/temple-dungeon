using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dialoid.Core.Simulation;
using Dialoid.Core.Tower;
using Godot;
using Dialoid.Game.Ui.Art;

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
    private readonly GridContainer _activityGrid = new() { Name = "ActivityGrid", Columns = 8 };
    private readonly GridContainer _currencyGrid = new() { Name = "CurrencyGrid", Columns = 3 };
    private readonly List<Panel> _emptyActivitySlots = new();
    private readonly TowerGlassWindow _glassWindow = new();
    private Button _install = null!;
    private Button _removeFloor = null!;
    private string? _selectedInventoryId;
    private bool _targetInventory;
    private readonly Label _status = new();
    private readonly Label _details = new();
    private readonly Label _currencyInfo = new();
    private readonly Label _bonus = new();
    private readonly OptionButton _rank = new();
    private readonly OptionButton _activityPage = new() { Visible = false };
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
                _status.Show();
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
                button.CustomMinimumSize = new Vector2(60, 44);
                button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                var currencyImage = button.GetChild<TextureRect>(0);
                currencyImage.OffsetLeft = currencyImage.OffsetTop = -12;
                currencyImage.OffsetRight = currencyImage.OffsetBottom = 12;
                button.GetNode<Label>("Count").AddThemeFontSizeOverride("font_size", 11);
                button.FocusEntered += () => DescribeCurrency(id);
                button.MouseEntered += () => DescribeCurrency(id);
                void ClearPreview()
                {
                    if (_describedCurrency != id || button.HasFocus() || button.IsHovered()) return;
                    _describedCurrency = null;
                    _currencyInfo.Text = "";
                }
                button.FocusExited += ClearPreview;
                button.MouseExited += ClearPreview;
                _currencies.Add(button);
            }
            for (int i = 0; i < Data.Rules.Floors; i++)
            {
                int slot = i;
                var floorButton = AddButton(_floorList, "", () => SelectFloor(slot));
                ConfigureMontageFloor(floorButton, slot);
                floorButton.Name = "Floor_" + slot;
                _floors.Add(floorButton);
            }
            SwitchRank(1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _status.Text = "Не удалось загрузить башню: " + ex.Message;
            _status.Show();
        }
    }

    public override void _Ready()
    {
        Build();
        _close.GrabFocus();
        VisibilityChanged += () => { if (IsVisibleInTree()) _close.GrabFocus(); };
    }

    private void Build()
    {
        if (_built) return;
        _built = true;
        BuildMontage();
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
        if (_activityGrid.GetGlobalRect().HasPoint(mouse.Position)) return;
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

    private static Button AddInventoryNode(GridContainer grid, Texture2D? icon, Action action, bool activity = false)
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
        if (activity)
        {
            if (icon?.ResourcePath.EndsWith("timed_run.png", StringComparison.Ordinal) == true)
            {
                var motif = new ShaderMaterial { Shader = GD.Load<Shader>("res://Ui/Tower/activity_glass_motif.gdshader") };
                motif.SetShaderParameter("hourglass", true);
                motif.SetShaderParameter("silhouette", GD.Load<Texture2D>("res://Ui/Tower/Art/activities/hourglass_silhouette.svg"));
                image.Material = motif;
            }
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.SizeFlagsVertical = SizeFlags.Fill;
            button.CustomMinimumSize = new Vector2(48, 80);
            button.RemoveThemeStyleboxOverride("normal");
            button.RemoveThemeStyleboxOverride("focus");
            const float halfIcon = 21;
            image.OffsetLeft = image.OffsetTop = -halfIcon;
            image.OffsetRight = image.OffsetBottom = halfIcon;
            count.OffsetRight = count.OffsetBottom = -4;
            count.AddThemeConstantOverride("outline_size", 3);
            image.SetAnchorsPreset(LayoutPreset.TopLeft);
            count.SetAnchorsPreset(LayoutPreset.TopLeft);
            button.AddChild(new Control { Name = "SocketBounds", MouseFilter = MouseFilterEnum.Ignore });
            var caption = new Label { Name = "ActivityName", MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            caption.AddThemeFontSizeOverride("font_size", 10);
            caption.AutowrapMode = TextServer.AutowrapMode.Word;
            caption.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            button.AddChild(caption);
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
        "escort" => "Доведите союзника до цели, защищая его по пути.",
        "hold" => "Удерживайте позицию, пока шкала не заполнится.",
        "ritual" => "Активируйте точки ритуала и отбивайте вызванные волны.",
        "levers" => "Переключите механизмы этажа, чтобы открыть ворота мегаструктуры.",
        "clear" => "Истребите нужное число монстров на этаже.",
        "waves" => "Отбейте все волны монстров, прорывающихся из разлома.",
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
        const int capacity = 16;
        int pages = Math.Max(1, (groups.Count + capacity - 1) / capacity);
        int page = Math.Clamp(_activityPage.Selected, 0, pages - 1);
        if (_activityPage.ItemCount != pages)
        {
            _activityPage.Clear();
            for (int i = 0; i < pages; i++) _activityPage.AddItem($"{i + 1}/{pages}");
            _activityPage.Select(page);
        }
        _activityPage.Visible = pages > 1;
        for (int index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            string id = group.Id;
            if (!_inventoryNodes.TryGetValue(id, out var button))
            {
                button = AddInventoryNode(_activityGrid, TowerBoardArt.Activity(group.TypeId), () => SelectInventory(id), activity: true);
                button.Name = "Activity_" + id.Replace(':', '_');
                button.MouseEntered += _glassWindow.QueueRedraw;
                button.MouseExited += _glassWindow.QueueRedraw;
                button.FocusEntered += _glassWindow.QueueRedraw;
                button.FocusExited += _glassWindow.QueueRedraw;
                _activityGrid.MoveChild(button, _inventoryNodes.Count);
                _inventoryNodes.Add(id, button);
            }
            button.Visible = index / capacity == page;
            button.GetNode<Label>("Count").Text = group.Count.ToString();
            button.GetNode<Label>("ActivityName").Text = Data.FloorType(group.TypeId).Name;
            button.TooltipText = $"{Data.FloorType(group.TypeId).Name} · ранг {group.Rank} · {group.Count} шт.\n" + ActivityObjective(group.TypeId)
                + (group.Variant == null ? "" : "\nРазновидность: " + VariantName(group.Variant))
                + (group.EverModified ? "\nОтдельный экземпляр после намазки." : "")
                + string.Concat(group.Mods.Select(m => $"\n{ModText(m)} · тир {m.Tier}"));
            button.AccessibilityName = (id == _selectedInventoryId ? "Выбрано. " : "") + $"{Data.FloorType(group.TypeId).Name}, ранг {group.Rank}, {group.Count} шт.";
            button.AccessibilityDescription = button.TooltipText;
            button.ThemeTypeVariation = id == _selectedInventoryId ? "SelectedActivitySlot" : "ActivitySlot";
        }
        var selectedGroup = groups.FirstOrDefault(g => g.Id == _selectedInventoryId);
        int visibleGroups = Math.Min(capacity, groups.Count - page * capacity);
        int visibleCapacity = visibleGroups > 8 ? capacity : 8;
        int emptyCount = visibleCapacity - visibleGroups;
        _catalogueLattice.CustomMinimumSize = new Vector2(0, visibleCapacity > 8 ? 184 : 130);
        _floorSocket.CustomMinimumSize = Vector2.One * (visibleCapacity > 8 ? 48 : 86);
        _floorTitle.AddThemeFontSizeOverride("font_size", visibleCapacity > 8 ? 26 : 32);
        _portraitGap.CustomMinimumSize = new Vector2(0, visibleCapacity > 8 ? 0 : 14);
        _metaGap.CustomMinimumSize = new Vector2(0, visibleCapacity > 8 ? 0 : 8);
        _identityGap.CustomMinimumSize = new Vector2(0, visibleCapacity > 8 ? 0 : 10);
        while (_emptyActivitySlots.Count < emptyCount)
        {
            var empty = new Panel { ThemeTypeVariation = "EmptyActivitySlot", SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore };
            _activityGrid.AddChild(empty);
            _emptyActivitySlots.Add(empty);
        }
        for (int i = 0; i < _emptyActivitySlots.Count; i++) _emptyActivitySlots[i].Visible = i < emptyCount;
        _activityGrid.QueueSort();
        _glassWindow.QueueRedraw();
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
            if (_selectedInventoryId != null && _inventoryNodes.TryGetValue(_selectedInventoryId, out var selectedNode) && selectedNode.Visible) selectedNode.GrabFocus();
            else if (!_install.Disabled) _install.GrabFocus();
            else if (SelectedFloor is int floor) _floors[floor].GrabFocus();
            else _wholeTower.GrabFocus();
        }
    }

    private void ArrangeActivitySlots()
    {
        const int inset = 8;
        int rows = Math.Max(1, _activityGrid.GetChildren().OfType<Control>().Count(c => c.Visible) / _activityGrid.Columns);
        int columns = _activityGrid.Columns;
        var area = _activityGrid.Size - Vector2.One * (2 * inset);
        float width = (area.X - (columns - 1) * 6) / columns;
        float height = (area.Y - (rows - 1) * 8) / rows;
        float side = Mathf.Floor(Mathf.Min(48, Mathf.Min(width, height - 32)));
        if (side <= 0) return;
        int index = 0;
        foreach (Control slot in _activityGrid.GetChildren())
        {
            if (!slot.Visible) continue;
            var position = Vector2.One * inset + new Vector2(index % columns * (width + 6), index / columns * (height + 8));
            _activityGrid.FitChildInRect(slot, new Rect2(position.Round(), new Vector2(Mathf.Floor(width), Mathf.Floor(height))));
            if (slot.GetNodeOrNull<Control>("SocketBounds") is { } socket)
            {
                socket.Position = new Vector2(Mathf.Floor((width - side) / 2), 0);
                socket.Size = Vector2.One * side;
                var image = slot.GetChild<TextureRect>(0);
                image.Position = socket.Position + Vector2.One * side * .12f;
                image.Size = Vector2.One * side * .76f;
                var count = slot.GetNode<Label>("Count");
                count.Position = socket.Position + new Vector2(0, side - 19);
                count.Size = new Vector2(side - 2, 18);
                var caption = slot.GetNode<Label>("ActivityName");
                caption.Position = new Vector2(0, side + 4);
                caption.Size = new Vector2(Mathf.Floor(width), 30);
            }
            index++;
        }
        _glassWindow.QueueRedraw();
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
            _status.Show();
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
        _status.Text = result == TowerRefusal.None ? success : TowerTexts.Refusal(result);
        _status.Show();
        Refresh();
    }

    private void Refresh()
    {
        if (Data == null || Assembly == null || Wallet == null) return;
        _refreshing = true;
        RefreshInventory();
        var spec = Assembly.Spec;
        _wholeTower.Text = (SelectedFloor == null ? "▶ " : "") + "Вся башня · цель\n" +
            string.Join(" ", Enumerable.Range(0, Data.Rules.TowerSlots).Select(n => n < spec.TowerMods.Count ? "◆" : "◇")) +
            $" · {spec.TowerMods.Count}/{Data.Rules.TowerSlots}";
        var towerFrame = TowerBoardArt.Card(SelectedFloor == null);
        towerFrame.ContentMarginTop = towerFrame.ContentMarginBottom = 4;
        _wholeTower.AddThemeStyleboxOverride("normal", towerFrame);
        _wholeTower.AddThemeStyleboxOverride("hover", towerFrame);
        _rank.Select(spec.Rank - 1);
        var rule = Data.Rules.Ranks[spec.Rank - 1];
        _rank.TooltipText = $"Уровень зоны: {RankRules.ZoneLevel(Data.Rules, spec.Rank, TestCharacterLevel)} · тир до {rule.MaxTier}\n" +
            $"Множитель опыта: {RankRules.XpMultiplier(Data.Rules, spec.Rank, TestCharacterLevel):0.##}\n" +
            "Все ранги доступны для сборки. Проверка входа: настоящий прогресс, открыт только ранг 1.";
        _shuffle.Text = $"Перемешать · цена {Assembly.NextShuffleCost} · {Wallet.CountUsable("shuffle", spec.Rank)} шт.";
        _shuffle.AccessibilityName = _shuffle.Text;
        _shuffle.AccessibilityDescription = _shuffle.TooltipText;
        _bonus.Text = Assembly.ShuffleBonusActive ? $"Бонус перемешивания: +{Data.Rules.ShuffleBonus:P0}" : "Бонус перемешивания не активен";
        for (int i = 0; i < _floors.Count; i++)
        {
            var f = spec.Floors[i];
            var floorFrame = MontageSurface(SelectedFloor == i ? .92f : .60f);
            floorFrame.BgColor = SelectedFloor == i ? new Color(.12f, .20f, .24f, .94f) : new Color(.06f, .10f, .12f, .65f);
            floorFrame.BorderWidthTop = floorFrame.BorderWidthRight = 0;
            floorFrame.BorderWidthLeft = SelectedFloor == i ? 2 : 0;
            floorFrame.BorderWidthBottom = 1;
            floorFrame.BorderColor = SelectedFloor == i ? new Color("b7cbd0") : new Color("30434c");
            _floors[i].AddThemeStyleboxOverride("normal", floorFrame);
            _floors[i].AddThemeStyleboxOverride("hover", floorFrame);
            _floors[i].Icon = null;
            _floors[i].Text = "";
            ((TextureRect)_floors[i].FindChild("FloorMotif", true, false)!).Texture = f.TypeId == null ? null : TowerBoardArt.Activity(f.TypeId);
            _floors[i].GetNode<Label>("FloorActivity").Text = f.TypeId == null ? "Пустой этаж" : Data.FloorType(f.TypeId).Name;
            _floors[i].GetNode<Label>("FloorMetadata").Text =
                $"{(f.BiomeId == null ? "Без биома" : Data.Biomes.First(b => b.Id == f.BiomeId).Name)}  {f.Mods.Count}/{Data.Rules.FloorSlots}";
            _floors[i].AccessibilityName = $"Этаж {i+1}. {_floors[i].GetNode<Label>("FloorActivity").Text}. {_floors[i].GetNode<Label>("FloorMetadata").Text}";

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
        RefreshReferenceDetail();
    }

    private string ModText(ModInstance mod) => Data!.Mod(mod.ModId).Name.Replace("{v}", mod.Value.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")), StringComparison.Ordinal);

    private static Theme CreateTheme()
    {
        var theme = RoomHudTheme.ReferenceWindowTheme();
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
        theme.SetTypeVariation("ActivitySlot", "Button");
        theme.SetTypeVariation("SelectedActivitySlot", "ActivitySlot");
        theme.SetTypeVariation("ActivityLattice", "PanelContainer");
        var lead = Box("101a1db3", "303e40");
        lead.SetContentMarginAll(14);
        theme.SetStylebox("panel", "ActivityLattice", lead);
        theme.SetTypeVariation("EmptyActivitySlot", "Panel");
        theme.SetStylebox("panel", "EmptyActivitySlot", new StyleBoxEmpty());
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" })
            theme.SetStylebox(state, "ActivitySlot", new StyleBoxEmpty());
        theme.SetStylebox("panel", "PopupMenu", Box("10191d", "526a74"));
        theme.SetStylebox("hover", "PopupMenu", Box("293b42", "7099a8"));
        theme.SetStylebox("panel", "PanelContainer", new StyleBoxEmpty());
        return theme;
    }

    private static string VariantName(string id) => id switch
    { "escort" => "Сопровождение", "hold" => "Удержание", "ritual" => "Ритуал", "levers" => "Переключение", _ => id };
    private static string OperationText(CurrencyOp op) => op switch
    {
        CurrencyOp.AddMod => "Добавить случайное свойство к подходящей цели.",
        CurrencyOp.AddModBiased => "Добавить случайное свойство со смещением вероятности.",
        CurrencyOp.RerollOne => "Заменить одно случайное свойство.",
        CurrencyOp.RerollValueHigh => "Перебросить число одного свойства в верхней половине диапазона.",
        CurrencyOp.UpgradeTier => "Повысить тир случайного свойства.",
        CurrencyOp.Clear => "Удалить все свойства цели без возврата валюты.",
        CurrencyOp.RemoveOne => "Удалить выбранное выше свойство.",
        CurrencyOp.RerollVariant => "Сменить разновидность активности и снять несовместимые свойства.",
        _ => "Перемешать башню."
    };
}
