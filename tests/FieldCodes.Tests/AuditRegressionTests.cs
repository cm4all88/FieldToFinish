using FieldCodes;
using FieldCodes.Editing;
using FieldCodes.Settings;

namespace FieldCodes.Tests;

/// <summary>
/// Regressions pinned from the 2026-08 audit. Each of these was a CONFIRMED
/// defect found in the wild -- the settings-doubling one had already tripled the
/// office settings file on the user's machine before it was caught.
/// </summary>
public sealed class AuditRegressionTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ftf-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ------------------------------------------------- settings list doubling

    [Fact]
    public void SettingsListsSurviveLoadSaveCyclesWithoutGrowing()
    {
        // The constructor seeds defaults; deserializing used to APPEND the file's
        // entries after them, so every load/save cycle doubled the schedule
        // columns and the draw-order layer lists. Three cycles must be identity.
        var path = Path.Combine(TempDir(), FtfSettings.FileName);
        new FtfSettings().Save(path);

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var loaded = FtfSettings.Load(path);

            Assert.Equal(7, loaded.Tags.TableColumns.Count);
            Assert.Equal(4, loaded.DrawOrder.ProtectedLayers.Count);
            Assert.Equal(3, loaded.DrawOrder.MaskableLayers.Count);

            loaded.Save(path);
        }
    }

    // --------------------------------------------------------- NaN validation

    [Fact]
    public void NaNInASettingsFileIsRejectedByValidation()
    {
        // "v <= 0" is false for NaN, so NaN used to pass every check and poison
        // the geometry maths downstream (NaN drip radii, NaN collision boxes).
        var path = Path.Combine(TempDir(), FtfSettings.FileName);
        File.WriteAllText(path,
            "{\"general\":{\"unitsPerFoot\":NaN}," +
            "\"lineLabels\":{\"repeatIntervalFeet\":NaN,\"textHeightPlotted\":NaN}}");

        var settings = FtfSettings.Load(path);
        var problems = new List<string>();
        settings.Validate(problems);

        Assert.Contains(problems, p => p.Contains("units per survey foot"));
        Assert.Contains(problems, p => p.Contains("repeat interval"));
        Assert.Contains(problems, p => p.Contains("text height"));
    }

    // ------------------------------------------------- label format guarding

    [Fact]
    public void ABadNumberFormatInARuleIsCaughtByTheEditor()
    {
        // "{number:Q9}" used to validate clean and then throw on every parse.
        var doc = RuleDocument.Load(
            Path.Combine(AppContext.BaseDirectory, "rules.json"));
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "POLE {number:Q9}";
        doc.ApplyEdit(pole);

        Assert.Contains(doc.Validate(), p => p.Contains("not a valid"));
    }

    [Fact]
    public void ABadNumberFormatAtParseTimeDegradesInsteadOfThrowing()
    {
        // Last line of defence for hand-edited files: the raw value, not a crash.
        var fields = new Dictionary<string, string> { { "number", "12" } };

        var expanded = FieldCodeParser.Expand("POLE {number:Q9}", fields);

        Assert.Equal("POLE 12", expanded);
    }

    // ------------------------------------------------- single-digit AKA alias

    [Fact]
    public void SingleDigitControlAliasesAreRecognized()
    {
        // "XMAG AKA 5" used to fall into the unknown-codes report because the
        // alias pattern required at least two characters after AKA.
        var cfg = RulesConfig.Load(Path.Combine(AppContext.BaseDirectory, "rules.json"));
        var parser = new FieldCodeParser(cfg);

        Assert.Equal("control", parser.Parse("1", "XMAG AKA 5").RuleId);
        Assert.Equal("control", parser.Parse("1", "XMAG AKA 12").RuleId);       // still fine
        Assert.Equal("control", parser.Parse("1", "XHT AKA 1010 & 1014").RuleId);
    }
}
