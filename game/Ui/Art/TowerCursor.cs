using Godot;

namespace Dialoid.Game.Ui.Art;

/// <summary>Native pointer art; text editing and resize cursors retain their system shapes.</summary>
public partial class TowerCursor : Node
{
    public const string TexturePath = "res://Ui/Art/Cursors/tower-pointer.svg";
    public static readonly Vector2 Hotspot = new(3, 3);
    public Texture2D? Pointer { get; private set; }
    public bool NativeInstalled { get; private set; }

    public override void _Ready()
    {
        Pointer = GD.Load<Texture2D>(TexturePath);
        if (Pointer == null || Pointer.GetSize() != new Vector2(40, 40))
        {
            GD.PushError("Tower cursor texture is missing or has an unexpected size; system cursor retained.");
            return;
        }
        if (DisplayServer.GetName() == "headless") return;
        Godot.Input.SetCustomMouseCursor(Pointer, Godot.Input.CursorShape.Arrow, Hotspot);
        Godot.Input.SetCustomMouseCursor(Pointer, Godot.Input.CursorShape.PointingHand, Hotspot);
        NativeInstalled = true;
    }

    public override void _ExitTree()
    {
        if (!NativeInstalled) return;
        Godot.Input.SetCustomMouseCursor(null, Godot.Input.CursorShape.Arrow);
        Godot.Input.SetCustomMouseCursor(null, Godot.Input.CursorShape.PointingHand);
        NativeInstalled = false;
    }
}
