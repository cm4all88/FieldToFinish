using System.Xml.Linq;

namespace FieldCodes.Tests;

/// <summary>
/// The TRACE development install must never replace or alter the internal
/// FieldToFinish bundle, and the baseline script must never install after a failed check.
/// These are static checks of the files (the PowerShell itself is only run on Windows).
/// </summary>
public sealed class BundleSeparationTests
{
    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FieldToFinish.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    // The internal manifest has "--" inside a comment, which strict XML forbids. It is
    // deliberately left unchanged, so comments are stripped before parsing it here.
    private static XElement Manifest(string bundle)
    {
        var text = File.ReadAllText(Path.Combine(Repo(), "deploy", bundle, "PackageContents.xml"));
        text = System.Text.RegularExpressions.Regex.Replace(text, "<!--.*?-->", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        return XDocument.Parse(text).Root!;
    }

    [Fact]
    public void TheTraceManifestIsStrictlyValidXml()
        => XDocument.Load(Path.Combine(Repo(), "deploy", "TRACE.bundle", "PackageContents.xml"));

    private static string Text(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Repo() }.Concat(parts).ToArray()));

    [Fact]
    public void TheTwoManifestsAreIndependentProducts()
    {
        var trace = Manifest("TRACE.bundle");
        var internalBundle = Manifest("FieldToFinish.bundle");

        Assert.Equal("TRACE", (string?)trace.Attribute("Name"));
        Assert.Equal("Field to Finish", (string?)internalBundle.Attribute("Name"));
        Assert.NotEqual((string?)trace.Attribute("ProductCode"), (string?)internalBundle.Attribute("ProductCode"));
        Assert.NotEqual((string?)trace.Attribute("UpgradeCode"), (string?)internalBundle.Attribute("UpgradeCode"));
        Assert.NotEqual(
            (string?)trace.Descendants("ComponentEntry").Single().Attribute("AppName"),
            (string?)internalBundle.Descendants("ComponentEntry").Single().Attribute("AppName"));
    }

    [Fact]
    public void TheInternalManifestIsUnchanged()
    {
        var internalBundle = Manifest("FieldToFinish.bundle");
        Assert.Equal("Parametrix", (string?)internalBundle.Attribute("Author"));
        var entry = internalBundle.Descendants("ComponentEntry").Single();
        Assert.Equal("FieldToFinish", (string?)entry.Attribute("AppName"));
        Assert.Equal("./Contents/2024/FieldCodes.Cad.dll", (string?)entry.Attribute("ModuleName"));
    }

    [Fact]
    public void TheTraceManifestHasNoCompanyContent()
    {
        var text = Text("deploy", "TRACE.bundle", "PackageContents.xml");
        Assert.DoesNotContain("Parametrix", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PMX", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DevInstallerPicksTheBundleByEdition()
    {
        var script = Text("deploy", "install-dev.ps1");
        Assert.Contains("$bundleName = if ($Edition -eq 'Parametrix') { 'FieldToFinish.bundle' } else { 'TRACE.bundle' }", script);
        // The TRACE path must refuse to proceed when the other edition's bundle exists.
        Assert.Contains("Write-Error (\"$otherName is installed", script);
        // And no code path removes or overwrites the other bundle.
        Assert.DoesNotContain("Remove-Item $otherFound", script);
        Assert.DoesNotContain("Rename-Item $otherFound", script);
    }

    [Fact]
    public void BaselineStopsBeforeInstallingWhenAnyCheckFails()
    {
        var script = Text("deploy", "Run-TraceBaseline.ps1");
        var install = script.IndexOf("Installing the TRACE development bundle", StringComparison.Ordinal);
        Assert.True(install > 0);

        foreach (var guard in new[]
        {
            "Stop-Run 'Build failed.'",
            "Stop-Run 'The build does not carry the neutral TRACE rules.",
            "Stop-Run 'Unit tests failed.",
            "Stop-Run 'An existing FieldToFinish bundle would conflict with TRACE.",
        })
        {
            var at = script.IndexOf(guard, StringComparison.Ordinal);
            Assert.True(at > 0, "missing guard: " + guard);
            Assert.True(at < install, "guard comes after the install step: " + guard);
        }

        Assert.Contains("NOT A VALID BASELINE", script);
    }

    [Fact]
    public void BaselineInstallsOnlyTheTraceBundle()
    {
        var script = Text("deploy", "Run-TraceBaseline.ps1");
        Assert.Contains("ApplicationPlugins\\TRACE.bundle'", script);
        Assert.DoesNotContain("Remove-Item", script.Substring(script.IndexOf("FieldToFinish.bundle", StringComparison.Ordinal)).Split("Stop-Run 'An existing")[0]);
    }
}
