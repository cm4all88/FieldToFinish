using FieldCodes;
using FieldCodes.Editing;

namespace FieldCodes.Tests;

/// <summary>
/// The editor workflow, end to end at the model level: every path the Point
/// Features page drives, proven without a UI. One test per requirement from the
/// editor specification.
/// </summary>
public sealed class PointFeatureEditingTests
{
    private readonly string _root;
    private readonly string _plugin;
    private readonly string _office;
    private readonly string _drawing;

    public PointFeatureEditingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ftf-editor-" + Path.GetRandomFileName());
        _plugin = Path.Combine(_root, "bundle");
        _office = Path.Combine(_root, "office");
        _drawing = Path.Combine(_root, "job");
        Directory.CreateDirectory(_plugin);
        Directory.CreateDirectory(_drawing);

        // The real shipped rules act as the factory level.
        File.Copy(Path.Combine(AppContext.BaseDirectory, "rules.json"),
                  Path.Combine(_plugin, "rules.json"));
    }

    private RulesResolution Resolve() => RulesStore.Resolve(_drawing, _plugin, _office);

    private static ParsedPoint Parse(RulesConfig cfg, string desc)
        => new FieldCodeParser(cfg).Parse("1", desc);

    // ------------------------------------------------------------------- editing

    [Fact]
    public void EditingAPowerPoleLabel()
    {
        var r = Resolve();
        var doc = RuleDocument.Load(r.ActivePath);
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "UTILITY POLE {number}";
        doc.ApplyEdit(pole);
        Assert.Empty(doc.Validate());
        doc.Save(r.SaveTarget);

        var parsed = Parse(RulesConfig.Load(Resolve().ActivePath), "PP 1234");
        Assert.Equal("UTILITY POLE 1234", parsed.LabelText);
        Assert.Equal("V-UTIL-POWR-TEXT", parsed.LabelLayer);
    }

    [Fact]
    public void EditingASignLabel()
    {
        var r = Resolve();
        var doc = RuleDocument.Load(r.ActivePath);
        var sign = doc.GetEditable("sn-sign");
        sign.LabelFormat = "SIGN (FIELD LOCATED)";
        doc.ApplyEdit(sign);
        Assert.Empty(doc.Validate());
        doc.Save(r.SaveTarget);

        var parsed = Parse(RulesConfig.Load(Resolve().ActivePath), "SN 135");
        Assert.Equal("SIGN (FIELD LOCATED)", parsed.LabelText);
        Assert.Equal(315.0, parsed.RotationDegrees);     // rotation untouched
    }

    [Fact]
    public void DisablingAFeature()
    {
        var r = Resolve();
        var doc = RuleDocument.Load(r.ActivePath);
        var pole = doc.GetEditable("pole");
        pole.Enabled = false;
        doc.ApplyEdit(pole);
        doc.Save(r.SaveTarget);

        var cfg = RulesConfig.Load(Resolve().ActivePath);
        Assert.True(Parse(cfg, "PP 1234").Unhandled);
        Assert.Equal("tree", Parse(cfg, "CON 18 . 25").RuleId);   // trees untouched
    }

    [Fact]
    public void ApplyWithoutSaveChangesNothingOnDisk()
    {
        var r = Resolve();
        var before = File.ReadAllText(r.ActivePath);

        var doc = RuleDocument.Load(r.ActivePath);
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "STAGED ONLY {number}";
        doc.ApplyEdit(pole);                              // Apply

        // The staged document sees it; no file anywhere does.
        Assert.Equal("STAGED ONLY 9",
            Parse(doc.CompileCandidate(), "PP 9").LabelText);
        Assert.Equal(before, File.ReadAllText(r.ActivePath));
        Assert.False(File.Exists(Path.Combine(_office, "rules.json")));
    }

    [Fact]
    public void CancelIsReloadingTheEditableFromTheDocument()
    {
        var doc = RuleDocument.Load(Resolve().ActivePath);

        // Fields were changed in the UI but never applied; Cancel re-reads the
        // document, which still holds the original values.
        var reloaded = doc.GetEditable("pole");
        Assert.Equal("POLE {number}", reloaded.LabelFormat);
        Assert.Equal("P", reloaded.TagPrefix);
        Assert.True(reloaded.Enabled);
    }

    [Fact]
    public void SaveAndReloadRoundTrip()
    {
        var r = Resolve();
        var doc = RuleDocument.Load(r.ActivePath);
        var pole = doc.GetEditable("pole");
        pole.Description = "Primary Power Pole";
        pole.TagPrefix = "PW";
        doc.ApplyEdit(pole);
        doc.Save(r.SaveTarget);

        // A fresh resolve-and-load, exactly what the page's Reload does.
        var again = RuleDocument.Load(Resolve().ActivePath).GetEditable("pole");
        Assert.Equal("Primary Power Pole", again.Description);
        Assert.Equal("PW", again.TagPrefix);
    }

    // -------------------------------------------------------------- persistence

    [Fact]
    public void FirstEditCopiesFactoryToOffice()
    {
        var before = Resolve();
        Assert.Equal(RulesSource.Factory, before.Source);

        var doc = RuleDocument.Load(before.ActivePath);
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "MY POLE {number}";
        doc.ApplyEdit(pole);
        doc.Save(before.SaveTarget);

        var after = Resolve();
        Assert.Equal(RulesSource.Office, after.Source);
        Assert.NotEqual(before.ActivePath, after.ActivePath);

        // Factory byte-for-byte untouched; office carries the edit AND the comments.
        var factory = File.ReadAllText(Path.Combine(_plugin, "rules.json"));
        Assert.Contains("\"POLE {number}\"", factory);
        var office = File.ReadAllText(after.ActivePath);
        Assert.Contains("MY POLE", office);
        Assert.Contains("//point-features", office);
    }

    [Fact]
    public void ADrawingOverridePersistsInTheDrawingFolder()
    {
        // A job diverges: its rules live beside the drawing.
        File.Copy(Path.Combine(_plugin, "rules.json"), Path.Combine(_drawing, "rules.json"));

        var r = Resolve();
        Assert.Equal(RulesSource.Drawing, r.Source);

        var doc = RuleDocument.Load(r.ActivePath);
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "JOB POLE {number}";
        doc.ApplyEdit(pole);
        doc.Save(r.SaveTarget);

        // The edit stayed with the job; office was never created, factory untouched.
        Assert.Contains("JOB POLE", File.ReadAllText(Path.Combine(_drawing, "rules.json")));
        Assert.False(File.Exists(Path.Combine(_office, "rules.json")));
        Assert.Contains("\"POLE {number}\"",
            File.ReadAllText(Path.Combine(_plugin, "rules.json")));
        Assert.Equal(RulesSource.Drawing, Resolve().Source);
    }

    // ------------------------------------------------------------------ preview

    [Fact]
    public void PreviewUsesStagedChangesBeforeAnySave()
    {
        var doc = RuleDocument.Load(Resolve().ActivePath);
        var pole = doc.GetEditable("pole");
        pole.LabelFormat = "STAGED POLE {number}";
        doc.ApplyEdit(pole);

        var preview = FeaturePreview.Render(
            Parse(doc.CompileCandidate(), "PP 1234"), "Power Pole");

        Assert.Contains("Label: STAGED POLE 1234", preview);
    }

    [Fact]
    public void ThePolePreviewReadsAsSpecified()
    {
        var preview = FeaturePreview.Render(
            Parse(RulesConfig.Load(Resolve().ActivePath), "PP 1234"), "Power Pole");

        Assert.StartsWith("Power Pole", preview);
        Assert.Contains("Civil 3D symbol: Existing", preview);
        Assert.Contains("FTF action: Label", preview);
        Assert.Contains("Label: POLE 1234", preview);
        Assert.Contains("Layer: V-UTIL-POWR-TEXT", preview);
        Assert.Contains("Tag: P#", preview);
        Assert.Contains("Rotation: None", preview);
    }

    [Fact]
    public void TheSignPreviewReadsAsSpecified()
    {
        var preview = FeaturePreview.Render(
            Parse(RulesConfig.Load(Resolve().ActivePath), "SN 135"), "Sign");

        Assert.StartsWith("Sign", preview);
        Assert.Contains("Civil 3D symbol: Existing", preview);
        Assert.Contains("FTF action: Rotate existing marker + Label", preview);
        Assert.Contains("Rotation: 315°", preview);
        Assert.Contains("Label: SIGN", preview);
        Assert.Contains("Tag: S#", preview);
    }

    [Fact]
    public void PreviewNeverClaimsToCreateGeometry()
    {
        var cfg = RulesConfig.Load(Resolve().ActivePath);
        foreach (var desc in new[] { "PP 1234", "SN 135", "CON 18 . 25", "CB 4", "PP" })
        {
            var preview = FeaturePreview.Render(Parse(cfg, desc), null);
            Assert.DoesNotContain("create", preview, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("insert", preview, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ----------------------------------------------------------- the hard boundary

    [Fact]
    public void TheEntireEditingModelCannotTouchCivil3d()
    {
        // The structural guarantee behind "the editor never creates Civil 3D
        // geometry": everything it is made of lives in FieldCodes, an assembly that
        // does not reference AutoCAD or Civil 3D at all. There is no code path from
        // the editor to a drawing.
        var references = typeof(RuleDocument).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        Assert.DoesNotContain(references,
            name => name.StartsWith("Ac", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Aecc", StringComparison.OrdinalIgnoreCase));
    }
}
