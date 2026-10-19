using FieldCodes.Compare;

namespace FieldCodes.Tests;

/// <summary>The factory profile shipped in config/compare-profiles is generated from
/// CreateDefault() and must never drift from it. Set FTF_WRITE_DEFAULT_PROFILE=1 to
/// regenerate the file from this test.</summary>
public sealed class ShippedProfileTests
{
    private static string RepoFile()
    {
        // tests/FieldCodes.Tests/bin/Debug/net8.0 -> repo root
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FieldToFinish.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "config", "compare-profiles", "Default.json");
    }

    [Fact]
    public void ShippedDefaultProfileMatchesTheBuiltInOne()
    {
        var path = RepoFile();
        var expected = CompareProfile.CreateDefault().ToJson();
        if (Environment.GetEnvironmentVariable("FTF_WRITE_DEFAULT_PROFILE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected);
        }
        Assert.True(File.Exists(path), "config/compare-profiles/Default.json is missing; run the tests with FTF_WRITE_DEFAULT_PROFILE=1 to generate it");
        var shipped = CompareProfile.Load(path);
        var problems = new List<string>();
        shipped.Validate(problems);
        Assert.Empty(problems);
        Assert.Equal(expected, shipped.ToJson());
    }
}
