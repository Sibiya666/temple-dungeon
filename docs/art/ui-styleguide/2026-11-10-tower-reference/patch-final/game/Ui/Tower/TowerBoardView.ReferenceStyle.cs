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
        shell.AddThemeStyleboxOverride("panel", RoomHudTheme.ReferenceSurface(0, border: 2));
        AddChild(shell);
        MoveChild(shell, 1);
        shell.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        shell.OffsetLeft = shell.OffsetTop = 8;
        shell.OffsetRight = shell.OffsetBottom = -8;
        _referencePortrait = new TextureRect { Name = "ActivityDetailPortrait", CustomMinimumSize = new Vector2(40, 40),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore };
        _referenceTitle = RoomHudTheme.Heading("Активность", 20);
        _referenceTitle.Name = "ActivityDetailTitle";
        var identity = new HBoxContainer();
        identity.AddChild(_referencePortrait);
        identity.AddChild(_referenceTitle);
        _referenceContext = new Label { Name = "ActivityDetailContext", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _referenceContext.AddThemeColorOverride("font_color", RoomHudTheme.MutedText);
        _referenceContext.AddThemeFontSizeOverride("font_size", RoomHudTheme.CaptionSize);
        _right!.AddChild(identity);
        _right.MoveChild(identity, 1);
        _right.AddChild(_referenceContext);
        _right.MoveChild(_referenceContext, 2);
        _right.MoveChild(_details, 3);
        _details.AddThemeColorOverride("font_color", RoomHudTheme.MutedText);
        _details.AddThemeFontSizeOverride("font_size", RoomHudTheme.CaptionSize);
    }
    private void RefreshReferenceDetail()
    {
        if (_referencePortrait == null || Assembly == null || Data == null) return;
        var inventory = _targetInventory ? SelectedGroup() : null;
        string? type = inventory?.TypeId ?? (SelectedFloor is int slot ? Assembly.Spec.Floors[slot].TypeId : null);
        _referencePortrait.Texture = TowerBoardArt.Activity(type);
        _referenceTitle.Text = type == null ? SelectedFloor == null ? "Вся башня" : "Пустой этаж" : Data.FloorType(type).Name;
        _referenceContext.Text = $"Ранг {Assembly.Spec.Rank} · " + (inventory != null ? "Активность инвентаря" : SelectedFloor == null ? "Маршрут целиком" : $"Этаж {SelectedFloor + 1}");
    }
}