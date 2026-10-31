using Godot;

namespace Dialoid.Game.Ui.Art;

/// <summary>Native pointer art; text editing and resize cursors retain their system shapes.</summary>
public partial class TowerCursor : Node
{
    public const string TexturePath = "res://Ui/Art/Cursors/tower-sword.svg";
    public const string HandTexturePath = "res://Ui/Art/Cursors/tower-hand.svg";
    public static readonly Vector2 Hotspot = new(3, 3);
    public static readonly Vector2 HandHotspot = new(12, 3);
    public Texture2D? Pointer { get; private set; }
    public Texture2D? Hand { get; private set; }
    public bool NativeInstalled { get; private set; }
    private SceneTree? _tree;
    private readonly Dictionary<BaseButton, ButtonBinding> _buttons = new();

    private sealed class ButtonBinding
    {
        public required Action Entered { get; init; }
        public required Action Exited { get; init; }
        public Control.CursorShape LastAssigned { get; set; }
    }

    public override void _Ready()
    {
        _tree = GetTree();
        _tree.NodeAdded += OnNodeAdded;
        VisitExisting(_tree.Root);
        Pointer = GD.Load<Texture2D>(TexturePath);
        Hand = GD.Load<Texture2D>(HandTexturePath);
        if (Pointer == null || Hand == null || Pointer.GetSize() != new Vector2(40, 40) || Hand.GetSize() != new Vector2(40, 40))
        {
            GD.PushError("Tower cursor texture is missing or has an unexpected size; system cursor retained.");
            return;
        }
        if (DisplayServer.GetName() == "headless") return;
        Godot.Input.SetCustomMouseCursor(Pointer, Godot.Input.CursorShape.Arrow, Hotspot);
        Godot.Input.SetCustomMouseCursor(Hand, Godot.Input.CursorShape.PointingHand, HandHotspot);
        NativeInstalled = true;
    }

    private void VisitExisting(Node node)
    {
        if (node is BaseButton button) RegisterButton(button);
        foreach (var child in node.GetChildren()) VisitExisting(child);
    }

    private void OnNodeAdded(Node node)
    {
        if (node is BaseButton button)
            Callable.From(() => RegisterButton(button)).CallDeferred();
    }

    private void RegisterButton(BaseButton button)
    {
        if (!GodotObject.IsInstanceValid(button) || !button.IsInsideTree() || _buttons.ContainsKey(button)
            || button.MouseDefaultCursorShape != Control.CursorShape.Arrow) return;
        var binding = new ButtonBinding
        {
            Entered = () => RefreshButton(button),
            Exited = () => RemoveButton(button),
            LastAssigned = button.Disabled ? Control.CursorShape.Arrow : Control.CursorShape.PointingHand,
        };
        _buttons.Add(button, binding);
        button.MouseDefaultCursorShape = binding.LastAssigned;
        button.MouseEntered += binding.Entered;
        button.TreeExiting += binding.Exited;
    }

    private void RefreshButton(BaseButton button)
    {
        if (!_buttons.TryGetValue(button, out var binding)) return;
        // A component that explicitly changes its cursor takes ownership back.
        if (button.MouseDefaultCursorShape != binding.LastAssigned)
        {
            RemoveButton(button);
            return;
        }
        binding.LastAssigned = button.Disabled ? Control.CursorShape.Arrow : Control.CursorShape.PointingHand;
        button.MouseDefaultCursorShape = binding.LastAssigned;
    }

    private void RemoveButton(BaseButton button)
    {
        if (!_buttons.Remove(button, out var binding) || !GodotObject.IsInstanceValid(button)) return;
        button.MouseEntered -= binding.Entered;
        button.TreeExiting -= binding.Exited;
        if (button.MouseDefaultCursorShape == binding.LastAssigned)
            button.MouseDefaultCursorShape = Control.CursorShape.Arrow;
    }

    public override void _ExitTree()
    {
        if (_tree != null) _tree.NodeAdded -= OnNodeAdded;
        foreach (var button in _buttons.Keys.ToArray()) RemoveButton(button);
        _tree = null;
        if (!NativeInstalled) return;
        Godot.Input.SetCustomMouseCursor(null, Godot.Input.CursorShape.Arrow);
        Godot.Input.SetCustomMouseCursor(null, Godot.Input.CursorShape.PointingHand);
        NativeInstalled = false;
    }
}
