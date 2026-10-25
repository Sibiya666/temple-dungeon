namespace Dialoid.Core.Tests.Fixtures;

public static class FixturePaths
{
    public static string Tiny => Path.Combine(AppContext.BaseDirectory, "Fixtures", "tiny");

    /// <summary>The real game data directory (repository data/).</summary>
    public static string RealData
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "data", "stats.json")))
                dir = dir.Parent;
            return dir == null ? throw new DirectoryNotFoundException("data/") : Path.Combine(dir.FullName, "data");
        }
    }

    /// <summary>Copies the tiny fixture into a fresh temp directory so a test can break it.</summary>
    public static string CopyTiny()
    {
        var target = Path.Combine(Path.GetTempPath(), "dialoid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        foreach (var f in Directory.GetFiles(Tiny)) File.Copy(f, Path.Combine(target, Path.GetFileName(f)));
        return target;
    }
}
