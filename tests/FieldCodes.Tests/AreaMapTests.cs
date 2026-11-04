using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FieldCodes.Tests;

/// <summary>
/// tests/areas.json is the map tools/test-plan.ps1 chooses a change's tests from, so a targeted run
/// is only as good as the map. These hold it to the code: its shape, every filter naming a real test
/// class, every source and unit-test file in an area, and every test that reaches an area's code
/// (by the types it uses, through the core code those use, or by reading the area's files) running
/// when that area changes. Shared areas run the full suite, so the reach stops at their files.
/// </summary>
public sealed class AreaMapTests
{
    private static readonly string[] Sections = { "main", "identity", "accountability", "style" };
    private static readonly Regex Clause = new(@"^FullyQualifiedName~FieldCodes\.Tests\.(\w+)\.$");
    private static readonly Regex Commit = new("^[0-9a-f]{40}$");

    private static string Repo([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));

    private sealed record Area(string Id, bool Shared, string[] Paths, Regex[] Globs, string[] Classes, string[] Civil3d, JsonElement Json)
    {
        public bool Has(string path) => Globs.Any(g => g.IsMatch(path));
    }

    private sealed record Map(Area[] Areas, string[] AlwaysRun, Regex[] NoTests, JsonElement Root);

    private static Map Load()
    {
        var root = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo(), "tests", "areas.json"))).RootElement;
        string[] Strings(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToArray();
        string ClassOf(string clause)
        {
            var m = Clause.Match(clause);
            Assert.True(m.Success, "a unitTests clause must be FullyQualifiedName~FieldCodes.Tests.<Class>. : " + clause);
            return m.Groups[1].Value;
        }
        var areas = root.GetProperty("areas").EnumerateArray().Select(a =>
        {
            var paths = Strings(a.GetProperty("paths"));
            return new Area(a.GetProperty("id").GetString()!, a.GetProperty("shared").GetBoolean(), paths,
                            paths.Select(Glob).ToArray(), Strings(a.GetProperty("unitTests")).Select(ClassOf).ToArray(),
                            Strings(a.GetProperty("civil3d")), a);
        }).ToArray();
        return new Map(areas, Strings(root.GetProperty("alwaysRun")).Select(ClassOf).ToArray(),
                       Strings(root.GetProperty("noTests").GetProperty("paths")).Select(Glob).ToArray(), root);
    }

    /// <summary>The same glob as tools/test-plan.ps1: * within a folder, ** across folders.</summary>
    private static Regex Glob(string glob)
    {
        var re = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                if (i + 2 < glob.Length && glob[i + 2] == '/') { re.Append("(?:.*/)?"); i += 2; }
                else { re.Append(".*"); i++; }
            }
            else if (c == '*') re.Append("[^/]*");
            else if (c == '?') re.Append("[^/]");
            else re.Append(Regex.Escape(c.ToString()));
        }
        return new Regex(re.Append('$').ToString());
    }

    /// <summary>Every file of the repository by its repo-relative path, build output left out.</summary>
    private static List<string> Files(string under = "")
    {
        var skip = new[] { ".git", "bin", "obj", "TestResults", ".vs" };
        var root = Repo();
        var found = new List<string>();
        var todo = new Stack<string>();
        todo.Push(Path.Combine(root, under));
        while (todo.Count > 0)
        {
            var dir = todo.Pop();
            if (!Directory.Exists(dir)) continue;
            foreach (var d in Directory.GetDirectories(dir))
                if (!skip.Contains(Path.GetFileName(d))) todo.Push(d);
            foreach (var f in Directory.GetFiles(dir))
                found.Add(Path.GetRelativePath(root, f).Replace('\\', '/'));
        }
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>The test classes in this assembly, by name.</summary>
    private static HashSet<string> TestClasses() =>
        typeof(AreaMapTests).Assembly.GetTypes()
            .Where(t => t.GetMethods().Any(m => m.GetCustomAttributes<FactAttribute>(true).Any()))
            .Select(t => t.Name).ToHashSet();

    // ----------------------------------------------------------------- the map itself

    [Fact]
    public void EveryAreaIsCompleteAndItsRecordIsWellFormed()
    {
        var map = Load();
        var tests = TestClasses();
        Assert.Equal(map.Areas.Length, map.Areas.Select(a => a.Id).Distinct().Count());
        var sections = map.Root.GetProperty("civil3dSections").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(Sections, sections);
        var installers = map.Root.GetProperty("installerTests").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.All(installers, s => Assert.True(File.Exists(Path.Combine(Repo(), s)), s));
        Assert.All(map.AlwaysRun, c => Assert.Contains(c, tests));

        foreach (var a in map.Areas)
        {
            Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", a.Id);
            Assert.False(string.IsNullOrWhiteSpace(a.Json.GetProperty("title").GetString()), a.Id + ": title");
            Assert.NotEmpty(a.Paths);
            // A shared area runs the full suite; a list would only suggest it does not.
            if (a.Shared) Assert.True(a.Classes.Length == 0, a.Id + " is shared: its unitTests must be empty (the full suite)");
            foreach (var c in a.Classes) Assert.True(tests.Contains(c), a.Id + ": no test class " + c);
            Assert.Equal(a.Classes.Length, a.Classes.Distinct().Count());
            foreach (var s in a.Civil3d) Assert.Contains(s, Sections);
            if (a.Json.TryGetProperty("civil3dManual", out var manual))
                foreach (var runner in manual.EnumerateArray())
                    Assert.True(File.Exists(Path.Combine(Repo(), runner.GetString()!)), a.Id + ": no runner " + runner);

            var v = a.Json.GetProperty("verified");
            Assert.True(v.TryGetProperty("notes", out _), a.Id + ": verified.notes");
            Record(a.Id + " unit", v.GetProperty("unit"));
            var civil = v.GetProperty("civil3d");
            Record(a.Id + " civil3d", civil);
            if (civil.ValueKind == JsonValueKind.Object)
            {
                // Only the sections that check this area, and only ones that passed in full.
                var passed = civil.GetProperty("passed").EnumerateArray().Select(x => x.GetString()!).ToArray();
                Assert.NotEmpty(passed);
                Assert.All(passed, s => Assert.Contains(s, a.Civil3d));
            }
            if (v.TryGetProperty("installer", out var installer))
            {
                Assert.True(a.Json.TryGetProperty("installerTests", out var flag) && flag.GetBoolean(), a.Id + ": an installer record needs installerTests");
                foreach (var run in installer.EnumerateArray())
                {
                    Record(a.Id + " installer", run);
                    Assert.Contains(run.GetProperty("script").GetString()!, installers);
                }
            }
        }
    }

    private static void Record(string what, JsonElement record)
    {
        if (record.ValueKind == JsonValueKind.Null) return;
        Assert.True(record.ValueKind == JsonValueKind.Object, what + ": a record or null");
        Assert.Matches(Commit, record.GetProperty("commit").GetString()!);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", record.GetProperty("date").GetString()!);
        Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("where").GetString()), what + ": where");
    }

    [Fact]
    public void EveryPathOfTheMapMatchesAFileAndEveryFileOfTheCodeIsInAnArea()
    {
        var map = Load();
        var all = Files();
        foreach (var a in map.Areas)
            foreach (var (path, glob) in a.Paths.Zip(a.Globs))
                Assert.True(all.Any(glob.IsMatch), a.Id + ": " + path + " matches no file (a typo leaves its files to the full suite)");
        // The plugin, the core and the unit tests: a file in no area runs the full suite on every
        // change, which is safe but means nobody decided what tests it.
        var loose = Files("src").Concat(Files(Path.Combine("tests", "FieldCodes.Tests")))
            .Where(f => !map.Areas.Any(a => a.Has(f))).ToList();
        Assert.True(loose.Count == 0, "in no area of tests/areas.json: " + string.Join(", ", loose));
    }

    [Fact]
    public void EveryTestClassRunsWhenItsOwnFileChanges()
    {
        var map = Load();
        var classes = TestClasses();
        var nested = typeof(AreaMapTests).Assembly.GetTypes().Where(t => classes.Contains(t.Name) && (t.IsNested || t.Namespace != "FieldCodes.Tests"));
        Assert.True(!nested.Any(), "a test class the filters can name is a top-level class of FieldCodes.Tests: " + string.Join(", ", nested.Select(t => t.FullName)));
        var lost = new List<string>();
        foreach (var (file, cls) in TestSources())
        {
            var areas = map.Areas.Where(a => a.Has(file)).ToList();
            if (areas.Any(a => a.Shared) || map.AlwaysRun.Contains(cls)) continue;
            if (!areas.Any(a => a.Classes.Contains(cls))) lost.Add(cls + " (" + file + ")");
        }
        Assert.True(lost.Count == 0, "these test classes run in no area that their own file belongs to: " + string.Join(", ", lost));
    }

    /// <summary>Each test class with the file it is written in.</summary>
    private static IEnumerable<(string File, string Class)> TestSources()
    {
        var classes = TestClasses();
        var decl = new Regex(@"^\s*(?:public |internal )?(?:sealed |static |abstract )*(?:partial )?class\s+(\w+)", RegexOptions.Multiline);
        foreach (var file in Files(Path.Combine("tests", "FieldCodes.Tests")).Where(f => f.EndsWith(".cs", StringComparison.Ordinal)))
            foreach (Match m in decl.Matches(Code(File.ReadAllText(Path.Combine(Repo(), file)))))
                if (classes.Contains(m.Groups[1].Value)) yield return (file, m.Groups[1].Value);
    }

    // ----------------------------------------------------------------- what reaches what

    [Fact]
    public void ATestThatReachesAnAreasCodeRunsWhenThatAreaChanges()
    {
        var map = Load();
        var all = Files();
        var shared = all.Where(f => map.Areas.Any(a => a.Shared && a.Has(f))).ToHashSet();
        var core = all.Where(f => f.StartsWith("src/FieldCodes/", StringComparison.Ordinal) && f.EndsWith(".cs", StringComparison.Ordinal)).ToList();
        var code = core.ToDictionary(f => f, f => Code(File.ReadAllText(Path.Combine(Repo(), f))));

        // Which core file declares each type (a partial type: every file of it).
        var owners = new Dictionary<string, HashSet<string>>();
        var decl = new Regex(@"\b(?:public|internal)\s+(?:(?:sealed|static|abstract|partial|readonly|ref|unsafe)\s+)*(?:class|struct|enum|record|interface)\s+([A-Z]\w*)");
        foreach (var (file, text) in code)
            foreach (Match m in decl.Matches(text))
            {
                if (!owners.TryGetValue(m.Groups[1].Value, out var set)) owners[m.Groups[1].Value] = set = new HashSet<string>();
                set.Add(file);
            }
        var uses = code.ToDictionary(kv => kv.Key, kv => Owners(TypeUses(kv.Value, owners), owners, kv.Key));

        HashSet<string> Reach(IEnumerable<string> start)
        {
            var seen = new HashSet<string>(start);
            var todo = new Stack<string>(seen);
            while (todo.Count > 0)
            {
                var f = todo.Pop();
                if (shared.Contains(f)) continue;            // a shared file runs everything anyway
                foreach (var g in uses[f]) if (seen.Add(g)) todo.Push(g);
            }
            return seen;
        }

        var readPath = new Regex(@"""(src|tests|deploy|config|docs|LiveSmoke|tools)""((?:\s*,\s*""[^""]+"")*)");
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        // An area's own files; one that is also in a shared area runs the full suite instead.
        var areaFiles = map.Areas.Where(a => !a.Shared).ToDictionary(a => a, a => all.Where(f => a.Has(f) && !shared.Contains(f)).ToHashSet());
        var bySource = TestSources().GroupBy(t => t.File).ToDictionary(g => g.Key, g => g.Select(t => t.Class).ToArray());
        foreach (var (file, classes) in bySource)
        {
            var raw = File.ReadAllText(Path.Combine(Repo(), file));
            var reach = Reach(Owners(TypeUses(Code(raw), owners), owners, null));
            var reads = new HashSet<string>();
            foreach (Match m in readPath.Matches(raw))
            {
                var parts = new[] { m.Groups[1].Value }.Concat(Regex.Matches(m.Groups[2].Value, @"""([^""]+)""").Select(x => x.Groups[1].Value)).ToList();
                if (parts[0] == "LiveSmoke") parts.Insert(0, "tests");
                var path = string.Join("/", parts);
                reads.UnionWith(all.Where(f => f == path || f.StartsWith(path + "/", StringComparison.Ordinal)));
            }
            foreach (var (a, mine) in areaFiles)
            {
                string? why = mine.Contains(file) ? "is in the area"
                    : reach.FirstOrDefault(mine.Contains) is { } used ? "reaches " + used
                    : reads.FirstOrDefault(mine.Contains) is { } read ? "reads " + read
                    // It loads the plugin compiled against the stand-ins and checks every member the
                    // Civil 3D harnesses reach by reflection: any plugin file can break it.
                    : file.EndsWith("/HarnessContractTests.cs", StringComparison.Ordinal) && mine.Any(f => f.StartsWith("src/FieldCodes.Cad/", StringComparison.Ordinal)) ? "loads the compiled plugin"
                    : null;
                if (why == null) continue;
                foreach (var c in classes)
                    if (!a.Classes.Contains(c) && !map.AlwaysRun.Contains(c)) missing.Add(a.Id + ": " + c + " (" + why + ")");
            }
        }
        Assert.True(missing.Count == 0, "add these to the area's unitTests in tests/areas.json (or make the code they reach shared):\n  " + string.Join("\n  ", missing));
    }

    private static HashSet<string> Owners(IEnumerable<string> names, Dictionary<string, HashSet<string>> owners, string? self)
    {
        var found = new HashSet<string>();
        foreach (var n in names) foreach (var o in owners[n]) if (o != self) found.Add(o);
        return found;
    }

    // A type name in a place only a type can stand: new T, typeof(T), is T, as T, T<, T[], T?, T.Member,
    // T name, a cast, a generic argument, a base type. A property or enum member of the same name is
    // not a use of the type (Evidence the class vs. a string property called Evidence).
    private static readonly Regex Word = new(@"\b[A-Z]\w*\b");
    private static readonly Regex MemberAccess = new(@"\.\s*$");
    private static readonly Regex TypeBefore = new(@"(?:\bnew\s+|\btypeof\s*\(\s*|\bnameof\s*\(\s*|\bdefault\s*\(\s*|\bis\s+(?:not\s+)?|\bas\s+|<\s*|<[\w\s.,<>\[\]?]*,\s*)$");
    private static readonly Regex BaseBefore = new(@"\b(?:class|struct|interface|record)\s+\w+(?:\s*<[^>]*>)?\s*:\s*(?:[\w.<>]+\s*,\s*)*$");
    private static readonly Regex CastBefore = new(@"(?:^|[^\w\s)\]])\s*\(\s*$");
    private static readonly Regex TypeAfter = new(@"^(?:\s*\.\s*[A-Za-z_]|\s*\[\s*\]|\?(?=\s*@?[A-Za-z_]\w*\s*[=;,)])|\?\s*[>\[]|<|>(?!=)|\s+@?[A-Za-z_]\w*\s*(?:[=;,)\]{(:]|=>|\bin\b))");
    private static readonly Regex CastAfter = new(@"^\s*\)\s*[\w(@]");

    private static HashSet<string> TypeUses(string code, Dictionary<string, HashSet<string>> owners)
    {
        var found = new HashSet<string>();
        foreach (Match m in Word.Matches(code))
        {
            if (!owners.ContainsKey(m.Value) || found.Contains(m.Value)) continue;
            var from = Math.Max(0, m.Index - 80);
            var before = code.Substring(from, m.Index - from);
            var end = m.Index + m.Length;
            var after = code.Substring(end, Math.Min(60, code.Length - end));
            if (MemberAccess.IsMatch(before)) continue;
            if (TypeBefore.IsMatch(before) || BaseBefore.IsMatch(before) || TypeAfter.IsMatch(after) ||
                (CastBefore.IsMatch(before) && CastAfter.IsMatch(after)))
                found.Add(m.Value);
        }
        return found;
    }

    /// <summary>C# with comments, strings and character literals blanked, so words in them are not code.</summary>
    private static string Code(string text)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c == '/' && next == '/') { while (i < text.Length && text[i] != '\n') i++; sb.Append(' '); continue; }
            if (c == '/' && next == '*') { var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal); i = close < 0 ? text.Length : close + 2; sb.Append(' '); continue; }
            if (c == '\'' ) { i++; while (i < text.Length && text[i] != '\'' && text[i] != '\n') i += text[i] == '\\' ? 2 : 1; i++; sb.Append("' '"); continue; }
            if (c == '"' || ((c == '@' || c == '$') && (next == '"' || ((next == '@' || next == '$') && i + 2 < text.Length && text[i + 2] == '"'))))
            {
                var verbatim = false;
                while (text[i] != '"') { verbatim |= text[i] == '@'; i++; }
                i++;
                while (i < text.Length)
                {
                    if (verbatim) { if (text[i] == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { i += 2; continue; } break; } i++; }
                    else { if (text[i] == '\\') { i += 2; continue; } if (text[i] == '"' || text[i] == '\n') break; i++; }
                }
                i++;
                sb.Append("\"\"");
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }
}
