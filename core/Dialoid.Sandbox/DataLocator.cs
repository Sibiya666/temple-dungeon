namespace Dialoid.Sandbox;

/// <summary>Finds the game data directory: the nearest <c>data/</c> with <c>classes.json</c>, walking up from a start directory.</summary>
public static class DataLocator
{
    public const string DirectoryName = "data";
    public const string MarkerFile = "classes.json";

    /// <summary>Full path of the found data directory, or null.</summary>
    public static string? Find(string startDirectory) =>
        Candidates(startDirectory).FirstOrDefault(c => File.Exists(Path.Combine(c, MarkerFile)));

    /// <summary>
    /// Every data directory path checked, nearest first (for the "not found" message).
    /// An empty or blank start directory means the current directory.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string startDirectory)
    {
        var start = string.IsNullOrWhiteSpace(startDirectory) ? Directory.GetCurrentDirectory() : startDirectory;
        var result = new List<string>();
        for (var dir = new DirectoryInfo(Path.GetFullPath(start)); dir != null; dir = dir.Parent)
            result.Add(Path.Combine(dir.FullName, DirectoryName));
        return result;
    }
}
