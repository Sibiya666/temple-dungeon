using Godot;

namespace Dialoid.Game.Ui.Art;

/// <summary>A texture-only layer. It never takes focus or mouse input and never extends its owner's rect.</summary>
public partial class RoomHudDecoration : Control
{
    public bool Solid { get; set; }
    /// <summary>Use a sibling backdrop for a Container: never insert decoration into its layout.</summary>
    public Control? Follow { get; set; }
    private StyleBoxTexture _frame = null!;
    private bool _focused;

    public RoomHudDecoration()
    {
        Name = "D2Decoration";
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Resized += QueueRedraw;
    }

    public override void _Ready() => _frame = Solid ? RoomHudTheme.Panel(0) : RoomHudTheme.Border();

    public override void _Process(double delta)
    {
        if (Follow == null) return;
        Position = GetParent<Control>().GetGlobalTransform().AffineInverse() * Follow.GlobalPosition;
        Size = Follow.Size;
        Visible = Follow.Visible;
        var focused = Follow is GameWindow { PadFocused: true };
        if (focused != _focused)
        {
            _focused = focused;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_frame == null || Size.X < 1 || Size.Y < 1) return;
        DrawStyleBox(_frame, new Rect2(Vector2.Zero, Size));
        if (_focused) DrawRect(new Rect2(Vector2.Zero, Size).Grow(-1), WindowHost.FocusedBorder, false, 2);
    }
}
