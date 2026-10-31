using Dialoid.Sandbox;
using Godot;
using Dialoid.Game.Ui.Art;

namespace Dialoid.Game.Ui;

/// <summary>
/// The bar over the room (spec §4): one toggle button per window, captioned with its title and, smaller and dimmer, its key («Пассивки P»), and the dummy preset
/// list from data/dummies.json (choosing one restarts the fight on that preset). The buttons never take keyboard focus, so the
/// keys keep going to the fight after a click.
/// </summary>
public partial class TopBar : PanelContainer
{
    public const float Height = 40;

    private readonly Dictionary<string, Button> _buttons = new();
    private readonly Dictionary<Button, Label> _keys = new();
    private readonly List<string> _dummyIds = new();
    private WindowHost _host = null!;
    private Func<string>? _currentDummy;
    private Action<string>? _selectDummy;

    /// <summary>The window buttons by window id.</summary>
    public IReadOnlyDictionary<string, Button> Buttons => _buttons;

    /// <summary>The dummy preset list; <see cref="DummyIds"/> is its order.</summary>
    public OptionButton DummyOptions { get; private set; } = null!;
    public IReadOnlyList<string> DummyIds => _dummyIds;

    /// <summary>Fills the bar: buttons for the windows <paramref name="host"/> has, then the dummy list (when a fight exists).</summary>
    public void Setup(WindowHost host, SandboxSession? session, Func<string>? currentDummy, Action<string>? selectDummy)
    {
        _host = host;
        _currentDummy = currentDummy;
        _selectDummy = selectDummy;
        Name = "TopBar";
        // Only the gold focus frame of the windows' theme (the buttons keep their look); the gamepad reaches the bar by Menu.
        var theme = new Theme();
        WindowHost.AddFocusStyles(theme);
        Theme = theme;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(0, Height);
        var style = new StyleBoxFlat { BgColor = new Color(0.075f, 0.07f, 0.065f, 1f), BorderColor = WindowHost.Border, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4 };
        style.BorderWidthBottom = 1;
        AddThemeStyleboxOverride("panel", style);

        var row = new HBoxContainer { Name = "Row" };
        row.AddThemeConstantOverride("separation", 4);
        AddChild(row);
        foreach (var spec in WindowHost.Specs)
        {
            if (!host.Has(spec.Id)) continue;
            // Build windows left of the separator, analysis windows (log, meter, charts) right of it.
            if (spec.Id == WindowIds.Log) row.AddChild(new VSeparator { Name = "AnalysisSeparator" });
            var button = new Button
            {
                Name = "Open_" + spec.Id,
                Text = spec.Title,
                ToggleMode = true,
                FocusMode = FocusModeEnum.None,
                TooltipText = $"Открыть или закрыть окно «{spec.Title}» — клавиша {GameWindow.KeyName(spec.Key)}",
            };
            var id = spec.Id;
            GameIcons.Decorate(button, id, 14);
            button.Pressed += () => host.ToggleWindow(id);
            AddKey(button, GameWindow.KeyName(spec.Key));
            _buttons[id] = button;
            row.AddChild(button);
        }

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        if (session != null && currentDummy != null)
        {
            row.AddChild(new Label { Text = "Манекен:", VerticalAlignment = VerticalAlignment.Center });
            DummyOptions = new OptionButton { Name = "Dummy", FocusMode = FocusModeEnum.None, TooltipText = "Защита и здоровье манекена; смена начинает бой заново" };
            foreach (var (dummyId, dummy) in session.Data.Dummies)
            {
                _dummyIds.Add(dummyId);
                DummyOptions.AddItem(dummy.Name);
            }
            DummyOptions.ItemSelected += index =>
            {
                if (index >= 0 && index < _dummyIds.Count) _selectDummy?.Invoke(_dummyIds[(int)index]);
            };
            row.AddChild(DummyOptions);
        }
        host.Changed += Sync;
        Sync();
        if (Dialoid.Game.Input.InputDevice.Instance is { } device)
        {
            device.Changed += kind => ShowDevice(kind == Dialoid.Game.Input.InputKind.Gamepad);
            ShowDevice(device.Active == Dialoid.Game.Input.InputKind.Gamepad);
        }
    }

    /// <summary>The key after the title, smaller and dimmer («Персонаж C»), in room the button keeps at its right; padding 4 px.</summary>
    private void AddKey(Button button, string key)
    {
        var label = new Label { Name = "Key", MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", KeySize);
        label.AddThemeColorOverride("font_color", new Color(0.62f, 0.6f, 0.56f)); // about 60 % of the title's brightness
        button.AddChild(label);
        _keys[button] = label;
        SetKey(button, key);
    }

    private const int KeySize = 13;

    /// <summary>The caption at the button's right («P» on the keyboard, «[←]» or nothing on the gamepad) and the room kept for it.</summary>
    private void SetKey(Button button, string key)
    {
        var label = _keys[button];
        var keyWidth = key.Length == 0 ? 0 : ThemeDB.FallbackFont.GetStringSize(key, HorizontalAlignment.Left, -1, KeySize).X;
        foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
        {
            if (button.GetThemeStylebox(state, "Button")?.Duplicate() is not StyleBox box) continue;
            box.ContentMarginLeft = KeyPadding;
            box.ContentMarginRight = KeyPadding + (keyWidth > 0 ? 4 + keyWidth : 0);
            button.AddThemeStyleboxOverride(state, box);
        }
        label.Text = key;
        label.SetAnchorsAndOffsetsPreset(LayoutPreset.RightWide);
        label.OffsetLeft = -(KeyPadding + keyWidth);
        label.OffsetRight = -KeyPadding;
    }

    /// <summary>The captions follow the device: keys on the keyboard, D-pad glyphs (or nothing: those windows open from this bar) on the gamepad.</summary>
    public void ShowDevice(bool gamepad)
    {
        foreach (var (id, button) in _buttons)
        {
            var spec = WindowHost.SpecOf(id);
            var dpad = Dialoid.Game.Input.GamepadLayout.DpadOf(id);
            SetKey(button, !gamepad ? GameWindow.KeyName(spec.Key) : dpad is { } b ? ButtonPrompts.Glyph(b) : "");
            button.TooltipText = gamepad
                ? $"Окно «{spec.Title}»: A — открыть или перейти в него" + (dpad is { } d ? $", из боя — {ButtonPrompts.Glyph(d)}" : "")
                : $"Открыть или закрыть окно «{spec.Title}» — клавиша {GameWindow.KeyName(spec.Key)}";
        }
    }

    /// <summary>The window a top-bar button opens, or null for another control.</summary>
    public string? WindowOf(Control control)
    {
        foreach (var (id, button) in _buttons)
            if (button == control) return id;
        return null;
    }

    /// <summary>Inner padding of a window button, pixels.</summary>
    public const int KeyPadding = 4;

    /// <summary>The key caption of a window button.</summary>
    public Label KeyLabel(string windowId) => _keys[_buttons[windowId]];

    /// <summary>Chooses a preset as the list does.</summary>
    public void SelectDummy(string dummyId)
    {
        var index = _dummyIds.IndexOf(dummyId);
        if (index < 0) throw new ArgumentException($"Unknown dummy {dummyId}", nameof(dummyId));
        DummyOptions.Selected = index;
        _selectDummy?.Invoke(dummyId);
    }

    /// <summary>Pressed state of the buttons follows the open windows.</summary>
    public void Sync()
    {
        foreach (var (id, button) in _buttons) button.SetPressedNoSignal(_host.IsOpen(id));
    }

    public override void _Process(double delta)
    {
        // The fight may change its preset elsewhere (the self-test, a later window): the list follows.
        if (DummyOptions == null || _currentDummy == null) return;
        var index = _dummyIds.IndexOf(_currentDummy());
        if (index >= 0 && DummyOptions.Selected != index) DummyOptions.Selected = index;
    }
}
