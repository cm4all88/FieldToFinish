using FieldCodes.Editing;
using FieldCodes.Orient;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Tests;

/// <summary>
/// The Symbols and Rotations settings for how FTFORIENT weighs road lines and what a feature's
/// fallback rule may do: read from the office's rules file (with the built-in defaults when it
/// says nothing), written only when changed, checked, and reaching the command's standard.
/// </summary>
public sealed class OrientationSettingsEditingTests
{
    private static readonly string RulesPath = Path.Combine(AppContext.BaseDirectory, "rules.json");

    [Fact]
    public void TheShippedRulesGiveTheBuiltInDefaultsAndWriteNone()
    {
        var doc = RuleDocument.Load(RulesPath);
        var a = doc.GetOrientationApply();
        Assert.Equal(80, a.RoadSearchFeet, 6);
        Assert.Equal(25, a.MaxExtensionFeet, 6);
        Assert.Equal(20, a.LocalTangentFeet, 6);
        Assert.Equal(15, a.AlignmentToleranceDegrees, 6);
        Assert.Equal("Approach", a.IntersectionHandling);
        Assert.Equal("MeasuredReplaces", a.HandTurned);
        Assert.Equal("ReviewOnly", a.QcGraphics);
        Assert.False(a.QcShowInferredConnections);
        Assert.Equal("Review", a.Fallbacks["StreetLight"]);
        Assert.Equal("Review", a.Fallbacks["SignalPole"]);
        Assert.Equal("Apply", a.Fallbacks["Hydrant"]);
        Assert.Equal("Apply", a.Fallbacks["Sign"]);
        Assert.Equal(1.0, a.Reliability["Centerline"], 6);
        Assert.Equal(0.9, a.Reliability["LaneLine"], 6);
        Assert.Equal(0.85, a.Reliability["Edge"], 6);
        Assert.Equal(0.5, a.Reliability["RoadContext"], 6);

        doc.ApplyOrientationApply(a);                       // nothing changed: nothing written
        var text = doc.Render();
        foreach (var name in new[] { "maxExtensionFeet", "localTangentFeet", "alignmentToleranceDegrees", "fallbacks", "roadReferences", "qcGraphics", "handTurned" })
            Assert.DoesNotContain(name, text);
    }

    [Fact]
    public void ChangedSettingsAreWrittenAndReachFtfOrient()
    {
        var doc = RuleDocument.Load(RulesPath);
        var a = doc.GetOrientationApply();
        a.MaxExtensionFeet = 40;
        a.LocalTangentFeet = 30;
        a.AlignmentToleranceDegrees = 10;
        a.IntersectionHandling = "Review";
        a.HandTurned = "Keep";
        a.QcGraphics = "All";
        a.QcShowInferredConnections = true;
        a.Fallbacks["Hydrant"] = "Review";
        a.Fallbacks["StreetLight"] = "Apply";
        a.Reliability["Edge"] = 0.7;
        doc.ApplyOrientationApply(a);
        Assert.Empty(doc.Validate());

        var std = doc.CompileCandidate().Orientation;
        Assert.Equal(40, std.MaxExtensionFeet, 6);
        Assert.Equal(30, std.LocalTangentFeet, 6);
        Assert.Equal(10, std.AlignmentToleranceDegrees, 6);
        Assert.Equal(IntersectionHandling.Review, std.IntersectionHandling);
        Assert.Equal(HandTurnedRotation.Keep, std.HandTurned);
        Assert.Equal(QcGraphicsMode.All, std.QcGraphics);
        Assert.True(std.QcShowInferredConnections);
        Assert.Equal(OrientFallback.Review, std.FallbackFor(OrientKind.Hydrant));
        Assert.Equal(OrientFallback.Apply, std.FallbackFor(OrientKind.StreetLight));
        Assert.Equal(OrientFallback.Review, std.FallbackFor(OrientKind.SignalPole));     // written whole: the rest kept
        Assert.Equal(0.7, std.ReliabilityOf(LineRole.Edge), 6);
        Assert.Equal(1.0, std.ReliabilityOf(LineRole.Centerline), 6);                    // written whole
        Assert.Equal(1.0, std.ReliabilityOf(LineRole.OneWay), 6);                        // one-way lines follow centre lines

        var again = RuleDocument.FromText(doc.Render()).GetOrientationApply();
        Assert.Equal("Review", again.Fallbacks["Hydrant"]);
        Assert.Equal(0.7, again.Reliability["Edge"], 6);
    }

    [Fact]
    public void OutOfRangeSettingsAreRefusedByName()
    {
        var doc = RuleDocument.Load(RulesPath);
        var a = doc.GetOrientationApply();
        a.RoadSearchFeet = 0;
        a.MaxExtensionFeet = 500;
        a.AlignmentToleranceDegrees = 120;
        a.QcGraphics = "Sometimes";
        a.Fallbacks["Hydrant"] = "Guess";
        a.Reliability["Edge"] = 2;
        doc.ApplyOrientationApply(a);
        var problems = doc.Validate();
        Assert.Contains(problems, p => p.Contains("between 1 and 500 ft"));
        Assert.Contains(problems, p => p.Contains("between 0 and 200 ft"));
        Assert.Contains(problems, p => p.Contains("between 1 and 90 degrees"));
        Assert.Contains(problems, p => p.Contains("qcGraphics must be one of"));
        Assert.Contains(problems, p => p.Contains("fallback for Hydrant must be Apply, Review or Leave"));
        Assert.Contains(problems, p => p.Contains("the trust in Edge lines must be between 0 and 1"));
        Assert.All(problems, p => Assert.StartsWith("Symbols and Rotations: ", p));
    }

    // ------------------------------------------------------------ road lines, lists, distances, QC

    private static RuleDocument WithOrientation(string json)
    {
        var root = JObject.Parse(File.ReadAllText(RulesPath));
        var o = (JObject)root["orientation"]!;
        foreach (var p in JObject.Parse(json).Properties()) o[p.Name] = p.Value;
        return RuleDocument.FromText(root.ToString());
    }

    /// <summary>What the page does with what it shows: every list through its text box, every line through its grid row.</summary>
    private static EditableOrientationDetails AsThePageReadsIt(EditableOrientationDetails d)
    {
        d.Lines = d.Lines.Select(l => new EditableLineRole
        {
            SourceIndex = l.SourceIndex, Role = l.Role,
            Layers = EditableLineRole.ParseLayers(l.LayersText), Codes = EditableLineRole.ParseCodes(l.CodesText)
        }).ToList();
        d.StopSignCodes = EditableOrientationDetails.ParseList(string.Join(Environment.NewLine, d.StopSignCodes));
        d.StopSignWords = EditableOrientationDetails.ParseList(string.Join(Environment.NewLine, d.StopSignWords));
        d.LuminaireCodes = EditableOrientationDetails.ParseList(string.Join(Environment.NewLine, d.LuminaireCodes));
        return d;
    }

    [Fact]
    public void TheShippedRulesGivePmxsLinesListsAndDistancesAndWriteNone()
    {
        var doc = RuleDocument.Load(RulesPath);
        var d = doc.GetOrientationDetails();
        var pmx = OrientationStandard.Pmx();

        Assert.True(d.LinesBuiltIn);
        Assert.Equal(pmx.Lines.Select(l => l.Role.ToString()), d.Lines.Select(l => l.Role));
        Assert.Equal(pmx.Lines.Select(l => string.Join("|", l.Layers)), d.Lines.Select(l => string.Join("|", l.Layers)));
        Assert.Equal(pmx.Lines.Select(l => string.Join("|", l.Codes)), d.Lines.Select(l => string.Join("|", l.Codes)));
        Assert.All(d.Lines, l => Assert.Equal(-1, l.SourceIndex));
        Assert.True(d.StopSignCodesBuiltIn && d.StopSignWordsBuiltIn && d.LuminaireCodesBuiltIn);
        Assert.Equal(new[] { "SNS", "SNY" }, d.StopSignCodes);
        Assert.Equal(new[] { "STOP", "YIELD", "ALL WAY", "ALLWAY" }, d.StopSignWords);
        Assert.Equal(new[] { "LUM", "LTA", "LTHEAD" }, d.LuminaireCodes);
        Assert.Equal(150, d.IntersectionSearchFeet, 6);
        Assert.Equal(60, d.StopBarSearchFeet, 6);
        Assert.Equal(1.5, d.SamePostFeet, 6);
        Assert.Equal(20, d.ValveClusterFeet, 6);
        Assert.Equal(60, d.HydrantSearchFeet, 6);
        Assert.Equal(5, d.MainSnapFeet, 6);
        Assert.Equal(3, d.PanelSnapFeet, 6);
        Assert.Equal(40, d.LuminaireSearchFeet, 6);
        Assert.Equal("FTF-QC-ORIENT", d.QcLayer);
        Assert.Equal("FTF-QC-ORIENT-INFERRED", d.QcInferredLayer);
        Assert.Equal(0.3, d.QcArrowPlotted, 6);
        Assert.Equal(0.06, d.QcTextPlotted, 6);

        // The built-in values are what a rules file with no orientation section gets.
        var none = EditableOrientationDetails.BuiltIn();
        Assert.Equal(d.Lines.Count, none.Lines.Count);
        Assert.Equal(new OrientationStandard().QcTextPlotted, none.QcTextPlotted, 6);

        var before = doc.Render();
        doc.ApplyOrientationDetails(AsThePageReadsIt(doc.GetOrientationDetails()));      // nothing changed: nothing written
        Assert.Equal(before, doc.Render());
        Assert.Empty(doc.Validate());
    }

    [Fact]
    public void EditedLinesListsAndDistancesReachFtfOrient()
    {
        var doc = RuleDocument.Load(RulesPath);
        var d = doc.GetOrientationDetails();
        d.Lines.Insert(0, new EditableLineRole { Role = "Centerline", Layers = EditableLineRole.ParseLayers("OFFICE-CL, office road cl*"), Codes = EditableLineRole.ParseCodes("ocl") });
        d.Lines.Single(l => l.Role == "Crosswalk").Codes = new List<string> { "XW", "XWALK" };
        d.StopSignCodes = EditableOrientationDetails.ParseList("stopsn\r\nSNY");
        d.StopSignWords = EditableOrientationDetails.ParseList("STOP\nALTO\n\n");
        d.LuminaireCodes = new List<string> { "LUMX" };
        d.IntersectionSearchFeet = 120;
        d.StopBarSearchFeet = 45;
        d.SamePostFeet = 2.5;
        d.ValveClusterFeet = 30;
        d.HydrantSearchFeet = 75;
        d.MainSnapFeet = 7.5;
        d.PanelSnapFeet = 4;
        d.LuminaireSearchFeet = 50;
        d.QcLayer = "OFFICE-QC";
        d.QcInferredLayer = "OFFICE-QC-INFERRED";
        d.QcArrowPlotted = 0.5;
        d.QcTextPlotted = 0.08;
        doc.ApplyOrientationDetails(d);
        Assert.Empty(doc.Validate());
        Assert.Equal(Enumerable.Range(0, d.Lines.Count), d.Lines.Select(l => l.SourceIndex));      // rows now sit in the file's list

        var std = doc.CompileCandidate().Orientation;
        Assert.Equal(LineRole.Centerline, std.RoleFor("OFFICE-CL", null));
        Assert.Equal(LineRole.Centerline, std.RoleFor("OFFICE ROAD CL 2", null));
        Assert.Equal(LineRole.Centerline, std.RoleFor("0", "OCL3"));
        Assert.Equal(LineRole.Crosswalk, std.RoleFor("0", "XWALK"));
        Assert.Equal(LineRole.Edge, std.RoleFor("V-SURF-ASPH-E", null));              // the rest of PMX's list came with it
        Assert.Equal(OrientationStandard.Pmx().Lines.Count + 1, std.Lines.Count);
        Assert.Equal(new[] { "STOPSN", "SNY" }, std.StopSignCodes);                    // replaced, not added to
        Assert.True(std.IsStopSign(new OrientFeature { Code = "STOPSN" }));
        Assert.False(std.IsStopSign(new OrientFeature { Code = "SNS" }));
        Assert.True(std.IsStopSign(new OrientFeature { Code = "SN", Description = "SN ALTO" }));
        Assert.False(std.IsStopSign(new OrientFeature { Code = "SN", Description = "SN YIELD" }));
        Assert.True(std.IsLuminaire("LUMX"));
        Assert.False(std.IsLuminaire("LUM"));
        Assert.Equal(120, std.IntersectionSearchFeet, 6);
        Assert.Equal(45, std.StopBarSearchFeet, 6);
        Assert.Equal(2.5, std.SamePostFeet, 6);
        Assert.Equal(30, std.ValveClusterFeet, 6);
        Assert.Equal(75, std.HydrantSearchFeet, 6);
        Assert.Equal(7.5, std.MainSnapFeet, 6);
        Assert.Equal(4, std.PanelSnapFeet, 6);
        Assert.Equal(50, std.LuminaireSearchFeet, 6);
        Assert.Equal("OFFICE-QC", std.QcLayer);
        Assert.Equal("OFFICE-QC-INFERRED", std.QcInferredLayer);
        Assert.Equal(0.5, std.QcArrowPlotted, 6);
        Assert.Equal(0.08, std.QcTextPlotted, 6);

        var text = doc.Render();
        Assert.Contains("\"//lines\"", text);
        var again = RuleDocument.FromText(text).GetOrientationDetails();
        Assert.False(again.LinesBuiltIn || again.StopSignCodesBuiltIn || again.StopSignWordsBuiltIn || again.LuminaireCodesBuiltIn);
        Assert.Equal("OFFICE-CL|office road cl*", string.Join("|", again.Lines[0].Layers));
        Assert.Equal(new[] { "STOP", "ALTO" }, again.StopSignWords);

        // Read back and put back as the page would: nothing more is written.
        var reread = RuleDocument.FromText(text);
        reread.ApplyOrientationDetails(AsThePageReadsIt(reread.GetOrientationDetails()));
        Assert.Equal(text, reread.Render());
    }

    [Fact]
    public void ALineListInTheFileReplacesPmxsWholeList()
    {
        var doc = WithOrientation("{ \"lines\": [ { \"role\": \"Edge\", \"layers\": [ \"OFFICE-EP\" ], \"codes\": [] } ] }");
        var std = doc.CompileCandidate().Orientation;
        var only = Assert.Single(std.Lines);
        Assert.Equal(new[] { "OFFICE-EP" }, only.Layers);
        Assert.Empty(only.Codes);
        Assert.Equal(LineRole.Other, std.RoleFor("V-ALGN-CNTR-E", "CL"));
        Assert.False(doc.GetOrientationDetails().LinesBuiltIn);
    }

    [Fact]
    public void PmxsListsComeBackWhenPutBackOrEmptied()
    {
        var doc = RuleDocument.Load(RulesPath);
        var shipped = doc.Render();
        var d = doc.GetOrientationDetails();
        d.Lines.RemoveAt(0);
        d.StopSignCodes = new List<string> { "STOPSN" };
        d.LuminaireCodes = new List<string> { "LUMX" };
        doc.ApplyOrientationDetails(d);
        Assert.Contains("\"lines\"", doc.Render());

        // Use the PMX List: the same rules as PMX's are PMX's, and leave the file.
        d = doc.GetOrientationDetails();
        d.Lines = OrientationStandard.Pmx().Lines.Select(r => EditableLineRole.From(r, -1)).ToList();
        d.StopSignCodes = OrientationStandard.Pmx().StopSignCodes.ToList();
        d.LuminaireCodes = new List<string>();                                       // an empty box is PMX's list
        doc.ApplyOrientationDetails(d);
        Assert.Equal(shipped, doc.Render());
        var back = doc.GetOrientationDetails();
        Assert.True(back.LinesBuiltIn && back.StopSignCodesBuiltIn && back.LuminaireCodesBuiltIn);
        Assert.All(d.Lines, l => Assert.Equal(-1, l.SourceIndex));
    }

    [Fact]
    public void AFilesOwnLineRulesKeepTheirCommentsWhenEdited()
    {
        var doc = WithOrientation("{ \"lines\": [ { \"//\": \"office note\", \"role\": \"Edge\", \"layers\": [ \"OFFICE-EP\" ], \"codes\": [ \"EP\" ], \"future\": 1 }," +
                                  " { \"role\": \"Centerline\", \"layers\": [], \"codes\": [ \"CL\" ] } ] }");
        var d = doc.GetOrientationDetails();
        Assert.Equal(new[] { 0, 1 }, d.Lines.Select(l => l.SourceIndex));
        var edge = d.Lines[0];
        d.Lines.RemoveAt(0);
        d.Lines.Add(edge);                                     // moved down
        edge.Codes.Add("EOP");
        doc.ApplyOrientationDetails(d);

        var lines = (JArray)JObject.Parse(doc.Render())["orientation"]!["lines"]!;
        Assert.Equal("Centerline", (string?)lines[0]["role"]);
        Assert.Equal("office note", (string?)lines[1]["//"]);
        Assert.Equal(1, (int)lines[1]["future"]!);
        Assert.Equal(new[] { "EP", "EOP" }, lines[1]["codes"]!.Select(t => (string)t!));
        Assert.Equal(new[] { 0, 1 }, d.Lines.Select(l => l.SourceIndex));
    }

    [Fact]
    public void ListsWrittenWronglyArePutRightByTheFirstEditOnThePage()
    {
        var doc = WithOrientation("{ \"stopSignCodes\": \"SNS\", \"luminaireCodes\": [ \"LUM\", 5 ], \"lines\": [ { \"role\": \"Edge\", \"layers\": \"X-EP\" } ] }");
        Assert.NotEmpty(doc.Validate());
        var d = AsThePageReadsIt(doc.GetOrientationDetails());
        Assert.Equal(new[] { "X-EP" }, d.Lines.Single().Layers);                 // shown as the page can show it
        d.SamePostFeet = 2;
        doc.ApplyOrientationDetails(d);
        Assert.Empty(doc.Validate());
        var o = JObject.Parse(doc.Render())["orientation"]!;
        Assert.Null(o["stopSignCodes"]);                                         // it showed PMX's list: PMX's it is
        Assert.Equal(new[] { "LUM", "5" }, o["luminaireCodes"]!.Select(t => (string)t!));
        Assert.Equal(new[] { "X-EP" }, o["lines"]![0]!["layers"]!.Select(t => (string)t!));
    }

    [Fact]
    public void SavingPutsThisPagesLinesListsAndDistancesOnTheFileAsItIsNow()
    {
        var page = RuleDocument.Load(RulesPath);
        var d = page.GetOrientationDetails();
        d.Lines.Add(new EditableLineRole { Role = "WaterMain", Layers = new List<string> { "OFFICE-WATER" } });
        d.StopSignWords = new List<string> { "STOP" };
        d.SamePostFeet = 2;
        page.ApplyOrientationDetails(d);

        var fresh = RuleDocument.Load(RulesPath);
        fresh.SetInsertBlock(fresh.RuleIds().First(), false);            // something another page saved meanwhile
        page.CopyOrientationDetailsTo(fresh);
        Assert.Empty(fresh.Validate());
        var std = fresh.CompileCandidate().Orientation;
        Assert.Equal(LineRole.WaterMain, std.RoleFor("OFFICE-WATER", null));
        Assert.Equal(new[] { "STOP" }, std.StopSignWords);
        Assert.Equal(2, std.SamePostFeet, 6);
        Assert.Contains("\"//lines\"", fresh.Render());

        // Back to PMX's on the page: the file's own list goes too.
        d = page.GetOrientationDetails();
        d.Lines = OrientationStandard.Pmx().Lines.Select(r => EditableLineRole.From(r, -1)).ToList();
        page.ApplyOrientationDetails(d);
        page.CopyOrientationDetailsTo(fresh);
        Assert.True(fresh.GetOrientationDetails().LinesBuiltIn);
        Assert.DoesNotContain("\"//lines\"", fresh.Render());
    }

    [Fact]
    public void EachMistakeInLinesListsDistancesAndQcIsNamed()
    {
        var doc = WithOrientation(
            "{ \"lines\": [ { \"role\": \"Curb\", \"layers\": [ \"X\" ] }, { \"layers\": [ \"Y\" ] }, { \"role\": \"Edge\" }," +
            "  { \"role\": \"Edge\", \"layers\": [ \"BAD<NAME\" ], \"codes\": [ \"E P\", \"CL\" ] }, { \"role\": \"Centerline\", \"layers\": [ \"bad<name\" ], \"codes\": [ \"cl\" ] }," +
            "  { \"role\": \"LaneLine\", \"layers\": \"V-STRIPE\" }, 7 ]," +
            "  \"stopSignCodes\": \"SNS\", \"stopSignWords\": [ \"STOP\", \" \" ], \"luminaireCodes\": [ \"LUM\", \"lum\", \"LT A\" ]," +
            "  \"intersectionSearchFeet\": 0, \"stopBarSearchFeet\": 400, \"samePostFeet\": 50, \"panelSnapFeet\": \"near\", \"luminaireSearchFeet\": 1," +
            "  \"mainSnapFeet\": 60, \"valveClusterFeet\": 0, \"hydrantSearchFeet\": 1000," +
            "  \"qcLayer\": \"\", \"qcInferredLayer\": \"QC:INFERRED\", \"qcArrowPlotted\": 5 }");
        var problems = doc.Validate();
        Assert.Contains(problems, p => p.Contains("line rule 1: role 'Curb' is not one of Centerline, OneWay"));
        Assert.Contains(problems, p => p.Contains("line rule 2: needs a role"));
        Assert.Contains(problems, p => p.Contains("line rule 3 (Edge): names no layers and no codes"));
        Assert.Contains(problems, p => p.Contains("line rule 4 (Edge): layer \"BAD<NAME\" has a character AutoCAD does not allow"));
        Assert.Contains(problems, p => p.Contains("line rule 4 (Edge): code 'E P' has a space in it"));
        Assert.Contains(problems, p => p.Contains("line rule 5 (Centerline): code CL is also in rule 4 (Edge), which is looked at first"));
        Assert.Contains(problems, p => p.Contains("line rule 5 (Centerline): layer bad<name is also in rule 4 (Edge)"));
        Assert.Contains(problems, p => p.Contains("line rule 6 (LaneLine): its layers must be a list of text"));
        Assert.Contains(problems, p => p.Contains("line rule 7 is not a rule"));
        Assert.Contains(problems, p => p.Contains("the stop and yield sign codes must be a list"));
        Assert.Contains(problems, p => p.Contains("the stop sign words have a blank entry, which would make every sign a stop sign"));
        Assert.Contains(problems, p => p.Contains("listed twice in the luminaire codes: LUM"));
        Assert.Contains(problems, p => p.Contains("\"LT A\" in the luminaire codes is not one code"));
        Assert.Contains(problems, p => p.Contains("from an intersection") && p.Contains("between 10 and 500 ft (not 0)"));
        Assert.Contains(problems, p => p.Contains("from its stop bar must be between 5 and 300 ft"));
        Assert.Contains(problems, p => p.Contains("on one post must be between 0.1 and 10 ft"));
        Assert.Contains(problems, p => p.Contains("shot sign panel must be between 0.1 and 20 ft (not near)"));
        Assert.Contains(problems, p => p.Contains("luminaire shot may be from its light pole must be between 2 and 200 ft"));
        Assert.Contains(problems, p => p.Contains("water or gas line must be between 0.5 and 50 ft"));
        Assert.Contains(problems, p => p.Contains("valves are one cluster must be between 1 and 200 ft"));
        Assert.Contains(problems, p => p.Contains("hydrant may be from a valve cluster") && p.Contains("between 5 and 300 ft"));
        Assert.Contains(problems, p => p.Contains("the QC layer (direction arrows and notes) is required"));
        Assert.Contains(problems, p => p.Contains("the QC layer for inferred connections \"QC:INFERRED\" cannot be used"));
        Assert.Contains(problems, p => p.Contains("the plotted length of a QC arrow must be between 0.05 and 2 inch"));
        Assert.All(problems, p => Assert.StartsWith("Symbols and Rotations: ", p));
    }
}
