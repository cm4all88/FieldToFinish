using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

namespace FieldCodes.Tests;

/// <summary>
/// The Civil 3D smoke harnesses (tests/LiveSmoke) reach the plugin by reflection, so the compiler
/// never checks what they call. At 68804df IdentityTestHarness looked SourcePointReader.Read up by
/// name; 95cb191 had added Read(db, tr, notes), the lookup threw AmbiguousMatchException inside
/// IDVERIFY READER, nothing caught it, and accoreconsole ended with no output. UPDATEFTF stopped
/// and put the previous build back.
///
/// These tests load the plugin as compiled (tests/CadCompileCheck's output: the plugin's own code,
/// with the AutoCAD stand-ins compiled into it; built before this project) and check every
/// signature the harnesses call resolves to exactly one method, as it must in Civil 3D. They also
/// hold the harnesses to the rules that make such a failure loud: no lookup by name alone, every
/// command guarded, each step on the disk.
/// </summary>
public sealed class HarnessContractTests
{
    /// <summary>The harnesses deploy\smoke-test.ps1 and tests\LiveSmoke\run-figures.ps1 run.</summary>
    private static readonly string[] Harnesses =
    {
        "IdentityTestHarness.cs", "AccountabilityTestHarness.cs", "ContourTestHarness.cs",
        "StyleTestHarness.cs", "FigureFixtureHarness.cs",
    };

    private const string BlockStart = "        // ==== plugin contract: this block is the same in every Civil 3D harness (HarnessContractTests) ====";
    private const string BlockEnd = "        // ==== end of plugin contract ====";

    private static readonly Regex Signature = new(@"""(FieldCodes\.[\w.]+\.\w+\([^""]*\))""");

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FieldToFinish.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>
    /// The line endings a checkout gives a file: LF as the repository stores it (Linux), CRLF as Git
    /// for Windows writes it by default (core.autocrlf=true), which is what UPDATEFTF's build
    /// checkout has. Every test here that reads a file runs on both, so a check written on one
    /// cannot fail only on the other: TheSmokeTestSaysHowARunEndedAndWhereItGotTo looked for
    /// "\n}\n" and failed on Windows at 063b747 (the search found nothing in CRLF text).
    /// </summary>
    public static TheoryData<string> LineEndings => new() { "\n", "\r\n" };

    private static string WithLineEndings(string text, string eol) => text.Replace("\r\n", "\n").Replace("\n", eol);

    private static string Harness(string name, string eol) => WithLineEndings(File.ReadAllText(Path.Combine(Root(), "tests", "LiveSmoke", name)), eol);

    private static string Source(string eol, params string[] parts) => WithLineEndings(File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray())), eol);

    /// <summary>A PowerShell function, from its "function" line to the "}" that closes it (the first
    /// line that is only "}"), whichever line endings the file has.</summary>
    private static string FunctionBody(string script, string name)
    {
        var start = script.IndexOf("function " + name, StringComparison.Ordinal);
        Assert.True(start >= 0, "no function " + name);
        var end = Regex.Match(script.Substring(start), @"^\}\r?$", RegexOptions.Multiline);
        Assert.True(end.Success, "function " + name + " has no closing brace on a line of its own");
        return script.Substring(start, end.Index + 1);
    }

    // ------------------------------------------------------------------ the compiled plugin

    private sealed class PluginContext : AssemblyLoadContext
    {
        private readonly string _folder;

        public PluginContext(string folder) : base("FieldCodes.Cad as compiled", isCollectible: false) { _folder = folder; }

        protected override Assembly? Load(AssemblyName name)
        {
            var path = Path.Combine(_folder, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }

    private static readonly object LoadLock = new();
    private static Assembly? _plugin;
    private static string? _traceFile;

    private static string PluginFolder()
    {
        // The same configuration as this test build: bin/<Configuration>/net8.0.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Parent!.Name;
        var folder = Path.Combine(Root(), "tests", "CadCompileCheck", "bin", configuration, "net48");
        Assert.True(File.Exists(Path.Combine(folder, "CadCompileCheck.dll")),
            "The compiled plugin is not at " + folder + ". FieldCodes.Tests builds tests/CadCompileCheck first; build it (dotnet build tests/CadCompileCheck -c " + configuration + ").");
        return folder;
    }

    /// <summary>The plugin as compiled, loaded with FTF_TRACE_FILE pointing at a fresh file, so
    /// its diagnostic trace can be read back.</summary>
    private static Assembly Plugin()
    {
        lock (LoadLock)
        {
            if (_plugin != null) return _plugin;
            var folder = PluginFolder();
            _traceFile = Path.Combine(Path.GetTempPath(), "ftf-trace-" + Guid.NewGuid().ToString("N") + ".txt");
            var before = Environment.GetEnvironmentVariable("FTF_TRACE_FILE");
            Environment.SetEnvironmentVariable("FTF_TRACE_FILE", _traceFile);
            try
            {
                var plugin = new PluginContext(folder).LoadFromAssemblyPath(Path.Combine(folder, "CadCompileCheck.dll"));
                // The trace file is read when FtfCommandTrace is first used: now, while it is set.
                RuntimeHelpers.RunClassConstructor(plugin.GetType("FieldCodes.Cad.FtfCommandTrace", true)!.TypeHandle);
                _plugin = plugin;
            }
            finally { Environment.SetEnvironmentVariable("FTF_TRACE_FILE", before); }
            return _plugin;
        }
    }

    /// <summary>As the harnesses' Contract: the methods <paramref name="signature"/> names, by
    /// exact parameter types, in the plugin or in the FieldCodes library it loads.</summary>
    private static (List<MethodInfo> Matches, List<MethodInfo> Named, Type? Type) Resolve(string signature)
    {
        var plugin = Plugin();
        var open = signature.IndexOf('(');
        var dot = signature.LastIndexOf('.', open);
        var typeName = signature.Substring(0, dot);
        var name = signature.Substring(dot + 1, open - dot - 1);
        var wanted = signature.Substring(open + 1, signature.Length - open - 2).Replace(" ", string.Empty);
        var type = plugin.GetType(typeName)
                   ?? AssemblyLoadContext.GetLoadContext(plugin)!.LoadFromAssemblyName(new AssemblyName("FieldCodes")).GetType(typeName);
        if (type == null) return (new List<MethodInfo>(), new List<MethodInfo>(), null);
        var named = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Where(m => m.Name == name).ToList();
        return (named.Where(m => ParameterList(m) == wanted).ToList(), named, type);
    }

    private static string ParameterList(MethodInfo m) => string.Join(",", m.GetParameters().Select(p => TypeText(p.ParameterType)));

    private static string TypeText(Type t)
    {
        if (t.IsByRef) return TypeText(t.GetElementType()!) + "&";
        if (t.IsArray) return TypeText(t.GetElementType()!) + "[]";
        if (t.IsGenericType && !t.IsGenericTypeDefinition)
            return t.GetGenericTypeDefinition().FullName + "[" + string.Join(",", t.GetGenericArguments().Select(TypeText)) + "]";
        return t.FullName ?? t.Name;
    }

    // ------------------------------------------------------------------ the failure at 68804df

    [Fact]
    public void TheNameOnlyLookupThatStoppedIdverifyReaderIsAmbiguousInTheCompiledPlugin()
    {
        var reader = Plugin().GetType("FieldCodes.Cad.SourcePointReader", true)!;
        // IdentityTestHarness.cs:192 at 68804df.
        var ex = Assert.Throws<AmbiguousMatchException>(() => reader.GetMethod("Read", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
        Assert.Contains("Ambiguous match", ex.Message);

        // The harness now names the overload, and both resolve, to different methods.
        var plain = Resolve("FieldCodes.Cad.SourcePointReader.Read(Autodesk.AutoCAD.DatabaseServices.Database, Autodesk.AutoCAD.DatabaseServices.Transaction)");
        var notes = Resolve("FieldCodes.Cad.SourcePointReader.Read(Autodesk.AutoCAD.DatabaseServices.Database, Autodesk.AutoCAD.DatabaseServices.Transaction, FieldCodes.Cad.CogoNotes)");
        Assert.Single(plain.Matches);
        Assert.Single(notes.Matches);
        Assert.Equal(2, plain.Named.Count);
        Assert.NotEqual(plain.Matches[0], notes.Matches[0]);
        Assert.Contains(SignaturesIn("IdentityTestHarness.cs", "\n"), s => s.EndsWith("Read(Autodesk.AutoCAD.DatabaseServices.Database, Autodesk.AutoCAD.DatabaseServices.Transaction)", StringComparison.Ordinal));
        Assert.Contains(SignaturesIn("IdentityTestHarness.cs", "\n"), s => s.EndsWith("Read(Autodesk.AutoCAD.DatabaseServices.Database, Autodesk.AutoCAD.DatabaseServices.Transaction, FieldCodes.Cad.CogoNotes)", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ every signature

    private static List<string> SignaturesIn(string harness, string eol) =>
        Signature.Matches(Harness(harness, eol)).Select(m => m.Groups[1].Value).Distinct().ToList();

    [Theory]
    [MemberData(nameof(LineEndings))]
    public void EverySignatureTheHarnessesCallResolvesToOneMethodOfTheCompiledPlugin(string eol)
    {
        var problems = new List<string>();
        var total = 0;
        foreach (var harness in Harnesses)
        {
            var signatures = SignaturesIn(harness, eol);
            Assert.True(signatures.Count > 0, harness + " names no plugin signature; it reaches the plugin some other way.");
            foreach (var signature in signatures)
            {
                total++;
                var (matches, named, type) = Resolve(signature);
                if (type == null) problems.Add(harness + ": " + signature + ": no such type in the compiled plugin");
                else if (matches.Count != 1)
                    problems.Add(harness + ": " + signature + " matches " + matches.Count + "; the plugin has " +
                                 (named.Count == 0 ? "no such method" : string.Join("; ", named.Select(m => m.Name + "(" + ParameterList(m) + ")"))));
            }
        }
        Assert.True(problems.Count == 0, "Plugin members the Civil 3D harnesses call (FATAL in Civil 3D):\n" + string.Join("\n", problems));
        Assert.True(total >= 20, "Expected the harnesses' signatures, found " + total);
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public void NoHarnessLooksAPluginMethodUpByNameAlone(string eol)
    {
        var problems = new List<string>();
        foreach (var harness in Harnesses)
        {
            var code = AmbiguousTypeNameTests.CodeOnly(WithoutContractBlock(Harness(harness, eol)));
            // GetMethod by name (GetMethods, which lists them all, cannot be ambiguous).
            foreach (Match m in Regex.Matches(code, @"\.GetMethod\s*\("))
                problems.Add(harness + "(" + (code.Take(m.Index).Count(c => c == '\n') + 1) + "): use Call/Contract with the exact signature");
        }
        Assert.True(problems.Count == 0, "Lookups outside the plugin contract block:\n" + string.Join("\n", problems));
    }

    private static string Block(string text)
    {
        var start = text.IndexOf(BlockStart, StringComparison.Ordinal);
        var end = text.IndexOf(BlockEnd, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "no plugin contract block");
        return text.Substring(start, end + BlockEnd.Length - start).Replace("\r\n", "\n");
    }

    private static string WithoutContractBlock(string text)
    {
        var block = Block(text);
        // Keep the line count, so reported lines are the file's.
        return text.Replace("\r\n", "\n").Replace(block, new string('\n', block.Count(c => c == '\n')));
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public void ThePluginContractBlockIsTheSameInEveryHarness(string eol)
    {
        var first = Block(Harness(Harnesses[0], eol));
        foreach (var harness in Harnesses.Skip(1))
            Assert.True(Block(Harness(harness, eol)) == first, harness + ": its plugin contract block differs from " + Harnesses[0] + "'s; copy it over.");
        // Lines written through to the disk, and exceptions recorded in full.
        Assert.Contains("FileOptions.WriteThrough", first);
        Assert.Contains("stream.Flush(true)", first);
        Assert.Contains("Record(\"EXCEPTION \"", first);
        Assert.Contains("Fatal(what + \" threw \"", first);
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public void EveryHarnessCommandIsGuardedAndEveryResultLineIsDurable(string eol)
    {
        var problems = new List<string>();
        foreach (var harness in Harnesses)
        {
            var text = Harness(harness, eol);
            foreach (Match m in Regex.Matches(text, @"\[CommandMethod\(""(\w+)""\)\]\s*public void \w+\(\)\s*\{\s*(.*)"))
            {
                var first = m.Groups[2].Value;
                if (m.Groups[1].Value == "IDSEED") first = Regex.Match(text.Substring(m.Index), @"\{\s*Record\(""BEGIN[^\n]*\n\s*(.*)").Groups[1].Value;
                if (!first.StartsWith("Guard(\"" + m.Groups[1].Value + "\"", StringComparison.Ordinal))
                    problems.Add(harness + ": " + m.Groups[1].Value + " does not run its body through Guard");
            }
            if (!text.Contains("AppendDurably(file, line)", StringComparison.Ordinal) || text.Contains("File.AppendAllText(file", StringComparison.Ordinal))
                problems.Add(harness + ": Record does not write through to the disk (AppendDurably)");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public void TheHarnessesCheckTheirWholeContractBeforeSeeding(string eol)
    {
        foreach (var harness in new[] { "IdentityTestHarness.cs", "AccountabilityTestHarness.cs", "FigureFixtureHarness.cs" })
        {
            var text = Harness(harness, eol);
            Assert.Contains("foreach (var signature in PluginContract) Contract(_plugin, signature);", text);
            var listed = Regex.Match(text, @"PluginContract = \{([^}]*)\}").Groups[1].Value.Split(',').Select(x => x.Trim()).ToHashSet();
            foreach (Match c in Regex.Matches(text, @"private const string (\w+) = ""FieldCodes\."))
                Assert.True(listed.Contains(c.Groups[1].Value), harness + ": " + c.Groups[1].Value + " is not in PluginContract, so it is not checked before seeding.");
        }
    }

    // ------------------------------------------------------------------ what the harnesses expect

    [Theory]
    [MemberData(nameof(LineEndings))]
    public void TheIdentityHarnessExpectsTheLineworkCellsTheCsvWriterWrites(string eol)
    {
        // IdentityTestHarness's LINES stage reads FTFLINES's inventory CSV cell by cell. At 59d05ef it
        // expected "-" for the line with no shots; LineworkInventoryCsv writes "'-" (a cell starting
        // with - = + or @ gets a leading ', so a spreadsheet does not take it for a formula), and the
        // stage failed in Civil 3D. Each expected cell is checked here against the writer itself.
        var expects = Regex.Matches(Harness("IdentityTestHarness.cs", eol),
            @"Expect\(ed, row\(""(\w)""\), header, ""[^""]*"", ""([^""]*)"", (null|""[^""]*""), ""(\d+)"", ""([^""]*)""\);");
        Assert.Equal(7, expects.Count);

        // Every Source cell the writer can write, one per way a line is identified.
        var sources = Enum.GetValues<FieldCodes.Linework.LineIdentitySource>()
            .Select(source => Cells(new FieldCodes.Linework.LineworkRow { Source = source })["Source"]).ToList();
        Assert.Contains("'-", sources);

        foreach (Match e in expects)
        {
            var line = e.Groups[1].Value;
            var source = e.Groups[2].Value;
            Assert.True(sources.Contains(source), "line " + line + ": the harness expects Source '" + source + "'; the writer writes one of: " + string.Join(", ", sources));
            var codes = e.Groups[3].Value == "null" ? null : e.Groups[3].Value.Trim('"');
            var shots = e.Groups[5].Value;
            var cells = Cells(new FieldCodes.Linework.LineworkRow
            {
                Codes = codes,
                VertexCount = int.Parse(e.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture),
                Shots = shots.Length == 0 ? new List<string>() : shots.Split(' ').ToList(),
            });
            if (codes != null) Assert.True(cells["Codes"] == codes, "line " + line + ": Codes '" + codes + "' is written as '" + cells["Codes"] + "'");
            Assert.True(cells["Vertices"] == e.Groups[4].Value, "line " + line + ": Vertices");
            Assert.True(cells["Shots"] == shots, "line " + line + ": Shots '" + shots + "' is written as '" + cells["Shots"] + "'");
        }
    }

    /// <summary>One row as LineworkInventoryCsv writes it, by column.</summary>
    private static Dictionary<string, string> Cells(FieldCodes.Linework.LineworkRow row)
    {
        var lines = FieldCodes.Linework.LineworkInventoryCsv.Render(new[] { row }).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].Split(',');
        var values = lines[1].Split(',');
        Assert.Equal(header.Length, values.Length);
        return header.Zip(values, (h, v) => (h, v)).ToDictionary(x => x.h, x => x.v);
    }

    // ------------------------------------------------------------------ the plugin's trace

    [Fact]
    public void ThePluginsTraceIsWrittenStepByStepWithFullExceptions()
    {
        var trace = Plugin().GetType("FieldCodes.Cad.FtfCommandTrace", true)!;
        Assert.True((bool)trace.GetProperty("Tracing")!.GetValue(null)!);
        trace.GetMethod("Step", new[] { typeof(string) })!.Invoke(null, new object[] { "reader: point 3 handle 1A2: reading" });
        trace.GetMethod("Start", new[] { typeof(string) })!.Invoke(null, new object[] { "FTFTESTTRACE" });
        var inner = new InvalidOperationException("inner cause");
        System.Exception thrown;
        try { throw new ApplicationException("outer\nsecond line", inner); }
        catch (System.Exception ex) { thrown = ex; }
        trace.GetMethod("Failed", new[] { typeof(string), typeof(System.Exception) })!.Invoke(null, new object[] { "FTFTESTTRACE", thrown });

        var lines = File.ReadAllLines(_traceFile!);
        Assert.All(lines, l => Assert.Matches(@"^TRACE \d\d:\d\d:\d\d\.\d{3} ", l));
        Assert.Contains(lines, l => l.EndsWith(" reader: point 3 handle 1A2: reading", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.EndsWith(" FTFTESTTRACE started", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains(" FTFTESTTRACE threw System.ApplicationException: outer | second line", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("System.InvalidOperationException: inner cause", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains(" at FieldCodes.Tests.HarnessContractTests.", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutTheVariableThePluginWritesNoTrace()
    {
        // An office session: FTF_TRACE_FILE unset, nothing written, nothing thrown.
        var folder = PluginFolder();
        var before = Environment.GetEnvironmentVariable("FTF_TRACE_FILE");
        Environment.SetEnvironmentVariable("FTF_TRACE_FILE", null);
        try
        {
            var plugin = new PluginContext(folder).LoadFromAssemblyPath(Path.Combine(folder, "CadCompileCheck.dll"));
            var trace = plugin.GetType("FieldCodes.Cad.FtfCommandTrace", true)!;
            Assert.False((bool)trace.GetProperty("Tracing")!.GetValue(null)!);
            trace.GetMethod("Step", new[] { typeof(string) })!.Invoke(null, new object[] { "nothing" });
            trace.GetMethod("Failed", new[] { typeof(string), typeof(System.Exception) })!.Invoke(null, new object[] { "X", new InvalidOperationException() });
        }
        finally { Environment.SetEnvironmentVariable("FTF_TRACE_FILE", before); }
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public void TheReaderAndThePointEnumerationTraceEachStep(string eol)
    {
        var reader = Source(eol, "src", "FieldCodes.Cad", "SourcePointReader.cs");
        Assert.Contains("FtfCommandTrace.Step(\"reader: point \" + n + \" handle \"", reader);
        Assert.Contains("FtfCommandTrace.Step(\"reader: done, \"", reader);
        var util = Source(eol, "src", "FieldCodes.Cad", "CadUtil.cs");
        Assert.Contains("FtfCommandTrace.Step(\"points: opening model space\")", util);
        // FTFLINES does not go through FtfSession.Run: it records its own exception before AutoCAD sees it.
        var lines = Source(eol, "src", "FieldCodes.Cad", "LineworkCommands.cs");
        Assert.Contains("FtfCommandTrace.Failed(\"FTFLINES\", ex);", lines);
        var session = Source(eol, "src", "FieldCodes.Cad", "FtfSession.cs");
        Assert.Equal(3, Regex.Matches(session, @"FtfCommandTrace\.Failed\(commandName, ex\);").Count);
    }

    // ------------------------------------------------------------------ the smoke test's report

    [Theory]
    [MemberData(nameof(LineEndings))]
    public void TheSmokeTestSaysHowARunEndedAndWhereItGotTo(string eol)
    {
        var smoke = Source(eol, "deploy", "smoke-test.ps1");
        // Exit codes are only readable from Start-Process when the handle was taken while it ran:
        // the main run, and the one runner every required check (identity, accountability, style) uses.
        Assert.Equal(2, Regex.Matches(smoke, @"\$null = \$(p|process)\.Handle").Count);
        Assert.Equal(2, Regex.Matches(smoke, @"Get-ExitReport \$(p|process) \$killed").Count);
        Assert.Equal(1, Regex.Matches(smoke, @"Start-Process -FilePath \$accore").Count - 1);   // one runner beside the main run

        // How the run ended: the exit code and what it means, or a kill on a timeout, and how long.
        var exit = FunctionBody(smoke, "Get-ExitReport");
        Assert.Contains("-532462766 { 'a .NET exception nothing caught' }", exit);
        Assert.Contains("3 { 'the harness ended the run (FATAL)' }", exit);
        Assert.Contains("exit code $code ($hex, $meaning) after $seconds s", exit);
        Assert.Contains("stopped by the smoke test after $seconds s ($why)", exit);
        // Where it got to: the exception, and the last steps recorded (harness STEP, plugin TRACE).
        var diagnostics = FunctionBody(smoke, "Get-RunDiagnostics");
        Assert.Contains("$_ -like 'EXCEPTION *'", diagnostics);
        Assert.Contains("$_ -like 'STEP *' -or $_ -like 'TRACE *'", diagnostics);
        Assert.Contains("--- last steps recorded", diagnostics);

        // The plugin's trace beside the harness's steps, in each run's result file.
        Assert.Contains("[Environment]::SetEnvironmentVariable('FTF_TRACE_FILE', $resultPath, 'Process')", smoke);
        Assert.Contains("$env:FTF_TRACE_FILE = $result", smoke);
        // The main run: a run without its COMPLETE line fails, with the diagnostics.
        Assert.Contains("if (-not ($outcome -contains 'COMPLETE')) { Fail 'the run stopped before its end' (@($failLines) + $diagnostics + $tail) }", smoke);

        // Every way a required check can fail reports the exit, the exception and the last steps;
        // a run that never wrote COMPLETE, and one stopped on a timeout, among them.
        var required = FunctionBody(smoke, "Invoke-RequiredCheck");
        Assert.Contains("$diagnostics = @($exitReport) + @(Get-RunDiagnostics $r)", required);
        Assert.Contains("if (-not ($r -contains 'COMPLETE')) { Fail ($prefix + 'the run stopped before its end') (@($failed) + $diagnostics", required);
        Assert.Contains("$why = 'no result after 300 s", required);
        var fails = Regex.Matches(required, @"\bFail \(\$prefix \+ [^\r\n]*").Select(m => m.Value).ToList();
        Assert.True(fails.Count >= 6, "expected the required check's failure paths, found " + fails.Count);
        foreach (var fail in fails)
            if (!fail.Contains("did not compile") && !fail.Contains("no rules.json") && !fail.Contains("stages not passed"))
                Assert.Contains("$diagnostics", fail);
    }
}
