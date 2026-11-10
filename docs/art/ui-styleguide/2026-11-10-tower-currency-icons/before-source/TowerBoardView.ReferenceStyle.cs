using System.Linq;
using Dialoid.Core.Tower;
using Dialoid.Game.Ui.Art;
using Godot;
namespace Dialoid.Game.Ui.Tower;
public partial class TowerBoardView
{
    private TextureRect _referencePortrait = null!;
    private Label _referenceTitle = null!;
    private Label _referenceContext = null!;
    private void BuildReferenceStyle()
    {
        var shell = new Panel { Name = "ReferenceShell", MouseFilter = MouseFilterEnum.Ignore };
        var outline = RoomHudTheme.ReferenceSurface(0, border: 1);
        outline.BgColor = Colors.Transparent;
        shell.AddThemeStyleboxOverride("panel", outline);
        AddChild(shell);
        MoveChild(shell, 2);
        shell.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        shell.OffsetLeft = shell.OffsetTop = 8;
        shell.OffsetRight = shell.OffsetBottom = -8;
        _referencePortrait = new TextureRect { Name = "ActivityDetailPortrait",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore };
        _referenceTitle = RoomHudTheme.Heading("Выберите активность", 23);
        _referenceTitle.Name = "ActivityDetailTitle";
        _referenceTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var identity = new HBoxContainer();
        identity.AddThemeConstantOverride("separation", 12);
        identity.AddChild(PortraitSocket(_referencePortrait, 64));
        _referenceContext = new Label { Name = "ActivityDetailContext", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _referenceContext.AddThemeColorOverride("font_color", RoomHudTheme.MutedText);
        _referenceContext.AddThemeFontSizeOverride("font_size", 12);
        var identityText = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        identityText.AddThemeConstantOverride("separation", 4);
        identity.AddChild(identityText);
        identityText.AddChild(_referenceTitle);
        identityText.AddChild(_referenceContext);
        _sourceDetail.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14) });
        _sourceDetail.AddChild(identity);
    }
    private void RefreshReferenceDetail()
    {
        if (_referencePortrait == null || Assembly == null || Data == null) return;
        // Install source survives choosing a floor/whole-tower crafting target.
        var source = SelectedGroup();
        _referencePortrait.Texture = TowerBoardArt.Activity(source?.TypeId);
        _referenceTitle.Text = source == null ? "Выберите активность" : Data.FloorType(source.TypeId).Name;
        _referenceContext.Text = source == null ? $"Ранг {Assembly.Spec.Rank} · источник не выбран" :
            $"Ранг {source.Rank} · {source.Count} шт. · " + (source.EverModified ? "отдельный экземпляр" : "стек");
        _sourceObjective.Text = source == null ? "Выберите источник для установки или намазки." : ActivityObjective(source.TypeId);
        _sourceProperties.Text = source == null ? "" : $"Свойства источника · {source.Mods.Count}/{Data.Rules.FloorSlots}\n" +
            (source.Variant == null ? "" : VariantName(source.Variant) + "\n") +
            (source.Mods.Count == 0 ? "Свойств пока нет" : string.Join("\n", source.Mods.Select(m => $"{ModText(m)} · тир {m.Tier}")));
        var spec = Assembly.Spec;
        var floor = SelectedFloor is int slot ? spec.Floors[slot] : null;
        _floorTitle.Text = SelectedFloor is int index ? $"Этаж {index + 1}" : "Вся башня";
        _floorPortrait.Texture = floor == null ? TowerBoardArt.Symbol("tower") : TowerBoardArt.Activity(floor.TypeId);
        _floorIdentity.Text = floor == null ? "Свойства маршрута" : floor.TypeId == null ? "Активность не установлена" : Data.FloorType(floor.TypeId).Name;
        var mods = floor?.Mods ?? spec.TowerMods;
        int capacity = floor == null ? Data.Rules.TowerSlots : Data.Rules.FloorSlots;
        var quality = SelectedFloor is int q ? Rewards.FloorQuality(Data, spec, q, false, Assembly.ShuffleBonusActive) :
            Rewards.SummitQuality(Data, spec, false, Assembly.ShuffleBonusActive);
        _floorProperties.Text = $"Свойства {(floor == null ? "башни" : "этажа")} · {mods.Count}/{capacity} · Прогноз качества ×{quality.Total:0.##}\n" +
            (floor?.Variant == null ? "" : $"Разновидность: {VariantName(floor.Variant)}\n") +
            (mods.Count == 0 ? "Свойств пока нет" : string.Join("\n", mods.Select(m => $"{ModText(m)} · тир {m.Tier}")));
        _sourceRoute.Text = "Источник: " + (source == null ? "не выбран" : $"{Data.FloorType(source.TypeId).Name} · {source.Count} шт.") +
            (SelectedFloor is int destination ? $" → Этаж {destination + 1}" : " → выберите этаж для установки");
        _installReason.Text = SelectedFloor == null ? "Для установки выберите этаж." : source == null ?
            "Выберите экземпляр в каталоге." : floor?.TypeId == null ? "Один экземпляр займёт выбранный этаж." : "Предыдущая активность вернётся в каталог со своими свойствами.";
        _routeCount.Text = $"{spec.Floors.Count(f => f.TypeId != null)}/{spec.Floors.Count}";
        _floorMeta.Text = $"Свойства · {mods.Count}/{capacity}";
        _floorSlots.Text = string.Join("   ", Enumerable.Range(0, capacity).Select(n => n < mods.Count ? "◆" : "◇"));
        _currencyTarget.Text = "Цель: " + TargetName();
        _installReason.Visible = !_status.Visible;
        _installReason.TooltipText = _installReason.Text;
        _status.TooltipText = _status.Text;
        _fullStatus.Text = _status.Text;
        _fullStatus.Visible = _status.Visible;
        _floorTitle.TooltipText = _floorProperties.Text;
    }
}
