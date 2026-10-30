using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>
/// The bar over the room (spec §4): one toggle button per window, captioned with its key («Пассивки (P)»), and the dummy preset
/// list from data/dummies.json (choosing one restarts the fight on that preset). The buttons never take keyboard focus, so the
/// keys keep going to the fight after a click.
/// </summary>
public partial class TopBar : PanelContainer
{
    public const float Height = 40;

    private readonly Dictionary<string, Button> _buttons = new();
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
            var button = new Button
            {
                Name = "Open_" + spec.Id,
                Text = $"{spec.Title} ({GameWindow.KeyName(spec.Key)})",
                ToggleMode = true,
                FocusMode = FocusModeEnum.None,
                TooltipText = $"Открыть или закрыть окно «{spec.Title}» — клавиша {GameWindow.KeyName(spec.Key)}",
            };
            var id = spec.Id;
            button.Pressed += () => host.ToggleWindow(id);
            _buttons[id] = button;
            row.AddChild(button);
        }

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        if (session != null && currentDummy != null)
        {
            row.AddChild(new Label { Text = "Манекен:", VerticalAlignment = VerticalAlignment.Center });
            DummyOptions = new OptionButton { Name = "Dummy", FocusMode = FocusModeEnum.None, TooltipText = "Пресет манекена из data/dummies.json; смена начинает бой заново" };
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
    }

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
