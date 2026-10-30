using Dialoid.Core.Data;
using Dialoid.Core.Explain;
using Dialoid.Game.Room;
using Dialoid.Game.Ui;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game;

/// <summary>
/// Root of the training room: loads game data, creates the sandbox session, fills the screen with the 3D room and puts the
/// plan-3 panels in overlay windows (hotkeys and the top bar), with the combat screen over the room (plan 4 task 10).
/// Every rule goes through <see cref="SandboxSession"/> and <see cref="TrainingRoom"/>; this class only draws.
/// <para>Layers, bottom to top: the room (a SubViewportContainer over the whole screen), the combat screen, the windows, the top
/// bar, the load error. Mouse events go through Godot's GUI: a window or the top bar stops them, so the fight and the camera
/// (which listen to unhandled input inside the room's viewport) only get what no window took.</para>
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

    /// <summary>Command line user argument (after "--") that runs <see cref="SelfTest"/> and quits.</summary>
    public const string SelfTestArg = "--selftest";

    /// <summary>The room's assets (stubs for whatever is missing in res://vendor/).</summary>
    public AssetLibrary? Assets { get; private set; }

    /// <summary>The 3D training room, over the whole screen.</summary>
    public RoomView? Room { get; private set; }

    /// <summary>The live fight of the room (null when the data failed to load).</summary>
    public TrainingRoom? Training { get; private set; }

    /// <summary>Wires <see cref="Training"/> to the room: input, animations, effects (null when the data failed to load).</summary>
    public CombatDriver? Combat { get; private set; }

    /// <summary>The viewport the room renders into, and its container (the whole screen).</summary>
    public SubViewport? RoomViewport { get; private set; }
    public SubViewportContainer? RoomContainer { get; private set; }

    /// <summary>The overlay windows (one per panel) and the bar with their buttons.</summary>
    public WindowHost Windows { get; private set; } = null!;
    public TopBar? Bar { get; private set; }

    /// <summary>The combat screen (null when the data failed to load).</summary>
    public CombatHud? Hud { get; private set; }

    /// <summary>The editing session, or null when the data failed to load.</summary>
    public SandboxSession? Session { get; private set; }

    /// <summary>The data load error shown to the owner, or null when the session is ready.</summary>
    public string? LoadError { get; private set; }

    /// <summary>The resolved data directory (with the marker file), or null when it was not found.</summary>
    public string? DataDirectory { get; private set; }

    /// <summary>The panels; null when the data failed to load.</summary>
    public BreakdownPanel? Breakdown { get; private set; }
    public CharacterPanel? Character { get; private set; }
    public BuildsPanel? Builds { get; private set; }
    public PassivePanel? Passives { get; private set; }
    public AscendancyPanel? AscendancyView { get; private set; }
    public SkillsPanel? Skills { get; private set; }
    public SimulationPanel? Simulation { get; private set; }

    /// <summary>The combat log, the damage meter and the charts (plan 4 task 11); null when the data failed to load.</summary>
    public CombatLogWindow? LogView { get; private set; }
    public MeterWindow? MeterView { get; private set; }
    public ChartsWindow? ChartsView { get; private set; }

    private Label _errorLabel = null!;

    public override void _Ready()
    {
        BuildRoom();
        Windows = new WindowHost { Name = "Windows" };
        (Session, LoadError) = LoadSession();
        if (LoadError != null)
        {
            AddChild(Windows);
            BuildError();
            _errorLabel.Text = LoadError;
            _errorLabel.Visible = true;
            GD.Print($"{LoadFailedMarker} {LoadError.ReplaceLineEndings(" | ")}");
        }
        else
        {
            BuildCombat(Session!);
            AddChild(Windows);
            BuildPanels(Session!);
            var bar = Bar = new TopBar();
            AddChild(bar);
            bar.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
            bar.Setup(Windows, Session, () => Training!.DummyId, id => Combat!.SetDummy(id));
            Windows.Bar = bar;
            WireGamepad();
            Windows.TopInset = () => bar.Size.Y;
            Windows.BottomInset = () => CombatHud.ReservedHeight;
            // A tree opens fitted to its window (captions inside the view).
            Windows.Opened += id =>
            {
                if (id == WindowIds.Passives) Passives!.View.RequestFit();
                else if (id == WindowIds.Ascendancy) AscendancyView!.View.RequestFit();
            };
            BuildError();
        }

        if (OS.GetCmdlineUserArgs().Contains(SelfTestArg))
            new SelfTest(this).Start();
    }

    /// <summary>
    /// The gamepad (plan 4 task 13): the fight and the camera take no gamepad buttons while a window has the focus; a lost gamepad
    /// lets go of everything the fight held; the log never reaches under the combat screen (its right edge stops 8 px left of it).
    /// </summary>
    private void WireGamepad()
    {
        Combat!.InputBlocked = () => Windows.WindowHasFocus;
        Room!.CameraRig.InputBlocked = () => Windows.WindowHasFocus;
        Windows.LeftBottomRightLimit = screen => CombatHud.LeftEdge(screen.X) - CombatHud.WindowGap;
        Windows.HeroScreenX = () => RoomToScreen(Combat.Hero.Position)?.X;
        // Start, then ↓ from the top bar: the combat screen's folded rows (their full lists) on the gamepad.
        Windows.HudRoot = Hud;
        Windows.HudTargets = () => Hud?.PadTargets() ?? (IReadOnlyList<Control>)Array.Empty<Control>();
        if (Dialoid.Game.Input.InputDevice.Instance is { } device)
        {
            device.GamepadLost += Combat.ReleaseAll;
            device.GamepadLost += Windows.ClearPadState;
            device.GamepadLost += Room.CameraRig.ClearStick;
            device.Changed += Combat.OnDeviceChanged;
        }
    }

    /// <summary>Opens a window by its id (<see cref="WindowIds"/>) and raises it.</summary>
    public void OpenWindow(string id) => Windows.OpenWindow(id);

    public void CloseWindow(string id) => Windows.CloseWindow(id);

    public bool IsOpen(string id) => Windows.IsOpen(id);

    /// <summary>Closes every window: only the room and the combat screen stay.</summary>
    public void CloseAllWindows() => Windows.CloseAll();

    /// <summary>Shows a number's breakdown in the «Разбор» window and opens it (spec §13.6). Used by every NumberLabel.</summary>
    public void ShowBreakdown(NumberLine line, string? context = null)
    {
        if (Breakdown == null) return;
        Breakdown.ShowLine(line, context);
        Windows.OpenFromUi(WindowIds.Breakdown); // on the gamepad from a focused window it takes the focus
    }

    private void BuildPanels(SandboxSession session)
    {
        Breakdown = new BreakdownPanel { Name = "BreakdownPanel" };
        Breakdown.Setup(session);
        Windows.Add(WindowIds.Breakdown, Breakdown);

        Character = new CharacterPanel { Name = "CharacterPanel" };
        Character.Setup(session, ShowBreakdown);
        Windows.Add(WindowIds.Character, Character);

        Passives = new PassivePanel { Name = "PassivePanel" };
        Passives.Setup(session);
        Windows.Add(WindowIds.Passives, Passives);

        AscendancyView = new AscendancyPanel { Name = "AscendancyPanel" };
        AscendancyView.Setup(session);
        Windows.Add(WindowIds.Ascendancy, AscendancyView);

        Skills = new SkillsPanel { Name = "SkillsPanel" };
        Skills.Setup(session, ShowBreakdown);
        Windows.Add(WindowIds.Skills, Skills);

        Simulation = new SimulationPanel { Name = "SimulationPanel" };
        Simulation.Setup(session, ShowBreakdown);
        Windows.Add(WindowIds.Simulation, Simulation);

        Builds = new BuildsPanel { Name = "BuildsPanel" };
        Builds.Setup(session, DataDirectory);
        Windows.Add(WindowIds.Builds, Builds);

        LogView = new CombatLogWindow { Name = "LogView" };
        LogView.Setup(session, Training!);
        Windows.Add(WindowIds.Log, LogView);
        MeterView = new MeterWindow { Name = "MeterView" };
        MeterView.Setup(session, Training!, ShowBreakdown, Combat?.Numbers, () => Windows.OpenFromUi(WindowIds.Charts));
        Windows.Add(WindowIds.Meter, MeterView);
        ChartsView = new ChartsWindow { Name = "ChartsView" };
        var meter = MeterView;
        ChartsView.Setup(session, Training!, () => meter.SelectedSegment);
        MeterView.SelectionChanged += ChartsView.MarkDirty;
        Windows.Add(WindowIds.Charts, ChartsView);

        // A replaced build makes the old breakdown history meaningless (spec §13.6 history is per build).
        Builds.BuildReplaced += Breakdown.ClearHistory;
        Character.BuildReplaced += Breakdown.ClearHistory;
    }

    private void BuildError()
    {
        _errorLabel = new Label
        {
            Name = "Error",
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _errorLabel.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.45f));
        AddChild(_errorLabel);
        _errorLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _errorLabel.OffsetTop = Bar != null ? TopBar.Height + 4 : 8;
        _errorLabel.OffsetLeft = 8;
        _errorLabel.OffsetRight = -8;
    }

    /// <summary>
    /// The room over the whole screen: a SubViewport with its own 3D world and a shadow atlas sized for the budget (one 2048 slot
    /// for the hero light, eight 1024 slots, sixteen 512 slots; spec §4), FXAA, and the room in it.
    /// </summary>
    private void BuildRoom()
    {
        RoomViewport = new SubViewport
        {
            Name = "RoomViewport",
            OwnWorld3D = true,
            PositionalShadowAtlasSize = 4096,
            ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa,
            HandleInputLocally = true,
        };
        RoomViewport.SetPositionalShadowAtlasQuadrantSubdiv(0, Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv1);
        RoomViewport.SetPositionalShadowAtlasQuadrantSubdiv(1, Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv4);
        RoomViewport.SetPositionalShadowAtlasQuadrantSubdiv(2, Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv4);
        RoomViewport.SetPositionalShadowAtlasQuadrantSubdiv(3, Viewport.PositionalShadowAtlasQuadrantSubdiv.Subdiv16);
        RoomContainer = new SubViewportContainer { Name = "RoomContainer", Stretch = true };
        RoomContainer.AddChild(RoomViewport);
        AddChild(RoomContainer);
        RoomContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        Assets = new AssetLibrary(AssetLibrary.LoadTable());
        Room = new RoomView { Name = "Room" };
        RoomViewport.AddChild(Room);
        Room.Build(Assets, RoomView.QualityFromArgs(OS.GetCmdlineUserArgs()));
        GD.Print($"Комната готова: модели {(Assets.HasModels ? "есть" : "заглушки")}, текстуры {(Assets.HasTextures ? "есть" : "однотонные")}, " +
                 $"качество {Room.Quality}, недостает ассетов: {Assets.Missing.Count}");
    }

    /// <summary>
    /// The live fight follows the session's build; the driver lives in the room's viewport (it gets the room's input). The combat
    /// screen sits over the room, under the windows.
    /// </summary>
    private void BuildCombat(SandboxSession session)
    {
        Training = new TrainingRoom(session);
        Training.Attach();
        Combat = new CombatDriver { Name = "Combat" };
        Room!.AddChild(Combat);
        Combat.Setup(Room, Training, SkillAnimations.Load());
        Hud = new CombatHud();
        AddChild(Hud);
        Hud.Setup(Combat, session, RoomToScreen);
        Hud.FigureAt = ScreenToFigure;
    }

    /// <summary>The dummy figure under a point of this control's screen (the target plate follows the cursor), or null.</summary>
    public int? ScreenToFigure(Vector2 point)
    {
        if (Combat == null || RoomViewport == null || RoomContainer == null || !RoomContainer.GetGlobalRect().HasPoint(point)) return null;
        var size = RoomViewport.Size;
        var scale = RoomContainer.Size.X > 0 && RoomContainer.Size.Y > 0 ? new Vector2(size.X, size.Y) / RoomContainer.Size : Vector2.One;
        return Combat.FigureAt((point - RoomContainer.GlobalPosition) * scale);
    }

    /// <summary>A point of the room on this control's screen; null when it is behind the camera.</summary>
    public Vector2? RoomToScreen(Vector3 point)
    {
        if (Room == null || RoomViewport == null || RoomContainer == null) return null;
        var camera = Room.CameraRig.Camera;
        if (!camera.IsInsideTree() || camera.IsPositionBehind(point)) return null;
        var p = camera.UnprojectPosition(point);
        var size = RoomViewport.Size;
        var scale = size.X > 0 && size.Y > 0 ? RoomContainer.Size / new Vector2(size.X, size.Y) : Vector2.One;
        return RoomContainer.Position + p * scale;
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
