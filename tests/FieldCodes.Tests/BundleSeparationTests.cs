using System.Xml.Linq;

namespace FieldCodes.Tests;

/// <summary>
/// The TRACE development install must never replace or alter the internal FieldToFinish
/// bundle, the two editions must have different assembly identities, and the baseline
/// script must verify its destination and never install after a failed check. These are
/// static checks of the files (the PowerShell itself is only run on Windows).
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

    private static string Text(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Repo() }.Concat(parts).ToArray()));

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    [Fact]
    public void TheTraceManifestIsStrictlyValidXml()
        => XDocument.Load(Path.Combine(Repo(), "deploy", "TRACE.bundle", "PackageContents.xml"));

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
        Assert.NotEqual(
            (string?)trace.Descendants("ComponentEntry").Single().Attribute("ModuleName"),
            (string?)internalBundle.Descendants("ComponentEntry").Single().Attribute("ModuleName"));
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
    public void TheTraceManifestLoadsTheTraceAssemblyAndHasNoCompanyContent()
    {
        var entry = Manifest("TRACE.bundle").Descendants("ComponentEntry").Single();
        Assert.Equal("./Contents/2024/TRACE.Cad.dll", (string?)entry.Attribute("ModuleName"));

        var text = Text("deploy", "TRACE.bundle", "PackageContents.xml");
        Assert.DoesNotContain("Parametrix", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PMX", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FieldCodes", text);
    }

    [Fact]
    public void TheEditionsHaveDifferentAssemblyIdentities()
    {
        var props = Text("Directory.Build.props");
        Assert.Contains("Condition=\"'$(TraceEdition)' == ''\">Default</TraceEdition>", props);

        var cad = Text("src", "FieldCodes.Cad", "FieldCodes.Cad.csproj");
        Assert.Contains("Condition=\"'$(TraceEdition)' == 'Parametrix'\">FieldCodes.Cad</AssemblyName>", cad);
        Assert.Contains("Condition=\"'$(TraceEdition)' != 'Parametrix'\">TRACE.Cad</AssemblyName>", cad);

        var core = Text("src", "FieldCodes", "FieldCodes.csproj");
        Assert.Contains("Condition=\"'$(TraceEdition)' == 'Parametrix'\">FieldCodes</AssemblyName>", core);
        Assert.Contains("Condition=\"'$(TraceEdition)' != 'Parametrix'\">TRACE.Core</AssemblyName>", core);
    }

    [Fact]
    public void DevInstallerVerifiesTheDestinationBeforeCopyingAnything()
    {
        var script = Text("deploy", "install-dev.ps1");
        Assert.Contains("$bundleName = if ($Edition -eq 'Parametrix') { 'FieldToFinish.bundle' } else { 'TRACE.bundle' }", script);

        var verified = script.IndexOf("Destination verified:", StringComparison.Ordinal);
        var firstCopy = script.IndexOf("Copy-Item", StringComparison.Ordinal);
        var firstRemove = script.IndexOf("Remove-Item", StringComparison.Ordinal);
        Assert.True(verified > 0);
        Assert.True(verified < firstCopy, "destination must be verified before the first copy");
        Assert.True(verified < firstRemove, "destination must be verified before the first removal");

        Assert.Contains("must never write to a FieldToFinish folder", script);
        Assert.Contains("is not a '$expectedName' bundle", script);
    }

    [Fact]
    public void DevInstallerNeverTouchesTheOtherEditionsBundleOrFiles()
    {
        var script = Text("deploy", "install-dev.ps1");
        // The other bundle is only warned about.
        Assert.DoesNotContain("Remove-Item $otherFound", script);
        Assert.DoesNotContain("Rename-Item", script);
        Assert.Contains("is also installed (it is NOT touched)", script);
        // Leftover build files from the other edition are never copied into a bundle.
        Assert.Contains("'^TRACE\\.'", script);
        Assert.Contains("'^FieldCodes(\\.|$)'", script);
        // The module checked for is the edition's own.
        Assert.Contains("'FieldCodes.Cad.dll' } else { 'TRACE.Cad.dll'", script);
    }

    [Fact]
    public void BaselineVerifiesTheDestinationAndStopsBeforeInstallingWhenAnyCheckFails()
    {
        var script = Text("deploy", "Run-TraceBaseline.ps1");
        var install = script.IndexOf("Installing the TRACE development bundle", StringComparison.Ordinal);
        Assert.True(install > 0);

        foreach (var guard in new[]
        {
            "Stop-Run 'Build failed.'",
            "Stop-Run 'The build does not carry the neutral TRACE rules.",
            "Stop-Run 'Unit tests failed.",
            "Stop-Run 'The install destination is not safe.",
        })
        {
            var at = script.IndexOf(guard, StringComparison.Ordinal);
            Assert.True(at > 0, "missing guard: " + guard);
            Assert.True(at < install, "guard comes after the install step: " + guard);
        }

        Assert.Contains("NOT A VALID BASELINE", script);
        Assert.Contains("(Split-Path -Leaf $bundle) -eq 'TRACE.bundle'", script);
    }

    [Fact]
    public void BaselineProvesAnExistingFieldToFinishBundleIsUntouched()
    {
        var script = Text("deploy", "Run-TraceBaseline.ps1");

        // Fingerprinted before the build, compared after the install.
        var before = script.IndexOf("$ftfBefore[$f] = Get-BundleFingerprint $f", StringComparison.Ordinal);
        var build = script.IndexOf("Building TRACE", StringComparison.Ordinal);
        var after = script.IndexOf("Existing FieldToFinish bundle untouched", StringComparison.Ordinal);
        var install = script.IndexOf("Installing the TRACE development bundle", StringComparison.Ordinal);
        Assert.True(before > 0 && before < build);
        Assert.True(after > install);

        // The baseline never renames, copies or removes anything of the other bundle.
        Assert.DoesNotContain("Rename-Item", script);
        Assert.Equal(1, Count(script, "Copy-Item"));                       // only the smoke-test log, into the logs folder
        Assert.Contains("Copy-Item -LiteralPath $draftLog -Destination (Join-Path $logDir", script);
        Assert.Equal(1, Count(script, "Remove-Item"));
        Assert.Contains("Remove-Item -LiteralPath $zip", script);

        // The expected files: the TRACE assemblies, and no internal-edition files inside the bundle.
        Assert.Contains("TRACE.Cad.dll", script);
        Assert.Contains("TRACE.Core.dll", script);
        Assert.Contains("No internal-edition files in the TRACE bundle", script);
    }

    [Fact]
    public void IsolationScriptOnlyEverRenamesTheOneFolder()
    {
        var script = Text("deploy", "Switch-TraceTestMode.ps1");
        Assert.Equal(2, Count(script, "Rename-Item"));            // -Begin and -End, nothing else
        foreach (var forbidden in new[] { "Remove-Item", "Copy-Item", "Move-Item", "Set-Content", "Add-Content", "Clear-Content" })
            Assert.DoesNotContain(forbidden, script);
        Assert.Contains("FieldToFinish.bundle.paused", script);
        Assert.Contains("Refusing: '$to' already exists", script);           // never renames over something
        Assert.Contains("IDENTICAL", script);                                 // proves the restore
        Assert.Contains("Already set aside", script);                         // a hand-renamed folder is fingerprinted in place
        Assert.Contains("Get-Process -Name acad", script);                    // not while Civil 3D is open
    }
}
