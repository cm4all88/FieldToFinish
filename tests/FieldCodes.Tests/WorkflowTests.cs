using FieldCodes.Annotation;
using FieldCodes.Settings;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Tests;

/// <summary>
/// Saved workflows (Settings > Workflows): how they are stored, what makes one invalid, how
/// their stages are put in order, the keyword FTFRUN offers each by, and the plan each runs --
/// which must be exactly what a Custom selection of the same stages and families runs.
/// </summary>
public sealed class WorkflowTests
{
    private static WorkflowDefinition TreesOnly() => new()
    {
        Name = "Trees only (no tags)",
        Description = "Driplines and tree labels.",
        Stages = new List<string> { "FTFLABELS", "FTFDRIP" },
        Families = new List<AnnotationCategory> { AnnotationCategory.Trees },
        ThenRun = new List<string> { "FTFLINES" }
    };

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ftf-wf-" + Path.GetRandomFileName());
        Directory.CreateDirectory(d);
        return d;
    }

    // ------------------------------------------------------------------ storage

    [Fact]
    public void WorkflowsRoundTripThroughTheSettingsFile()
    {
        var settings = new FtfSettings();
        settings.Workflows.Items.Add(TreesOnly());
        var path = Path.Combine(TempDir(), FtfSettings.FileName);

        settings.Save(path);

        // The shape on disk: a "workflows" section holding "items", families by name.
        var json = JObject.Parse(File.ReadAllText(path));
        var item = (JObject)json["workflows"]!["items"]![0]!;
        Assert.Equal("Trees only (no tags)", (string?)item["name"]);
        Assert.Equal(new[] { "FTFLABELS", "FTFDRIP" }, item["stages"]!.Select(t => (string?)t));
        Assert.Equal(new[] { "Trees" }, item["families"]!.Select(t => (string?)t));
        Assert.Equal(new[] { "FTFLINES" }, item["thenRun"]!.Select(t => (string?)t));

        var back = FtfSettings.Load(path).Workflows.Items.Single();
        Assert.Equal("Trees only (no tags)", back.Name);
        Assert.Equal("Driplines and tree labels.", back.Description);
        Assert.Equal(new[] { "FTFLABELS", "FTFDRIP" }, back.Stages);
        Assert.Equal(new[] { AnnotationCategory.Trees }, back.Families);
        Assert.Equal(new[] { "FTFLINES" }, back.ThenRun);
    }

    [Fact]
    public void ASettingsFileFromBeforeWorkflowsLoadsWithNone()
    {
        var json = JObject.FromObject(new FtfSettings());
        Assert.True(json.Remove("workflows"));
        var path = Path.Combine(TempDir(), FtfSettings.FileName);
        File.WriteAllText(path, json.ToString());

        var loaded = FtfSettings.Load(path);

        Assert.NotNull(loaded.Workflows);
        Assert.Empty(loaded.Workflows.Items);
        var problems = new List<string>();
        loaded.Validate(problems);
        Assert.Empty(problems);
    }

    [Fact]
    public void AHandEditedWorkflowWithMissingListsStillValidates()
    {
        var path = Path.Combine(TempDir(), FtfSettings.FileName);
        var json = JObject.FromObject(new FtfSettings());
        json["workflows"] = JObject.Parse(@"{ ""items"": [ null, { ""name"": ""Points"", ""stages"": [ ""FTFPOINTS"" ], ""families"": null } ] }");
        File.WriteAllText(path, json.ToString());

        var loaded = FtfSettings.Load(path);
        var problems = new List<string>();
        loaded.Validate(problems);

        Assert.Empty(problems);
        var only = Assert.Single(loaded.Workflows.Items);
        Assert.Empty(only.Families);
        Assert.Empty(only.ThenRun);
    }

    [Fact]
    public void RestoringDefaultsLeavesNoWorkflows()
    {
        var s = new WorkflowSettings();
        s.Items.Add(TreesOnly());
        s.RestoreDefaults();
        Assert.Empty(s.Items);
    }

    // ---------------------------------------------------------------- validation

    [Fact]
    public void AGoodWorkflowIsValid()
    {
        var s = new WorkflowSettings();
        s.Items.Add(TreesOnly());
        var problems = new List<string>();
        s.Validate(problems);
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("", "needs a name")]
    [InlineData("  ", "needs a name")]
    [InlineData("Trees", "already one of FTFRUN's choices")]
    [InlineData("all", "already one of FTFRUN's choices")]
    [InlineData("Custom", "name of a built-in preset")]
    [InlineData("Annotations Only", "name of a built-in preset")]
    [InlineData("!!!", "needs a letter or digit")]
    public void ANameFtfrunCannotOfferIsRefused(string name, string expected)
    {
        var w = TreesOnly();
        w.Name = name;
        Assert.Contains(Problems(w), p => p.Contains(expected));
    }

    [Fact]
    public void TwoWorkflowsCannotShareANameOrAKeyword()
    {
        var a = TreesOnly();
        var b = TreesOnly();
        b.Name = "TREES ONLY (NO TAGS)";
        Assert.Contains(Problems(a, b), p => p.Contains("two workflows are called"));

        b.Name = "Trees-only no-tags";       // a different name, the same keyword
        Assert.Contains(Problems(a, b), p => p.Contains("would both be typed as TreesOnlyNoTags"));
    }

    [Fact]
    public void StagesMustBeRealAndThereMustBeOne()
    {
        var w = TreesOnly();
        w.Stages = new List<string> { "FTFPOINTS", "FTFFOO" };
        Assert.Contains(Problems(w), p => p.Contains("names FTFFOO, which is not a processing stage"));

        w.Stages = new List<string>();
        Assert.Contains(Problems(w), p => p.Contains("has no stages to run"));
    }

    [Fact]
    public void OnlyAllowedCommandsCanRunAfterwards()
    {
        var w = TreesOnly();
        w.ThenRun = new List<string> { "FTFLINES", "FTFCLEAN", "FTFLEGEND" };
        var problems = Problems(w);
        Assert.Contains(problems, p => p.Contains("cannot run FTFCLEAN afterwards"));
        Assert.Contains(problems, p => p.Contains("cannot run FTFLEGEND afterwards"));
        Assert.DoesNotContain(problems, p => p.Contains("FTFLINES afterwards"));
    }

    [Fact]
    public void AnInvalidWorkflowStopsTheSettingsFromSaving()
    {
        var settings = new FtfSettings();
        var w = TreesOnly();
        w.Name = "Standard";
        settings.Workflows.Items.Add(w);
        Assert.Throws<ConfigException>(() => settings.Save(Path.Combine(TempDir(), FtfSettings.FileName)));
    }

    // ------------------------------------------------------------ normalization

    [Fact]
    public void NormalizingPutsStagesInRunOrderAndAddsDrawOrder()
    {
        var s = new WorkflowSettings();
        var w = TreesOnly();
        w.Stages = new List<string> { "ftflabels", "FTFDRIP", "FTFLABELS", "FTFNOPE" };
        w.Families = new List<AnnotationCategory> { AnnotationCategory.Trees, AnnotationCategory.Trees };
        w.ThenRun = new List<string> { " ftflines ", "FTFLINES" };
        s.Items.Add(w);

        var notes = s.Normalize();

        Assert.Equal(new[] { "FTFDRIP", "FTFLABELS", "FTFORDER" }, w.Stages);
        Assert.Equal(new[] { AnnotationCategory.Trees }, w.Families);
        Assert.Equal(new[] { "FTFLINES" }, w.ThenRun);
        Assert.Contains(notes, n => n.StartsWith("Trees only (no tags): Draw order added"));
        Assert.Contains(notes, n => n.Contains("Unknown stage FTFNOPE ignored"));
    }

    // ------------------------------------------------------------------ keywords

    [Theory]
    [InlineData("Trees only (no tags)", "TreesOnlyNoTags")]
    [InlineData("survey-check", "SurveyCheck")]
    [InlineData("Pass 2", "Pass2")]
    [InlineData("2nd pass", "Run2ndPass")]
    [InlineData("Árbol", "Rbol")]
    [InlineData("labels_and_tags", "LabelsAndTags")]
    [InlineData("  ", "")]
    [InlineData("!!!", "")]
    [InlineData(null, "")]
    public void FtfrunKeywordsAreLettersAndDigitsOnly(string? name, string keyword)
    {
        Assert.Equal(keyword, WorkflowSettings.Keyword(name!));
    }

    [Fact]
    public void ALongNameMakesAKeywordOfAtMost31Characters()
    {
        var keyword = WorkflowSettings.Keyword("Standard topographic finishing with every family and the schedule");
        Assert.Equal(WorkflowSettings.MaxKeywordLength, keyword.Length);
        Assert.StartsWith("StandardTopographicFinishing", keyword);
    }

    [Fact]
    public void FtfrunOffersEachUsableWorkflowOnce()
    {
        var a = TreesOnly();
        var clash = new WorkflowDefinition { Name = "trees", Stages = { "FTFDRIP" } };          // a built-in keyword
        var twin = new WorkflowDefinition { Name = "Trees-only no tags", Stages = { "FTFDRIP" } }; // same keyword as a
        var blank = new WorkflowDefinition { Name = "...", Stages = { "FTFDRIP" } };
        var b = new WorkflowDefinition { Name = "Points then order", Stages = { "FTFPOINTS" } };

        var choices = WorkflowSettings.Choices(new[] { a, clash, twin, blank, null!, b });

        Assert.Equal(new[] { "TreesOnlyNoTags", "PointsThenOrder" }, choices.Select(c => c.Key));
        Assert.Same(a, choices[0].Value);
        Assert.Same(b, new WorkflowSettings { Items = { a, b } }.Find("pointsthenorder"));
        Assert.Same(a, new WorkflowSettings { Items = { a, b } }.Find("trees only (no tags)"));
    }

    // -------------------------------------------------------------------- plans

    [Fact]
    public void AWorkflowRunsExactlyAsTheSameCustomSelectionWould()
    {
        var settings = new AnnotationSettings();
        var w = TreesOnly();

        var plan = ProcessPresets.Build(w, settings);
        var custom = ProcessPresets.Build(ProcessPreset.Custom, settings, w.Stages, w.Families);

        Assert.Equal(ProcessPreset.Workflow, plan.Preset);
        Assert.Equal("Trees only (no tags)", plan.Name);
        Assert.Equal("Trees only (no tags)", plan.Title);
        Assert.Equal(custom.Stages, plan.Stages);
        Assert.Equal(new[] { "FTFDRIP", "FTFLABELS", "FTFORDER" }, plan.Stages);
        Assert.Equal(custom.LabelFamilies, plan.LabelFamilies);
        Assert.Equal(custom.TagFamilies, plan.TagFamilies);
        Assert.Equal(custom.Adjustments, plan.Adjustments);
        Assert.Equal(new[] { "FTFLINES" }, plan.ThenRun);
    }

    [Fact]
    public void NoFamiliesMeansTheFamiliesSetOnLabelAll()
    {
        var settings = new AnnotationSettings { Categories = new List<AnnotationCategory> { AnnotationCategory.Signs, AnnotationCategory.Lines } };
        var w = new WorkflowDefinition { Name = "Signs", Stages = { "FTFLINELABELS", "FTFLABELS" } };

        var plan = ProcessPresets.Build(w, settings);

        Assert.Equal(new[] { AnnotationCategory.Signs, AnnotationCategory.Lines }.OrderBy(c => c), plan.LabelFamilies.OrderBy(c => c));
        Assert.Equal(new[] { "FTFLINELABELS", "FTFLABELS", "FTFORDER" }, plan.Stages);
    }

    [Fact]
    public void TheDependencyRulesApplyToWorkflowsToo()
    {
        // Line labels need the line family; a hand-edited stage name in lower case still counts.
        var w = new WorkflowDefinition
        {
            Name = "Labels", Stages = { "ftflinelabels", "FTFLABELS" },
            Families = { AnnotationCategory.Trees }, ThenRun = { "FTFCLEAN", "ftfkeymap" }
        };

        var plan = ProcessPresets.Build(w, new AnnotationSettings());

        Assert.Equal(new[] { "FTFLABELS", "FTFORDER" }, plan.Stages);
        Assert.Contains("Line labels skipped: line features are not selected.", plan.Adjustments);
        Assert.Contains("FTFCLEAN cannot run after a workflow; skipped.", plan.Adjustments);
        Assert.Equal(new[] { "FTFKEYMAP" }, plan.ThenRun);
    }

    [Fact]
    public void ABuiltInPresetKeepsItsTitleAndRunsNothingAfterwards()
    {
        var plan = ProcessPresets.Build(ProcessPreset.StandardTopographic, new AnnotationSettings());
        Assert.Null(plan.Name);
        Assert.Equal("Standard Topographic Finishing", plan.Title);
        Assert.Empty(plan.ThenRun);
        Assert.Equal("Saved Workflow", ProcessPresets.Title(ProcessPreset.Workflow));
    }

    // ----------------------------------------------------------------- helpers

    private static List<string> Problems(params WorkflowDefinition[] workflows)
    {
        var s = new WorkflowSettings();
        s.Items.AddRange(workflows);
        var problems = new List<string>();
        s.Validate(problems);
        return problems;
    }
}

/// <summary>Tests that set AnnotationRun's process-wide state, so they run alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AnnotationRunCollection
{
    public const string Name = "AnnotationRun state";
}

[Collection(AnnotationRunCollection.Name)]
public sealed class WorkflowFollowUpTests
{
    [Fact]
    public void FollowUpsAreHandedOnOnceTheRunEnds()
    {
        var handed = new List<ProcessPlan>();
        var previous = AnnotationRun.FollowUp;
        AnnotationRun.FollowUp = handed.Add;
        try
        {
            var plan = ProcessPresets.Build(new WorkflowDefinition { Name = "Report", Stages = { "FTFPOINTS" }, ThenRun = { "FTFLINES" } },
                                            new AnnotationSettings());
            using (AnnotationRun.Use(plan))
            {
                Assert.Same(plan, AnnotationRun.Current);
                Assert.Empty(handed);                 // not while the stages run
            }
            Assert.Null(AnnotationRun.Current);
            Assert.Same(plan, Assert.Single(handed));

            // A preset has nothing to run afterwards, so nothing is handed on.
            using (AnnotationRun.Use(ProcessPresets.Build(ProcessPreset.StandardTopographic, new AnnotationSettings()))) { }
            Assert.Single(handed);
        }
        finally { AnnotationRun.FollowUp = previous; }
    }
}
