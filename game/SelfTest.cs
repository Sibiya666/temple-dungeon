using Dialoid.Game.Ui;
using Dialoid.Sandbox;
using Godot;

namespace Dialoid.Game;

/// <summary>
/// Headless self-test of the debug scene (tools/godot_check.sh --selftest). It drives the real panels through the same
/// handlers their controls call and prints one line per step: «SELFTEST OK name» or «SELFTEST FAIL name: why».
/// Then it quits with 0 when every step passed, else 1.
/// To add a check, add a step in <see cref="Steps"/>: a name and an async body that throws <see cref="Failure"/> (via
/// <see cref="Check"/>) when the check fails; <see cref="NextFrame"/> lets the scene process a frame.
/// </summary>
public sealed class SelfTest
{
    /// <summary>A failed check; its message is printed after the step name.</summary>
    public sealed class Failure(string message) : Exception(message);

    /// <summary>One named check.</summary>
    public sealed record Step(string Name, Func<Task> Run);

    private readonly Main _main;

    public SelfTest(Main main) => _main = main;

    /// <summary>Runs every step after the first frame, prints the results and quits the scene.</summary>
    public async void Start()
    {
        int failed;
        try
        {
            failed = await RunAll();
        }
        catch (Exception e)
        {
            GD.Print($"SELFTEST FAIL selftest: {e.GetType().Name}: {e.Message}");
            failed = 1;
        }
        GD.Print(failed == 0 ? "SELFTEST PASSED" : $"SELFTEST FAILED: {failed}");
        _main.GetTree().Quit(failed == 0 ? 0 : 1);
    }

    private async Task<int> RunAll()
    {
        if (_main.LoadError != null)
        {
            GD.Print($"SELFTEST FAIL load: {_main.LoadError.ReplaceLineEndings(" | ")}");
            return 1;
        }

        await NextFrame();
        var steps = Steps();
        int failed = 0;
        foreach (var step in steps)
        {
            try
            {
                await step.Run();
                GD.Print($"SELFTEST OK {step.Name}");
            }
            catch (Exception e)
            {
                failed++;
                var why = e is Failure ? e.Message : $"{e.GetType().Name}: {e.Message}";
                GD.Print($"SELFTEST FAIL {step.Name}: {why.ReplaceLineEndings(" | ")}");
            }
        }
        GD.Print($"SELFTEST {steps.Count - failed}/{steps.Count}");
        return failed;
    }

    /// <summary>All steps in order. Later tasks append theirs here.</summary>
    private List<Step> Steps()
    {
        var steps = new List<Step>
        {
            new("panels", () =>
            {
                Check(_main.Breakdown != null && _main.Character != null && _main.Builds != null, "панели не созданы");
                return Task.CompletedTask;
            }),
        };

        var mains = _main.Builds?.References.Where(r => r.Group == ReferenceBuilds.MainGroup).ToList() ?? new();
        steps.Add(new("builds.main-listed", () =>
        {
            Check(mains.Count > 0, "в BuildsPanel нет основных эталонов");
            return Task.CompletedTask;
        }));
        foreach (var info in mains) steps.Add(new($"builds.load {info.Title}", () => LoadReference(info)));
        steps.Add(new("character.lines-match", CharacterLinesMatch));
        steps.Add(new("builds.import-garbage", ImportGarbage));
        if (mains.Count > 0) steps.Add(new("character.illegal-level", () => IllegalLevel(mains[0])));
        if (mains.Count > 0) steps.Add(new("character.signal-redraw", () => SignalRedraw(mains[0])));
        return steps;
    }

    /// <summary>The shown CharacterLine labels are the session's lines one to one, with the NumberLabel text rule.</summary>
    private async Task CharacterLinesMatch()
    {
        await Settle();
        var shown = _main.Character!.LineNumbers;
        var lines = _main.Session!.CharacterLines;
        Check(lines.Count > 0, "у сессии нет CharacterLines");
        Check(shown.Count == lines.Count, $"показано {shown.Count} чисел, в сессии {lines.Count}");
        for (int i = 0; i < lines.Count; i++)
        {
            var (label, line) = (shown[i], lines[i]);
            Check(label.Line.Key == line.Key && label.Line.Value == line.Value, $"№{i}: {label.Line.Key} вместо {line.Key}");
            Check(label.Text == NumberLabel.TextOf(line), $"№{i}: текст «{label.Text}»");
        }
    }

    /// <summary>A garbage import is refused with a Russian status and leaves the build alone.</summary>
    private async Task ImportGarbage()
    {
        var session = _main.Session!;
        var builds = _main.Builds!;
        var revision = session.Revision;
        var result = builds.ImportText("мусор");
        Check(!result.Ok, "импорт мусора принят");
        Check(session.Revision == revision, $"ревизия {revision} → {session.Revision}");
        Check(builds.ImportStatus.StartsWith("Импорт отклонен", StringComparison.Ordinal), $"статус «{builds.ImportStatus}»");
        Check(builds.ImportStatus.Any(c => c is >= 'а' and <= 'я'), "статус не на русском");
        await Settle();
    }

    /// <summary>Level 1 on a level-60 reference: no exception, violations shown, the panel still draws.</summary>
    private async Task IllegalLevel(ReferenceBuildInfo info)
    {
        var character = _main.Character!;
        Check(_main.Builds!.Load(info).Ok, "эталон не загрузился");
        await Settle();
        var result = character.SelectLevel(1);
        Check(result.Ok, $"уровень 1 отклонен: {result.Error}");
        await Settle();
        Check(character.ShownViolations.Count > 0, "нарушений не показано");
        Check(character.NumberFor(CharacterPanel.LevelKey)?.Line.Value == 1, "число уровня не 1");
        Check(character.Numbers.Count > 0 && !character.RedrawPending, "панель не перерисовалась");
        Check(_main.Builds.Load(info).Ok, "эталон не перезагрузился");
        await Settle();
        Check(character.ShownViolations.Count == 0, "после перезагрузки остались нарушения");
    }

    /// <summary>
    /// Fires the controls' own signals (as a click does) on controls the resulting redraw frees: weapon ItemSelected and
    /// an item's Toggled. The redraw must be deferred and land on the next frame.
    /// </summary>
    private async Task SignalRedraw(ReferenceBuildInfo info)
    {
        var character = _main.Character!;
        var session = _main.Session!;
        Check(_main.Builds!.Load(info).Ok, "эталон не загрузился");
        await Settle();

        var weapons = character.WeaponOptions!;
        var choices = character.WeaponChoices;
        var target = Enumerable.Range(0, choices.Count).First(i => choices[i] != session.Build.WeaponId);
        var revision = session.Revision;
        weapons.EmitSignal(OptionButton.SignalName.ItemSelected, target);
        Check(session.Revision == revision + 1, "оружие не сменилось");
        Check(character.RedrawPending, "перерисовка не отложена");
        Check(GodotObject.IsInstanceValid(weapons) && weapons.IsInsideTree(), "OptionButton удален во время сигнала");
        await Settle();
        Check(session.Build.WeaponId == choices[target] && character.WeaponOptions != weapons, "панель не перерисовалась после оружия");

        var box = character.ItemBoxes.FirstOrDefault(b => !b.ButtonPressed);
        if (box != null)
        {
            revision = session.Revision;
            box.ButtonPressed = true; // emits Toggled
            Check(session.Revision == revision + 1, "предмет не надет");
            Check(GodotObject.IsInstanceValid(box) && box.IsInsideTree(), "CheckBox удален во время сигнала");
            await Settle();
        }
        Check(_main.Builds.Load(info).Ok, "эталон не перезагрузился");
        await Settle();
    }

    /// <summary>Loads a main reference via BuildsPanel; CharacterPanel shows 0 violations; life opens its breakdown.</summary>
    private async Task LoadReference(ReferenceBuildInfo info)
    {
        var builds = _main.Builds!;
        var character = _main.Character!;
        var result = builds.Load(info);
        Check(result.Ok, $"загрузка отклонена: {result.Error}");
        Check(builds.LoadStatus.Contains(info.Title), $"статус загрузки «{builds.LoadStatus}»");
        await Settle();

        Check(character.ShownViolations.Count == 0, "нарушения: " + string.Join("; ", character.ShownViolations));
        var life = character.NumberFor("life");
        Check(life != null, "нет числа здоровья");
        Check(life!.Line.Breakdown != null, "у здоровья нет разбора");
        Check(life.Line.Value > 0, $"здоровье {life.Line.Value}");

        life.Open();
        Check(_main.CurrentTab == Main.BreakdownTab, $"открыта вкладка «{_main.CurrentTab}», а не «{Main.BreakdownTab}»");
        var shown = _main.Breakdown!;
        Check(shown.Current?.Line.Key == "life", "в «Разборе» не здоровье");
        Check(shown.ShownLines.Count > 0, "в «Разборе» нет строк");
        _main.SelectTab(Main.TabTitles[0]);
    }

    /// <summary>Throws a <see cref="Failure"/> with <paramref name="why"/> when the condition is false.</summary>
    public static void Check(bool condition, string why)
    {
        if (!condition) throw new Failure(why);
    }

    /// <summary>Waits for the next processed frame.</summary>
    public async Task NextFrame() => await _main.ToSignal(_main.GetTree(), SceneTree.SignalName.ProcessFrame);

    /// <summary>Waits two frames so deferred redraws (<see cref="Ui.SessionPanel.RequestRedraw"/>) have run.</summary>
    public async Task Settle()
    {
        await NextFrame();
        await NextFrame();
    }
}
