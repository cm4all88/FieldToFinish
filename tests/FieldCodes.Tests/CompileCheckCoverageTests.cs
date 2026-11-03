using System.Xml.Linq;

namespace FieldCodes.Tests;

/// <summary>
/// The compile checks are only worth something if they compile what the Windows build
/// compiles: every source file of src/FieldCodes.Cad, a new one included, and the Civil 3D
/// stand-ins that make a Civil name clash fail there as it fails against AeccDbMgd.dll.
/// </summary>
public sealed class CompileCheckCoverageTests
{
    private const string PluginWildcard = @"..\..\src\FieldCodes.Cad\**\*.cs";

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FieldToFinish.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static XDocument Project(params string[] parts) => XDocument.Load(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static IEnumerable<XElement> Elements(XDocument project, string name) =>
        project.Descendants().Where(e => e.Name.LocalName == name);

    [Fact]
    public void TheWindowsBuildCompilesEveryFileOfThePlugin()
    {
        // Default compile items, nothing added or removed: every .cs file under src/FieldCodes.Cad,
        // a new one included, is in the Windows build without anyone listing it, and nothing from
        // outside the folder is linked in. The checks below must take the same files.
        var project = Project("src", "FieldCodes.Cad", "FieldCodes.Cad.csproj");
        Assert.DoesNotContain(Elements(project, "EnableDefaultCompileItems"), e => e.Value.Trim().Equals("false", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Elements(project, "Compile"));
    }

    [Theory]
    [InlineData("CadCompileCheck")]
    [InlineData("RealApiCompileCheck")]
    public void EachCompileCheckTakesEveryFileOfThePluginByWildcard(string check)
    {
        var project = Project("tests", check, check + ".csproj");
        var plugin = Elements(project, "Compile").Where(e => (string?)e.Attribute("Include") == PluginWildcard).ToList();
        Assert.Single(plugin);
        // Only build output is left out.
        var exclude = ((string?)plugin[0].Attribute("Exclude") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(new[] { @"..\..\src\FieldCodes.Cad\bin\**", @"..\..\src\FieldCodes.Cad\obj\**" }, exclude.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Empty(Elements(project, "Compile").Where(e => e.Attribute("Remove") != null));
    }

    [Fact]
    public void EveryCompileCheckSeesTheCivilNamesThatClash()
    {
        // CadCompileCheck takes Stubs/*.cs; the other two list the Civil stand-ins one by one.
        var stand = Project("tests", "CadCompileCheck", "CadCompileCheck.csproj");
        Assert.Contains(Elements(stand, "Compile"), e => (string?)e.Attribute("Include") == "Stubs/*.cs");

        foreach (var (parts, prefix) in new[]
                 {
                     (new[] { "tests", "RealApiCompileCheck", "RealApiCompileCheck.csproj" }, @"..\CadCompileCheck\Stubs\"),
                     (new[] { "tests", "SmokeHarnessCompileCheck", "CivilStubs", "CivilStubs.csproj" }, @"..\..\CadCompileCheck\Stubs\"),
                 })
        {
            var includes = Elements(Project(parts), "Compile").Select(e => (string?)e.Attribute("Include")).ToList();
            Assert.Contains(prefix + "Civil.cs", includes);
            Assert.Contains(prefix + "CivilShadows.cs", includes);
            // Written by tools/CivilApiNames on a Civil 3D machine; taken when it is there.
            Assert.Contains(prefix + "CivilApiNames.g.cs", includes);
        }
    }
}
