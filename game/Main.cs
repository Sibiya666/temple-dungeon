using Dialoid.Core.Data;
using Dialoid.Core.Explain;
using Dialoid.Game.Ui;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game;

/// <summary>
/// Root of the debug scene: loads game data, creates the sandbox session and builds the tab layout in code.
/// Every rule goes through <see cref="SandboxSession"/>; this class only draws.
/// </summary>
public partial class Main : Control
{
    /// <summary>Environment variable that overrides the data directory search.</summary>
    public const string DataEnvVar = "DIALOID_DATA";

    /// <summary>
    /// Prefix of the log line printed when the data fails to load; tools/godot_check.sh looks for it
    /// (exit code 3, or required with --expect-load-failure).
    /// </summary>
    public const string LoadFailedMarker = "DIALOID_LOAD_FAILED:";

    /// <summary>Class of the session created at start.</summary>
    public const string StartClassId = "warrior";

    /// <summary>Tab titles in display order (spec §13).</summary>
    public static readonly IReadOnlyList<string> TabTitles =
        ["Персонаж", "Пассивки", "Асцендант", "Скиллы", "Симуляция", "Разбор", "Билды"];

    /// <summary>The editing session, or null when the data failed to load.</summary>
    public SandboxSession? Session { get; private set; }

    /// <summary>The data load error shown to the owner, or null when the session is ready.</summary>
    public string? LoadError { get; private set; }

    /// <summary>Command line user argument (after "--") that runs <see cref="SelfTest"/> and quits.</summary>
    public const string SelfTestArg = "--selftest";

    /// <summary>Title of the breakdown tab; <see cref="ShowBreakdown"/> switches to it.</summary>
    public const string BreakdownTab = "Разбор";

    /// <summary>The resolved data directory (with the marker file), or null when it was not found.</summary>
    public string? DataDirectory { get; private set; }

    /// <summary>The panels; null when the data failed to load.</summary>
    public BreakdownPanel? Breakdown { get; private set; }
    public CharacterPanel? Character { get; private set; }
    public BuildsPanel? Builds { get; private set; }

    private Label _errorLabel = null!;
    private TabContainer _tabs = null!;
    private readonly Dictionary<string, VBoxContainer> _pages = new();

    public override void _Ready()
    {
        BuildLayout();
        (Session, LoadError) = LoadSession();
        if (LoadError != null)
        {
            _errorLabel.Text = LoadError;
            _errorLabel.Visible = true;
            GD.Print($"{LoadFailedMarker} {LoadError.ReplaceLineEndings(" | ")}");
        }
        else
        {
            BuildPanels(Session!);
        }

        if (OS.GetCmdlineUserArgs().Contains(SelfTestArg))
            new SelfTest(this).Start();
    }

    /// <summary>The page of a tab by its title from <see cref="TabTitles"/>; each task fills its own page.</summary>
    public VBoxContainer Page(string title) => _pages[title];

    /// <summary>Title of the tab shown now.</summary>
    public string CurrentTab => TabTitles[_tabs.CurrentTab];

    /// <summary>Switches to the tab with the given title from <see cref="TabTitles"/>.</summary>
    public void SelectTab(string title)
    {
        var index = TabTitles.ToList().IndexOf(title);
        if (index < 0) throw new ArgumentException($"Unknown tab {title}", nameof(title));
        _tabs.CurrentTab = index;
    }

    /// <summary>Shows a number's breakdown on the «Разбор» tab and switches to it (spec §13.6). Used by every NumberLabel.</summary>
    public void ShowBreakdown(NumberLine line, string? context = null)
    {
        if (Breakdown == null) return;
        Breakdown.ShowLine(line, context);
        SelectTab(BreakdownTab);
    }

    private void BuildPanels(SandboxSession session)
    {
        Breakdown = new BreakdownPanel { Name = "BreakdownPanel" };
        Breakdown.Setup(session);
        AddPanel(BreakdownTab, Breakdown);

        Character = new CharacterPanel { Name = "CharacterPanel" };
        Character.Setup(session, ShowBreakdown);
        AddPanel("Персонаж", Character);

        Builds = new BuildsPanel { Name = "BuildsPanel" };
        Builds.Setup(session, DataDirectory);
        AddPanel("Билды", Builds);
    }

    private void AddPanel(string tab, Control panel)
    {
        panel.SizeFlagsVertical = SizeFlags.ExpandFill;
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Page(tab).AddChild(panel);
    }

    private void BuildLayout()
    {
        var root = new VBoxContainer { Name = "Root" };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);

        _errorLabel = new Label
        {
            Name = "Error",
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _errorLabel.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.45f));
        root.AddChild(_errorLabel);

        _tabs = new TabContainer { Name = "Tabs", SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(_tabs);
        foreach (var title in TabTitles)
        {
            var page = new VBoxContainer { Name = title };
            _tabs.AddChild(page);
            _pages[title] = page;
        }
    }

    private (SandboxSession? Session, string? Error) LoadSession()
    {
        var overridePath = System.Environment.GetEnvironmentVariable(DataEnvVar);
        string? dataDir;
        IReadOnlyList<string> checkedPaths;
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var full = Path.GetFullPath(overridePath);
            checkedPaths = [full];
            dataDir = File.Exists(Path.Combine(full, DataLocator.MarkerFile)) ? full : null;
        }
        else
        {
            var start = ProjectSettings.GlobalizePath("res://");
            checkedPaths = DataLocator.Candidates(start);
            dataDir = DataLocator.Find(start);
        }

        if (dataDir == null)
            return (null, "Данные игры не найдены. Проверенные пути:\n" + string.Join("\n", checkedPaths));

        DataDirectory = dataDir;
        var result = DataLoader.Load(dataDir);
        if (!result.Ok || result.Data == null)
        {
            var lines = result.Errors.Select(e => e.Id == null ? $"{e.File}: {e.Message}" : $"{e.File} ({e.Id}): {e.Message}");
            return (null, $"Данные игры из {dataDir} не загрузились:\n" + string.Join("\n", lines));
        }

        try
        {
            var session = SandboxSession.Create(result.Data, StartClassId);
            GD.Print($"Сессия готова: класс {StartClassId}, данные {dataDir}");
            return (session, null);
        }
        catch (ArgumentException e)
        {
            return (null, $"Сессия не создана: {e.Message}");
        }
    }
}
