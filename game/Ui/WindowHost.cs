using Godot;

namespace Dialoid.Game.Ui;

/// <summary>Ids of the windows over the room (spec §4 «Окна поверх игры»).</summary>
public static class WindowIds
{
    public const string Character = "character";
    public const string Passives = "passives";
    public const string Ascendancy = "ascendancy";
    public const string Skills = "skills";
    public const string Builds = "builds";
    public const string Breakdown = "breakdown";
    public const string Simulation = "simulation";
    public const string Log = "log";
    public const string Meter = "meter";
    public const string Charts = "charts";
}

/// <summary>Which edge a window opens at, so it never covers the hero in the centre (spec §4 «Где окна»).</summary>
public enum WindowEdge { Left, Right, LeftBottom, RightTop }

/// <summary>
/// One window: its id, title, hotkey (a physical key: the same key on the Russian layout), edge and default size in pixels
/// (0 height: the full height between the top bar and the combat screen).
/// </summary>
public sealed record WindowSpec(string Id, string Title, Key Key, WindowEdge Edge, float Width, float Height = 0);

/// <summary>
/// The overlay windows of the training room (plan 4 task 10): each holds one existing panel (the panels are not rewritten).
/// Hotkeys toggle them (<see cref="Specs"/>; D is the step right, so the meter is T), Esc closes the top one. A window opens at
/// its edge with an opaque background, can be dragged by its title and resized by its corner; where the owner left it is kept in
/// <see cref="ConfigPath"/>. Input: no hotkey works while a text field has focus; Enter and Esc take the focus off a text field;
/// a window stops every mouse event over it, so clicks, the wheel and drags over a window never reach the fight or the camera
/// (they listen to unhandled input only).
/// </summary>
public partial class WindowHost : Control
{
    /// <summary>Every window in top-bar order with its hotkey (spec §4 and the plan's task 10).</summary>
    public static readonly IReadOnlyList<WindowSpec> Specs =
    [
        new(WindowIds.Character, "Персонаж", Key.C, WindowEdge.Left, 470),
        new(WindowIds.Passives, "Пассивки", Key.P, WindowEdge.Left, 720),
        new(WindowIds.Ascendancy, "Асцендант", Key.N, WindowEdge.Left, 640),
        new(WindowIds.Skills, "Скиллы", Key.K, WindowEdge.Right, 660),
        new(WindowIds.Builds, "Билды", Key.B, WindowEdge.Right, 560),
        new(WindowIds.Breakdown, "Разбор", Key.R, WindowEdge.Right, 540),
        new(WindowIds.Simulation, "Симуляция", Key.M, WindowEdge.Right, 640),
        new(WindowIds.Log, "Журнал", Key.L, WindowEdge.LeftBottom, 560, 280),
        new(WindowIds.Meter, "Измеритель", Key.T, WindowEdge.RightTop, 420, 380),
        new(WindowIds.Charts, "Графики", Key.G, WindowEdge.Right, 660, 460),
    ];

    /// <summary>The widest a side window opens, as a share of the screen width (owner's ruling: about 530 px at 1600).</summary>
    public const float MaxSideFraction = 0.33f;

    /// <summary>Gap between a window and the screen edge, pixels.</summary>
    public const float Margin = 8;

    /// <summary>Smallest window a corner drag can make.</summary>
    public static readonly Vector2 MinSize = new(260, 160);

    /// <summary>Background of every window: opaque, warm dark (text over the dark scene stays readable).</summary>
    public static readonly Color Background = new(0.105f, 0.1f, 0.095f, 1f);
    public static readonly Color TitleBackground = new(0.17f, 0.145f, 0.115f, 1f);
    public static readonly Color Border = new(0.47f, 0.37f, 0.24f, 1f);

    /// <summary>Where window positions and sizes are kept between runs.</summary>
    public string ConfigPath { get; set; } = "user://windows.cfg";

    /// <summary>Pixels kept free at the top (the top bar) and at the bottom (the combat screen) for full-height windows.</summary>
    public Func<float> TopInset { get; set; } = () => 0;
    public Func<float> BottomInset { get; set; } = () => 0;

    /// <summary>True while a window's text field has focus (keys go to the field, not to the fight or the hotkeys).</summary>
    public static bool TextFieldFocused(Viewport viewport) => viewport.GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox;

    private readonly Dictionary<string, GameWindow> _windows = new();
    private readonly List<string> _order = new(); // open windows, bottom to top
    private ConfigFile _config = new();

    /// <summary>Raised after a window opened or closed (the top bar follows).</summary>
    public event Action? Changed;

    /// <summary>Raised when a closed window opens (its panel may refit to the new size).</summary>
    public event Action<string>? Opened;

    public WindowHost()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        LoadConfig();
        Resized += ClampAll; // the host follows the screen (and the self-test's headless root size)
        Theme = WindowTheme();
    }

    /// <summary>
    /// The windows' theme: a plain button gets a visible raised face and border (on the dark window the default face blended in,
    /// so «Вписать дерево» read as text). Flat buttons (every NumberLabel) stay flat; other controls keep the default theme.
    /// </summary>
    public static Theme WindowTheme()
    {
        var theme = new Theme();
        StyleBoxFlat Face(Color bg, Color border)
        {
            var box = new StyleBoxFlat { BgColor = bg, BorderColor = border, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 4, ContentMarginBottom = 4 };
            box.SetBorderWidthAll(1);
            box.SetCornerRadiusAll(3);
            return box;
        }
        theme.SetStylebox("normal", "Button", Face(new Color(0.24f, 0.2f, 0.16f), Border));
        theme.SetStylebox("hover", "Button", Face(new Color(0.31f, 0.26f, 0.2f), new Color(0.7f, 0.56f, 0.36f)));
        theme.SetStylebox("pressed", "Button", Face(new Color(0.17f, 0.14f, 0.11f), new Color(0.7f, 0.56f, 0.36f)));
        theme.SetStylebox("hover_pressed", "Button", Face(new Color(0.2f, 0.17f, 0.13f), new Color(0.7f, 0.56f, 0.36f)));
        theme.SetStylebox("disabled", "Button", Face(new Color(0.15f, 0.14f, 0.13f), new Color(0.3f, 0.27f, 0.24f)));
        // Check boxes would inherit the face from Button: they keep their plain look.
        foreach (var type in new[] { "CheckBox", "CheckButton" })
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
                theme.SetStylebox(state, type, new StyleBoxEmpty { ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 2, ContentMarginBottom = 2 });
        return theme;
    }

    /// <summary>Re-reads <see cref="ConfigPath"/> (the self-test points it to its own file first).</summary>
    public void LoadConfig()
    {
        _config = new ConfigFile();
        if (Godot.FileAccess.FileExists(ConfigPath) && _config.Load(ConfigPath) != Error.Ok)
            _config = new ConfigFile(); // a broken file: the default edges, overwritten at the next drag
    }

    /// <summary>Adds the window <paramref name="id"/> holding <paramref name="content"/> (hidden until opened).</summary>
    public GameWindow Add(string id, Control content)
    {
        var spec = SpecOf(id);
        var window = new GameWindow(spec, content) { Name = "Window_" + id, Visible = false };
        window.CloseRequested += () => CloseWindow(id);
        window.Moved += () => Remember(window);
        // A panel whose content grows (a longer breakdown) must not push its window off the screen.
        window.Resized += () => { if (window.Visible) Clamp(window); };
        _windows[id] = window;
        AddChild(window);
        return window;
    }

    public static WindowSpec SpecOf(string id) => Specs.FirstOrDefault(s => s.Id == id) ?? throw new ArgumentException($"Unknown window {id}", nameof(id));

    public IReadOnlyDictionary<string, GameWindow> Windows => _windows;

    public GameWindow Window(string id) => _windows[id];

    public bool Has(string id) => _windows.ContainsKey(id);

    public bool IsOpen(string id) => _windows.TryGetValue(id, out var w) && w.Visible;

    /// <summary>Open windows, bottom to top.</summary>
    public IReadOnlyList<string> OpenWindows => _order;

    /// <summary>The window on top, or null when none is open.</summary>
    public string? TopWindow => _order.Count > 0 ? _order[^1] : null;

    /// <summary>Opens the window at its remembered place (or its edge) and raises it.</summary>
    public void OpenWindow(string id)
    {
        if (!_windows.TryGetValue(id, out var window)) throw new ArgumentException($"Unknown window {id}", nameof(id));
        if (!window.Visible)
        {
            Place(window);
            window.Visible = true;
            Opened?.Invoke(id);
        }
        Raise(id);
        Changed?.Invoke();
    }

    public void CloseWindow(string id)
    {
        if (!_windows.TryGetValue(id, out var window) || !window.Visible) return;
        // A text field of the closing window must not keep the keys.
        if (GetViewport().GuiGetFocusOwner() is { } focus && window.IsAncestorOf(focus)) focus.ReleaseFocus();
        window.Visible = false;
        _order.Remove(id);
        Changed?.Invoke();
    }

    public void ToggleWindow(string id)
    {
        if (IsOpen(id)) CloseWindow(id);
        else OpenWindow(id);
    }

    public void CloseAll()
    {
        foreach (var id in _order.ToList()) CloseWindow(id);
    }

    /// <summary>Brings an open window to the top.</summary>
    public void Raise(string id)
    {
        var window = _windows[id];
        _order.Remove(id);
        _order.Add(id);
        MoveChild(window, GetChildCount() - 1);
    }

    /// <summary>The rect a window gets when nothing is remembered: its edge, its default size, inside the screen.</summary>
    public Rect2 DefaultRect(WindowSpec spec)
    {
        var screen = Size;
        var top = TopInset() + Margin;
        var bottom = screen.Y - BottomInset() - Margin;
        // A side window takes at most a third of the screen, so the hero and the dummy next to him stay in sight.
        var width = Mathf.Min(Mathf.Min(spec.Width, screen.X * MaxSideFraction), screen.X - 2 * Margin);
        var fullHeight = Mathf.Max(MinSize.Y, bottom - top);
        var height = spec.Height > 0 ? Mathf.Min(spec.Height, fullHeight) : fullHeight;
        var x = spec.Edge is WindowEdge.Left or WindowEdge.LeftBottom ? Margin : screen.X - Margin - width;
        var y = spec.Edge == WindowEdge.LeftBottom ? screen.Y - Margin - height : top;
        return new Rect2(x, y, width, height);
    }

    /// <summary>The window at its remembered place and size, or at its default one; always inside the screen.</summary>
    private void Place(GameWindow window)
    {
        var rect = DefaultRect(window.Spec);
        if (_config.HasSectionKey(window.Spec.Id, "position") && _config.GetValue(window.Spec.Id, "position").Obj is Vector2 pos)
            rect.Position = pos;
        if (_config.HasSectionKey(window.Spec.Id, "size") && _config.GetValue(window.Spec.Id, "size").Obj is Vector2 size)
            rect.Size = size;
        window.Position = rect.Position;
        window.Size = rect.Size;
        Clamp(window);
    }

    private void ClampAll()
    {
        foreach (var window in _windows.Values)
            if (window.Visible) Clamp(window);
    }

    /// <summary>Keeps the window inside the screen and below the top bar.</summary>
    public void Clamp(GameWindow window)
    {
        var screen = Size;
        if (screen.X < 1 || screen.Y < 1) return;
        var top = TopInset();
        var size = new Vector2(Mathf.Clamp(window.Size.X, MinSize.X, Mathf.Max(MinSize.X, screen.X)),
            Mathf.Clamp(window.Size.Y, MinSize.Y, Mathf.Max(MinSize.Y, screen.Y - top)));
        window.Size = size;
        size = window.Size; // read back: a panel's minimum size may have kept it larger
        window.Position = new Vector2(Mathf.Clamp(window.Position.X, 0, Mathf.Max(0, screen.X - size.X)),
            Mathf.Clamp(window.Position.Y, top, Mathf.Max(top, screen.Y - size.Y)));
    }

    /// <summary>Writes where the owner left the window.</summary>
    private void Remember(GameWindow window)
    {
        Clamp(window);
        _config.SetValue(window.Spec.Id, "position", window.Position);
        _config.SetValue(window.Spec.Id, "size", window.Size);
        _config.Save(ConfigPath);
    }

    /// <summary>Moves a window as a title drag does and remembers it.</summary>
    public void MoveWindow(string id, Vector2 position)
    {
        var window = _windows[id];
        window.Position = position;
        Remember(window);
    }

    /// <summary>The remembered position of a window, or null.</summary>
    public Vector2? RememberedPosition(string id) =>
        _config.HasSectionKey(id, "position") && _config.GetValue(id, "position").Obj is Vector2 p ? p : null;

    /// <summary>The open window under a screen point (the topmost), or null.</summary>
    public string? WindowAt(Vector2 point)
    {
        for (int i = _order.Count - 1; i >= 0; i--)
            if (_windows[_order[i]].GetGlobalRect().HasPoint(point)) return _order[i];
        return null;
    }

    // ---------- input ----------

    public override void _Input(InputEvent e)
    {
        switch (e)
        {
            // A press on a window raises it (its own controls still get the press).
            case InputEventMouseButton { Pressed: true } mb when WindowAt(mb.Position) is { } id && id != TopWindow:
                Raise(id);
                break;
            case InputEventKey { Pressed: true, Echo: false } key when TextFieldFocused(GetViewport()):
            {
                var code = CodeOf(key);
                var focus = GetViewport().GuiGetFocusOwner();
                if (code == Key.Escape)
                {
                    focus?.ReleaseFocus();
                    GetViewport().SetInputAsHandled();
                }
                // Enter submits first (LineEdit text_submitted, SpinBox value), then the focus goes; a multi-line field keeps Enter.
                else if (code is Key.Enter or Key.KpEnter && focus is LineEdit)
                    Callable.From(() => { if (IsInstanceValid(focus) && focus.HasFocus()) focus.ReleaseFocus(); }).CallDeferred();
                break;
            }
        }
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey key && HandleKey(key)) GetViewport().SetInputAsHandled();
    }

    /// <summary>A window hotkey or Esc; true when used. Ignored while a text field has focus (spec §4 «Ввод»).</summary>
    public bool HandleKey(InputEventKey key)
    {
        if (!key.Pressed || key.Echo || key.CtrlPressed || key.AltPressed || key.MetaPressed) return false;
        if (TextFieldFocused(GetViewport())) return false;
        var code = CodeOf(key);
        if (code == Key.Escape)
        {
            if (TopWindow is not { } top) return false;
            CloseWindow(top);
            return true;
        }
        var spec = Specs.FirstOrDefault(s => s.Key == code);
        if (spec == null || !_windows.ContainsKey(spec.Id)) return false;
        ToggleWindow(spec.Id);
        return true;
    }

    private static Key CodeOf(InputEventKey key) => key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
}

/// <summary>
/// A window over the room: a title bar (drag to move, «×» to close), the panel, and a corner grip to resize. The background is
/// opaque and the window stops every mouse event over it.
/// </summary>
public partial class GameWindow : PanelContainer
{
    public const float TitleHeight = 30;
    public const float GripSize = 16;

    public WindowSpec Spec { get; }
    public Control Content { get; }
    public Label TitleLabel { get; }
    public Button CloseButton { get; }

    /// <summary>The close button was pressed.</summary>
    public event Action? CloseRequested;

    /// <summary>A drag or a resize ended.</summary>
    public event Action? Moved;

    private bool _dragging;
    private bool _resizing;
    private Vector2 _grab;

    public GameWindow(WindowSpec spec, Control content)
    {
        Spec = spec;
        Content = content;
        MouseFilter = MouseFilterEnum.Stop;
        var style = new StyleBoxFlat
        {
            BgColor = WindowHost.Background,
            BorderColor = WindowHost.Border,
            ShadowColor = new Color(0, 0, 0, 0.55f),
            ShadowSize = 6,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(3);
        AddThemeStyleboxOverride("panel", style);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 0);
        AddChild(box);

        var title = new PanelContainer { Name = "Title", CustomMinimumSize = new Vector2(0, TitleHeight), MouseFilter = MouseFilterEnum.Stop };
        var titleStyle = new StyleBoxFlat { BgColor = WindowHost.TitleBackground, ContentMarginLeft = 10, ContentMarginRight = 4 };
        titleStyle.CornerRadiusTopLeft = titleStyle.CornerRadiusTopRight = 3;
        title.AddThemeStyleboxOverride("panel", titleStyle);
        title.GuiInput += OnTitleInput;
        title.MouseDefaultCursorShape = CursorShape.Move;
        box.AddChild(title);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        title.AddChild(row);
        TitleLabel = new Label
        {
            Text = $"{spec.Title} ({KeyName(spec.Key)})",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        TitleLabel.AddThemeColorOverride("font_color", new Color(0.93f, 0.85f, 0.7f));
        row.AddChild(TitleLabel);
        CloseButton = new Button { Text = "×", Flat = true, FocusMode = FocusModeEnum.None, TooltipText = "Закрыть (Esc закрывает верхнее окно)", CustomMinimumSize = new Vector2(28, 0) };
        CloseButton.Pressed += () => CloseRequested?.Invoke();
        row.AddChild(CloseButton);

        var body = new MarginContainer { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
        body.AddThemeConstantOverride("margin_left", 8);
        body.AddThemeConstantOverride("margin_right", 8);
        body.AddThemeConstantOverride("margin_top", 6);
        body.AddThemeConstantOverride("margin_bottom", 10);
        box.AddChild(body);
        content.SizeFlagsVertical = SizeFlags.ExpandFill;
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(content);

        var grip = new Control
        {
            Name = "Grip",
            CustomMinimumSize = new Vector2(GripSize, GripSize),
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.Fdiagsize,
            TooltipText = "Потяните, чтобы изменить размер",
        };
        grip.GuiInput += OnGripInput;
        grip.Draw += () =>
        {
            var c = WindowHost.Border;
            for (int i = 1; i <= 3; i++)
                grip.DrawLine(new Vector2(GripSize - 4 * i, GripSize - 2), new Vector2(GripSize - 2, GripSize - 4 * i), c, 1);
        };
        AddChild(grip);
        grip.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        grip.SizeFlagsVertical = SizeFlags.ShrinkEnd;
    }

    /// <summary>The key's letter for the title and the top bar («P», «Esc»).</summary>
    public static string KeyName(Key key) => OS.GetKeycodeString(key);

    private WindowHost? Host => GetParent() as WindowHost;

    private void OnTitleInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _dragging = mb.Pressed;
                _grab = mb.GlobalPosition - GlobalPosition;
                if (!mb.Pressed) Moved?.Invoke();
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                Position = motion.GlobalPosition - _grab - (GetParent<Control>()?.GlobalPosition ?? Vector2.Zero);
                Host?.Clamp(this);
                AcceptEvent();
                break;
        }
    }

    private void OnGripInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _resizing = mb.Pressed;
                _grab = mb.GlobalPosition - (GlobalPosition + Size);
                if (!mb.Pressed) Moved?.Invoke();
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _resizing:
                var size = motion.GlobalPosition - _grab - GlobalPosition;
                Size = new Vector2(Mathf.Max(WindowHost.MinSize.X, size.X), Mathf.Max(WindowHost.MinSize.Y, size.Y));
                Host?.Clamp(this);
                AcceptEvent();
                break;
        }
    }
}
