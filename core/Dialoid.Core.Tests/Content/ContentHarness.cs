using Dialoid.Core.Builds;
using Dialoid.Core.Character;
using Dialoid.Core.Data;
using Dialoid.Core.Effects;
using Dialoid.Core.Simulation;
using Dialoid.Core.Tests.Fixtures;

namespace Dialoid.Core.Tests.Content;

/// <summary>Shared helpers for content/balance tests over the real data directory.</summary>
public static class ContentHarness
{
    private static readonly Lazy<GameData> LazyData = new(() =>
    {
        var res = DataLoader.Load(FixturePaths.RealData);
        Assert.True(res.Errors.Count == 0, string.Join(Environment.NewLine, res.Errors.Select(e => $"{e.File} [{e.Id}]: {e.Message}")));
        return res.Data!;
    });

    /// <summary>Real game data, loaded once.</summary>
    public static GameData Data => LazyData.Value;

    /// <summary>Reads data/builds/&lt;relativePath&gt;.json (relative path without extension).</summary>
    public static Build LoadBuild(string relativePath)
    {
        var path = Path.Combine(FixturePaths.RealData, "builds", relativePath + ".json");
        Assert.True(BuildCodec.TryFromJson(File.ReadAllText(path), Data, out var build, out var error), $"{relativePath}: {error}");
        return build!;
    }

    /// <summary>Average mode, 60 seconds.</summary>
    public static SimResult Run(Build b, string dummyId, int? pack = null) =>
        new Simulator(Data, MechanicRegistry.Default).Run(b, new SimConfig(dummyId, SimMode.Average, PackSizeOverride: pack));

    /// <summary>Average mode against the attacking boss dummy.</summary>
    public static SimResult Survival(Build b) =>
        new Simulator(Data, MechanicRegistry.Default).Run(b, new SimConfig("attacking_boss", SimMode.Average, DummyAttacks: true));

    public static double TrainingDps(Build b) => Run(b, "training").DpsSingleTarget;

    /// <summary>Deep copy for test variants.</summary>
    public static Build Clone(Build b)
    {
        Assert.True(BuildCodec.TryFromJson(BuildCodec.ToJson(b), Data, out var copy, out var error), error);
        return copy!;
    }
}
