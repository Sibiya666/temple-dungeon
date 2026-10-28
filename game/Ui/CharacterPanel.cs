using Dialoid.Core.Character;
using Dialoid.Core.Explain;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game.Ui;

/// <summary>
/// The «Персонаж» tab (spec §13.1): class, level, ascendancy (from level 20), weapon, items, every CharacterLine through a
/// <see cref="NumberLabel"/> and the build's violations in red. Every change is a <see cref="SandboxSession"/> command;
/// the panel redraws itself whole on <see cref="SandboxSession.Changed"/>.
/// </summary>
public partial class CharacterPanel : VBoxContainer
{
    private static readonly Color Red = new(1f, 0.45f, 0.45f);

    private SandboxSession _session = null!;
    private Action<NumberLine, string?> _open = null!;
    private VBoxContainer _content = null!;
    private string? _commandError;
    private readonly List<NumberLabel> _numbers = new();
    private readonly List<string> _violations = new();

    /// <summary>The number labels shown now (level and CharacterLines).</summary>
    public IReadOnlyList<NumberLabel> Numbers => _numbers;

    /// <summary>The violation messages shown now.</summary>
    public IReadOnlyList<string> ShownViolations => _violations;

    /// <summary>The last refused command's error shown now, or null.</summary>
    public string? CommandError => _commandError;

    /// <summary>The shown label of a CharacterLine key (life, armor...), or null.</summary>
    public NumberLabel? NumberFor(string key) => _numbers.FirstOrDefault(n => n.Line.Key == key);

    public void Setup(SandboxSession session, Action<NumberLine, string?> open)
    {
        _session = session;
        _open = open;
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);
        _content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_content);
        _session.Changed += OnChanged;
        Redraw();
    }

    public override void _ExitTree()
    {
        if (_session != null) _session.Changed -= OnChanged;
    }

    // ---- handlers (the controls and the self-test call these) -------------------------------------------------------

    public CommandResult SelectClass(string classId) => Run(_session.SetClass(classId));
    public CommandResult SelectLevel(int level) => Run(_session.SetLevel(level));
    public CommandResult SelectAscendancy(string? ascendancyId) => Run(_session.SetAscendancy(ascendancyId));
    public CommandResult SelectWeapon(string? weaponId) => Run(_session.SetWeapon(weaponId));
    public CommandResult ToggleItem(string itemId) => Run(_session.ToggleItem(itemId));

    /// <summary>Remembers a refused command's error and redraws (a refusal does not raise Changed, but controls must reset).</summary>
    private CommandResult Run(CommandResult result)
    {
        if (!result.Ok)
        {
            _commandError = result.Error;
            Redraw();
        }
        return result;
    }

    private void OnChanged()
    {
        _commandError = null;
        Redraw();
    }

    // ---- drawing ------------------------------------------------------------------------------------------------------

    private void Redraw()
    {
        foreach (var child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }
        _numbers.Clear();
        _violations.Clear();

        var build = _session.Build;
        var data = _session.Data;

        if (_commandError != null) AddText("Команда отклонена: " + _commandError, Red);

        // Class.
        var classIds = data.Classes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var classBox = Options(classIds.Select(id => data.Classes[id].Name), classIds.IndexOf(build.ClassId));
        classBox.ItemSelected += i => SelectClass(classIds[(int)i]);
        Row("Класс (смена сбрасывает билд)", classBox);

        // Level: the slider applies on release (or on a click/keyboard step) so dragging does not rebuild the panel.
        var levelNumber = NumberLabel.For(LevelLine(build.Level), _open);
        var slider = new HSlider
        {
            MinValue = 1,
            MaxValue = BuildRules.MaxLevel,
            Step = 1,
            Value = build.Level,
            CustomMinimumSize = new Vector2(400, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        bool dragging = false;
        slider.DragStarted += () => dragging = true;
        slider.ValueChanged += v =>
        {
            levelNumber.SetLine(LevelLine((int)v));
            if (!dragging) CallDeferred(MethodName.ApplyLevel, (int)v);
        };
        slider.DragEnded += _ =>
        {
            dragging = false;
            CallDeferred(MethodName.ApplyLevel, (int)slider.Value);
        };
        _numbers.Add(levelNumber);
        Row("Уровень", slider, levelNumber);

        // Ascendancy: only the current class's, chosen from level 20 (an already chosen one can always be removed).
        var ascIds = new List<string?> { null };
        ascIds.AddRange(data.Ascendancies.Values.Where(a => a.ClassId == build.ClassId)
            .OrderBy(a => a.Id, StringComparer.Ordinal).Select(a => (string?)a.Id));
        var ascBox = Options(ascIds.Select(id => id == null ? "(нет)" : data.Ascendancies[id].Name), ascIds.IndexOf(build.AscendancyId));
        ascBox.Disabled = build.Level < BuildRules.AscendancyUnlockLevel && build.AscendancyId == null;
        ascBox.ItemSelected += i => SelectAscendancy(ascIds[(int)i]);
        Row("Асцендант", ascBox,
            ascBox.Disabled ? new Label { Text = $"открывается с {BuildRules.AscendancyUnlockLevel} уровня" } : null);

        // Weapon: any class may take any weapon stub (spec §8); a type the attacks cannot use shows up as a violation.
        var weaponIds = new List<string?> { null };
        weaponIds.AddRange(data.Weapons.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => (string?)k));
        var weaponBox = Options(weaponIds.Select(id => id == null ? "(нет)" : $"{data.Weapons[id].Name} ({data.Weapons[id].Type})"),
            weaponIds.IndexOf(build.WeaponId));
        weaponBox.ItemSelected += i => SelectWeapon(weaponIds[(int)i]);
        Row("Оружие", weaponBox);

        // Items.
        AddText("Предметы-заглушки:");
        foreach (var item in data.Items.Values.OrderBy(i => i.Id, StringComparer.Ordinal))
        {
            var id = item.Id;
            var box = new CheckBox { Text = item.Name, ButtonPressed = build.ItemIds.Contains(id) };
            box.Toggled += _ => ToggleItem(id);
            _content.AddChild(box);
        }

        // Violations.
        _content.AddChild(new HSeparator());
        var violations = _session.Violations;
        if (violations.Count == 0) AddText("Нарушений нет.");
        else
        {
            AddText("Нарушения:", Red);
            foreach (var v in violations)
            {
                _violations.Add(v.Message);
                AddText("• " + v.Message, Red);
            }
        }

        // Numbers.
        _content.AddChild(new HSeparator());
        var lines = _session.CharacterLines;
        if (lines.Count == 0) AddText("Показатели не посчитаны: исправьте нарушения.", Red);
        foreach (var line in lines)
        {
            var label = NumberLabel.For(line, _open);
            _numbers.Add(label);
            _content.AddChild(label);
        }
    }

    private void ApplyLevel(int level)
    {
        if (level != _session.Build.Level) SelectLevel(level);
    }

    /// <summary>The level is the owner's input, not a computed number: a line without a breakdown.</summary>
    private static NumberLine LevelLine(int level) =>
        new("level", "Уровень", level, null, "уровень билда — ввод владельца (ползунок)");

    private static OptionButton Options(IEnumerable<string> texts, int selected)
    {
        var box = new OptionButton();
        foreach (var t in texts) box.AddItem(t);
        box.Selected = selected;
        return box;
    }

    private void Row(string caption, params Control?[] controls)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = caption + ":", CustomMinimumSize = new Vector2(240, 0) });
        foreach (var c in controls)
            if (c != null) row.AddChild(c);
        _content.AddChild(row);
    }

    private Label AddText(string text, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        if (color is { } c) label.AddThemeColorOverride("font_color", c);
        _content.AddChild(label);
        return label;
    }
}
