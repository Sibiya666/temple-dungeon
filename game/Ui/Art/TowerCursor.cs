using Godot;

namespace Dialoid.Game.Ui.Art;

/// <summary>Native pointer art; text editing and resize cursors retain their system shapes.</summary>
public partial class TowerCursor : Node
{
    public const string GlassId = "glass";
    public const string SpearId = "spear";
    public const string KeyId = "key";
    public const string DefaultId = GlassId;
    public static IReadOnlyList<string> Designs { get; } = Array.AsReadOnly(new[] { GlassId, SpearId, KeyId });
    public string SettingsPath { get; set; } = "user://cursor.cfg";
    public string SelectedId { get; private set; } = DefaultId;
    public Vector2 Hotspot => HotspotFor(SelectedId);
    public Vector2 HandHotspot => Hotspot;
    public Texture2D? Pointer { get; private set; }
    public Texture2D? Hand => Pointer;
    public bool NativeInstalled { get; private set; }
    private SceneTree? _tree;
    private Label? _toast;
    private double _toastRemaining;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
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
        SetProcess(false);
        Select(LoadSelection(SettingsPath), persist: false, showToast: false);
    }

    public static string NormalizeId(string? id) => id is GlassId or SpearId or KeyId ? id : DefaultId;

    public static Vector2 HotspotFor(string id) => NormalizeId(id) == SpearId ? new Vector2(6, 3) : new Vector2(2, 2);

    public static string LoadSelection(string path)
    {
        if (!Godot.FileAccess.FileExists(path)) return DefaultId;
        using var config = new ConfigFile();
        if (config.Load(path) != Error.Ok) return DefaultId;
        var value = config.GetValue("cursor", "design", DefaultId);
        return value.VariantType == Variant.Type.String ? NormalizeId(value.AsString()) : DefaultId;
    }

    public static Error SaveSelection(string path, string? id)
    {
        using var config = new ConfigFile();
        config.SetValue("cursor", "design", NormalizeId(id));
        return config.Save(path);
    }

    public Texture2D? TextureFor(string id)
    {
        id = NormalizeId(id);
        if (_textures.TryGetValue(id, out var cached)) return cached;
        var texture = GD.Load<Texture2D>($"res://Ui/Art/Cursors/{id}.png");
        if (texture != null && texture.GetSize() == new Vector2(48, 48)) _textures[id] = texture;
        return texture;
    }

    public bool Select(string? id, bool persist = true, bool showToast = true)
    {
        var wanted = NormalizeId(id);
        var texture = TextureFor(wanted);
        if (texture == null || texture.GetSize() != new Vector2(48, 48))
        {
            GD.PushError("Tower cursor texture is missing or has an unexpected size; system cursor retained.");
            return false;
        }
        SelectedId = wanted;
        Pointer = texture;
        if (DisplayServer.GetName() != "headless")
        {
            Godot.Input.SetCustomMouseCursor(texture, Godot.Input.CursorShape.Arrow, Hotspot);
            Godot.Input.SetCustomMouseCursor(texture, Godot.Input.CursorShape.PointingHand, Hotspot);
            NativeInstalled = true;
        }
        if (persist && SaveSelection(SettingsPath, wanted) != Error.Ok)
            GD.PushWarning("Cursor preference could not be saved.");
        if (showToast) ShowToast();
        return wanted == id;
    }

    public void Cycle(bool persist = true, bool showToast = true)
    {
        var next = SelectedId switch { GlassId => SpearId, SpearId => KeyId, _ => GlassId };
        Select(next, persist, showToast);
    }

    public static bool IsCycleKey(InputEvent input, bool editingText, bool popupOpen) =>
        !editingText && !popupOpen && input is InputEventKey { Pressed: true, Echo: false } key &&
        (key.Keycode == Godot.Key.F8 || key.PhysicalKeycode == Godot.Key.F8) &&
        !key.CtrlPressed && !key.AltPressed && !key.ShiftPressed && !key.MetaPressed;

    public bool CanCycle(InputEvent input) => IsCycleKey(input,
        GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox,
        HasVisiblePopup(GetTree().Root));

    private static bool HasVisiblePopup(Node node)
    {
        if (node is Popup popup && popup.Visible) return true;
        foreach (var child in node.GetChildren(includeInternal: true)) if (HasVisiblePopup(child)) return true;
        return false;
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (!CanCycle(input)) return;
        Cycle();
        GetViewport().SetInputAsHandled();
    }

    private void ShowToast()
    {
        if (_toast == null)
        {
            var layer = new CanvasLayer { Layer = 110 };
            AddChild(layer);
            _toast = new Label
            {
                Name = "CursorToast",
                AnchorRight = 1, OffsetTop = 52, OffsetBottom = 82,
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore, FocusMode = Control.FocusModeEnum.None,
            };
            _toast.AddThemeColorOverride("font_color", new Color("d8d5c6"));
            _toast.AddThemeColorOverride("font_outline_color", new Color("10161b"));
            _toast.AddThemeConstantOverride("outline_size", 5);
            layer.AddChild(_toast);
        }
        _toast.Text = "Курсор: " + (SelectedId switch { GlassId => "Витраж", SpearId => "Наконечник", _ => "Ключ" }) + " · F8";
        _toast.Show();
        _toastRemaining = 2;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        _toastRemaining -= delta;
        if (_toastRemaining > 0) return;
        _toast?.Hide();
        SetProcess(false);
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
