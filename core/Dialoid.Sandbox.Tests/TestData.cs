using Dialoid.Core.Data;
using Dialoid.Sandbox;

namespace Dialoid.Sandbox.Tests;

/// <summary>The real repository data, located the same way the game does it.</summary>
public static class TestData
{
    private static readonly Lazy<GameData> LazyData = new(() =>
    {
        var dir = DataLocator.Find(AppContext.BaseDirectory)
            ?? throw new DirectoryNotFoundException("data/: " + string.Join(", ", DataLocator.Candidates(AppContext.BaseDirectory)));
        var res = DataLoader.Load(dir);
        Assert.True(res.Ok, string.Join(Environment.NewLine, res.Errors.Select(e => $"{e.File} [{e.Id}]: {e.Message}")));
        return res.Data!;
    });

    public static GameData Data => LazyData.Value;
}
