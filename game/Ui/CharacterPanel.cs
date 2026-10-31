using Dialoid.Core.Character;
using Dialoid.Core.Explain;
using Dialoid.Sandbox;
using Godot;
using Dialoid.Game.Ui.Art;

namespace Dialoid.Game.Ui;

/// <summary>
/// The «Персонаж» tab (spec §13.1): class, level, ascendancy (from level 20), weapon, items, every CharacterLine through a
/// <see cref="NumberLabel"/> and the build's violations in red. Every change is a <see cref="SandboxSession"/> command;
/// the panel redraws itself whole (deferred, see <see cref="SessionPanel"/>) on every change.
/// </summary>
public partial class CharacterPanel : SessionPanel
{
    /// <summary>Key of the level number (an owner-set <see cref="NumberLabel.InputLine"/>).</summary>
    public const string LevelKey = NumberLabel.InputKeyPrefix + "level";

    private Action<NumberLine, string?> _open = null!;
    private string? _commandError;
    private readonly List<NumberLabel> _numbers = new();
    private readonly List<string> _violations = new();
    private readonly List<CheckBox> _itemBoxes = new();

    /// <summary>The number labels shown now: the level first, then one per CharacterLine in order.</summary>
    public IReadOnlyList<NumberLabel> Numbers => _numbers;

    /// <summary>The CharacterLine labels shown now (without the level).</summary>
    public IReadOnlyList<NumberLabel> LineNumbers => _numbers.Where(n => !NumberLabel.IsInput(n.Line)).ToList();

    /// <summary>The violation messages shown now.</summary>
    public IReadOnlyList<string> ShownViolations => _violations;

    /// <summary>The last refused command's error shown now, or null.</summary>
    public string? CommandError => _commandError;

    /// <summary>The weapon selector shown now (the self-test fires its signal like a click).</summary>
    public OptionButton? WeaponOptions { get; private set; }

    /// <summary>Weapon id of each <see cref="WeaponOptions"/> item (null: none).</summary>
    public IReadOnlyList<string?> WeaponChoices { get; private set; } = Array.Empty<string?>();

    /// <summary>The item check boxes shown now, by item id order.</summary>
    public IReadOnlyList<CheckBox> ItemBoxes => _itemBoxes;

    /// <summary>The shown label of a key (life, armor..., <see cref="LevelKey"/>), or null.</summary>
    public NumberLabel? NumberFor(string key) => _numbers.FirstOrDefault(n => n.Line.Key == key);

    public void Setup(SandboxSession session, Action<NumberLine, string?> open)
    {
        InitSession(session);
        _open = open;
    }

    // ---- handlers (the controls and the self-test call these) -------------------------------------------------------

    /// <summary>Raised after an accepted class change (the build is reset; Main clears the breakdown history).</summary>
    public event Action? BuildReplaced;

    public CommandResult SelectClass(string classId)
    {
        var result = Run(Session.SetClass(classId));
        if (result.Ok) BuildReplaced?.Invoke();
        return result;
    }
    public CommandResult SelectLevel(int level) => Run(Session.SetLevel(level));
    public CommandResult SelectAscendancy(string? ascendancyId) => Run(Session.SetAscendancy(ascendancyId));
    public CommandResult SelectWeapon(string? weaponId) => Run(Session.SetWeapon(weaponId));
    public CommandResult ToggleItem(string itemId) => Run(Session.ToggleItem(itemId));

    /// <summary>Remembers a refused command's error and redraws (a refusal does not raise Changed, but controls must reset).</summary>
    private CommandResult Run(CommandResult result)
    {
        if (!result.Ok)
        {
            _commandError = result.Error;
            RequestRedraw();
        }
        return result;
    }

    protected override void OnSessionChanged()
    {
        _commandError = null;
        RequestRedraw();
    }

    // ---- drawing ------------------------------------------------------------------------------------------------------

    protected override void DrawContent()
    {
        _numbers.Clear();
        _violations.Clear();
        _itemBoxes.Clear();

        var build = Session.Build;
        var data = Session.Data;

        if (_commandError != null) AddText("Команда отклонена: " + _commandError, Red);

        // Class.
        var classIds = data.Classes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var classBox = Options(classIds.Select(id => data.Classes[id].Name), classIds.IndexOf(build.ClassId));
        classBox.Name = "ClassChoice";
        classBox.AddThemeConstantOverride("icon_max_width", 24);
        for (int i = 0; i < classIds.Count; i++)
        {
            classBox.SetItemIcon(i, GameIcons.Class(classIds[i]));
            classBox.GetPopup().SetItemIconMaxWidth(i, 24);
        }
        classBox.ItemSelected += i => SelectClass(classIds[(int)i]);
        Row("Класс (смена сбрасывает билд)", classBox);

        // Level: the slider applies on release (or on a click/keyboard step) so dragging does not rebuild the panel;
        // the mouse wheel does not change it (scrolling the page over it must not edit the build).
        var levelNumber = NumberLabel.For(LevelLine(build.Level), _open);
        var slider = new HSlider
        {
            MinValue = 1,
            MaxValue = BuildRules.MaxLevel,
            Step = 1,
            Value = build.Level,
            Scrollable = false,
            CustomMinimumSize = new Vector2(120, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, // fills the row; the window is a third of the screen
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
        ascBox.AddThemeConstantOverride("icon_max_width", 24);
        for (int i = 1; i < ascIds.Count; i++)
        {
            var start = data.Ascendancies[ascIds[i]!].Start;
            ascBox.SetItemIcon(i, GameIcons.Perk(start.Id, start.Effects));
            ascBox.GetPopup().SetItemIconMaxWidth(i, 24);
        }
        ascBox.Disabled = build.Level < BuildRules.AscendancyUnlockLevel && build.AscendancyId == null;
        ascBox.ItemSelected += i => SelectAscendancy(ascIds[(int)i]);
        Row("Асцендант", ascBox,
            ascBox.Disabled ? new Label { Text = $"открывается с {BuildRules.AscendancyUnlockLevel} уровня" } : null);

        // Weapon: any class may take any weapon stub (spec §8); a type the attacks cannot use shows up as a violation.
        var weaponIds = new List<string?> { null };
        weaponIds.AddRange(data.Weapons.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => (string?)k));
        var weaponBox = Options(weaponIds.Select(id => id == null ? "(нет)" : data.Weapons[id].Name),
            weaponIds.IndexOf(build.WeaponId));
        weaponBox.AddThemeConstantOverride("icon_max_width", 24);
        for (int i = 1; i < weaponIds.Count; i++)
        {
            weaponBox.SetItemIcon(i, GameIcons.Weapon(weaponIds[i]));
            weaponBox.GetPopup().SetItemIconMaxWidth(i, 24);
        }
        weaponBox.ItemSelected += i => SelectWeapon(weaponIds[(int)i]);
        WeaponOptions = weaponBox;
        WeaponChoices = weaponIds;
        Row("Оружие", weaponBox);

        // Items.
        AddText("Предметы-заглушки:");
        foreach (var item in data.Items.Values.OrderBy(i => i.Id, StringComparer.Ordinal))
        {
            var id = item.Id;
            var box = new CheckBox { Text = item.Name, ButtonPressed = build.ItemIds.Contains(id) };
            box.Icon = id == "ring_of_mist" ? GameIcons.Symbol("ring") : GameIcons.Skill(item.GrantsSkill);
            box.AddThemeConstantOverride("icon_max_width", 24);
            box.Toggled += _ => ToggleItem(id);
            _itemBoxes.Add(box);
            Content!.AddChild(box);
        }

        // Violations.
        Content!.AddChild(new HSeparator());
        var violations = Session.Violations;
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
        Content.AddChild(new HSeparator());
        var lines = Session.CharacterLines;
        if (lines.Count == 0) AddText("Показатели не посчитаны: исправьте нарушения.", Red);
        foreach (var line in lines)
        {
            var label = NumberLabel.For(line, _open);
            _numbers.Add(label);
            Content.AddChild(label);
        }
    }

    private void ApplyLevel(int level)
    {
        if (level != Session.Build.Level) SelectLevel(level);
    }

    private static NumberLine LevelLine(int level) =>
        NumberLabel.InputLine("level", "Уровень", level, "уровень билда, ползунок на вкладке «Персонаж»");

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
        {
            if (c == null) continue;
            // A number beside a control (the level) keeps its whole text: «…» there would hide the number.
            if (c is NumberLabel number) number.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            row.AddChild(c);
        }
        Content!.AddChild(row);
    }
}
