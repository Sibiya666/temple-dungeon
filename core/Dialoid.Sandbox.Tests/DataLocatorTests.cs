using Dialoid.Sandbox;

namespace Dialoid.Sandbox.Tests;

public class DataLocatorTests
{
    private static string RepoRoot
    {
        get
        {
            var data = DataLocator.Find(AppContext.BaseDirectory);
            Assert.NotNull(data);
            return Path.GetDirectoryName(data)!;
        }
    }

    [Fact]
    public void Finds_data_from_repo_root_core_and_test_bin()
    {
        var root = RepoRoot;
        var expected = Path.Combine(root, "data");
        Assert.True(File.Exists(Path.Combine(expected, "classes.json")));

        Assert.Equal(expected, DataLocator.Find(root));
        Assert.Equal(expected, DataLocator.Find(Path.Combine(root, "core")));
        Assert.Equal(expected, DataLocator.Find(AppContext.BaseDirectory));
    }

    [Fact]
    public void Returns_null_and_candidates_from_temp_dir()
    {
        var temp = Path.Combine(Path.GetTempPath(), "dialoid-locator-" + Guid.NewGuid().ToString("N"), "nested");
        Directory.CreateDirectory(temp);
        try
        {
            Assert.Null(DataLocator.Find(temp));
            var candidates = DataLocator.Candidates(temp);
            Assert.NotEmpty(candidates);
            Assert.Equal(Path.Combine(temp, "data"), candidates[0]);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(temp)!, recursive: true);
        }
    }

    [Fact]
    public void Empty_start_directory_means_current_directory()
    {
        var candidates = DataLocator.Candidates("");
        Assert.Equal(Path.Combine(Directory.GetCurrentDirectory(), "data"), candidates[0]);
        Assert.Equal(DataLocator.Find(Directory.GetCurrentDirectory()), DataLocator.Find(""));
        Assert.Equal(candidates, DataLocator.Candidates("   "));
    }
}
