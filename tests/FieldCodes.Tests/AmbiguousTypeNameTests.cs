using System.Text.RegularExpressions;

namespace FieldCodes.Tests;

/// <summary>
/// A type name that two imported namespaces both declare is a compile error (CS0104) in the
/// real build, but not in tests/CadCompileCheck, whose hand-written stand-ins declare only the
/// Autodesk types the plugin uses. That is how
/// <c>Font</c> in LayersPage.cs (Autodesk.AutoCAD.DatabaseServices.Font and System.Drawing.Font)
/// reached a Windows build. This test reads every plugin source file and fails on a bare use of
/// a name both sides of its <c>using</c> directives declare.
///
/// The table is every public type name the real AutoCAD 2024 assemblies (AcDbMgd, AcCoreMgd,
/// AcMgd, AdWindows from the AutoCAD.NET 24.3.0 package) share with the .NET Framework
/// namespaces the plugin imports, found by reflection over those assemblies; plus the
/// Civil 3D names known to clash with AutoCAD's or .NET's. tests/RealApiCompileCheck compiles
/// against the real assemblies and is the authority; this is the quick check that needs nothing
/// installed.
///
/// Civil 3D's assemblies are not published, so no table of Civil names can be known complete
/// here: DBObject in Accountability.cs (fb3d1b2) was missing from it and reached the Windows
/// build. A file therefore names Civil 3D types through aliases
/// (<c>using CivDb = Autodesk.Civil.DatabaseServices;</c>), which nothing can make ambiguous.
/// Only the files that already imported a Civil namespace plainly, and build on Windows, may
/// keep doing so (<see cref="PlainCivilImports"/>); a new file cannot.
/// </summary>
public sealed class AmbiguousTypeNameTests
{
    private static readonly (string Name, string[] Autodesk, string[] Other)[] Clashes =
    {
        ("Application", new[] { "Autodesk.AutoCAD.ApplicationServices", "Autodesk.AutoCAD.ApplicationServices.Core" }, new[] { "System.Windows", "System.Windows.Forms" }),
        ("AttributeCollection", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System.ComponentModel" }),
        ("Color", new[] { "Autodesk.AutoCAD.Colors" }, new[] { "System.Drawing", "System.Windows.Media" }),
        ("ColorConverter", new[] { "Autodesk.AutoCAD.Colors" }, new[] { "System.Drawing", "System.Windows.Media" }),
        ("ColorDialog", new[] { "Autodesk.AutoCAD.Windows" }, new[] { "System.Windows.Forms" }),
        ("CursorType", new[] { "Autodesk.AutoCAD.EditorInput" }, new[] { "System.Windows.Input" }),
        ("DoubleCollection", new[] { "Autodesk.AutoCAD.Geometry" }, new[] { "System.Windows.Media" }),
        ("Ellipse", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System.Windows.Shapes" }),
        ("EnumConverter", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System.ComponentModel" }),
        ("Exception", new[] { "Autodesk.AutoCAD.Runtime" }, new[] { "System" }),
        ("ExecutionContext", new[] { "Autodesk.AutoCAD.Windows.ToolPalette" }, new[] { "System.Threading" }),
        ("Expression", new[] { "Autodesk.AutoCAD.Windows.Data" }, new[] { "System.Windows" }),
        ("FlowDirection", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System.Windows", "System.Windows.Forms" }),
        ("Font", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System.Drawing" }),
        ("Geometry", new[] { "Autodesk.AutoCAD.GraphicsInterface" }, new[] { "System.Windows.Media" }),
        ("Group", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System.Text.RegularExpressions" }),
        ("HatchStyle", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System.Drawing.Drawing2D" }),
        ("HelpProvider", new[] { "Autodesk.AutoCAD.Internal.Forms" }, new[] { "System.Windows.Forms" }),
        ("Image", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System.Drawing", "System.Windows.Controls" }),
        ("ImageSource", new[] { "Autodesk.AutoCAD.GraphicsInterface" }, new[] { "System.Windows.Media" }),
        ("Int32Collection", new[] { "Autodesk.AutoCAD.Geometry" }, new[] { "System.Windows.Media" }),
        ("LayoutEventArgs", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System.Windows.Forms" }),
        ("LayoutEventHandler", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System.Windows.Forms" }),
        ("Line", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System.Windows.Shapes" }),
        ("Menu", new[] { "Autodesk.AutoCAD.Windows" }, new[] { "System.Windows.Controls", "System.Windows.Forms" }),
        ("MenuItem", new[] { "Autodesk.AutoCAD.Windows" }, new[] { "System.Windows.Controls", "System.Windows.Forms" }),
        ("OpenFileDialog", new[] { "Autodesk.AutoCAD.Windows" }, new[] { "System.Windows.Forms" }),
        ("Polyline", new[] { "Autodesk.AutoCAD.DatabaseServices", "Autodesk.AutoCAD.GraphicsInterface" }, new[] { "System.Windows.Shapes" }),
        ("Region", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System.Drawing" }),
        ("SaveFileDialog", new[] { "Autodesk.AutoCAD.Windows" }, new[] { "System.Windows.Forms" }),
        ("SelectionMode", new[] { "Autodesk.AutoCAD.EditorInput" }, new[] { "System.Windows.Controls", "System.Windows.Forms" }),
        ("Shape", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System.Windows.Shapes" }),
        ("StatusBar", new[] { "Autodesk.AutoCAD.Windows" }, new[] { "System.Windows.Forms" }),
        ("SynchronizationContext", new[] { "Autodesk.AutoCAD.Runtime" }, new[] { "System.Threading" }),
        ("TextAlignment", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System.Windows" }),
        ("TimeZone", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System" }),
        ("Trace", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System.Diagnostics" }),
        ("TypeDescriptor", new[] { "Autodesk.AutoCAD.ComponentModel" }, new[] { "System.ComponentModel" }),
        ("View", new[] { "Autodesk.AutoCAD.GraphicsSystem" }, new[] { "System.Windows.Forms" }),
        ("Visibility", new[] { "Autodesk.AutoCAD.DatabaseServices" }, new[] { "System.Windows" }),
        ("Window", new[] { "Autodesk.AutoCAD.Windows" }, new[] { "System.Windows" }),
        // Civil 3D (AeccDbMgd) declares these in its DatabaseServices too. Each has a stand-in in
        // tests/CadCompileCheck/Stubs (Civil.cs or CivilShadows.cs), so the compile checks fail
        // on it as the Windows build does (EveryCivilNameInTheTableIsDeclaredInTheStandIns).
        ("DBObject", new[] { "Autodesk.Civil.DatabaseServices" }, new[] { "Autodesk.AutoCAD.DatabaseServices" }),
        ("Entity", new[] { "Autodesk.Civil.DatabaseServices" }, new[] { "Autodesk.AutoCAD.DatabaseServices" }),
        ("Surface", new[] { "Autodesk.Civil.DatabaseServices" }, new[] { "Autodesk.AutoCAD.DatabaseServices" }),
        ("Section", new[] { "Autodesk.Civil.DatabaseServices" }, new[] { "Autodesk.AutoCAD.DatabaseServices" }),
        ("Assembly", new[] { "Autodesk.Civil.DatabaseServices" }, new[] { "System.Reflection" }),
        ("Label", new[] { "Autodesk.Civil.DatabaseServices" }, new[] { "System.Windows.Forms", "System.Windows.Controls" }),
    };

    /// <summary>
    /// The files that import a Civil 3D namespace plainly (<c>using Autodesk.Civil...;</c>). They
    /// predate the alias rule and build on Windows as they are. The list only shrinks: a file
    /// moved to aliases must come off it, and no file may be added. Every other file names Civil
    /// types through an alias.
    /// </summary>
    internal static readonly string[] PlainCivilImports =
    {
        "src/FieldCodes.Cad/BenchCommands.cs",
        "src/FieldCodes.Cad/CadUtil.cs",
        "src/FieldCodes.Cad/CogoNotes.cs",
        "src/FieldCodes.Cad/Compare/CogoPointPlotting.cs",
        "src/FieldCodes.Cad/Compare/CompareDrawingReader.cs",
        "src/FieldCodes.Cad/Compare/CompareLayerManager.cs",
        "src/FieldCodes.Cad/Compare/PlotVisibility.cs",
        "src/FieldCodes.Cad/InsertedBlocks.cs",
        "src/FieldCodes.Cad/OrientationCommands.cs",
        "src/FieldCodes.Cad/TreeCommands.cs",
        "src/FieldCodes.Cad/UtilityCadService.cs",
        "src/FieldCodes.Cad/UtilityCommands.cs",
        "tests/LiveSmoke/CompareTestHarness.cs",
    };

    private static readonly Regex PlainCivilUsing = new(@"^\s*using\s+(Autodesk\.(?:Civil|Aec)(?:\.[\w.]+)?)\s*;", RegexOptions.Multiline);

    private static readonly Regex UsingNamespace = new(@"^\s*using\s+([A-Za-z_][\w.]*)\s*;", RegexOptions.Multiline);
    private static readonly Regex UsingAlias = new(@"^\s*using\s+([A-Za-z_]\w*)\s*=", RegexOptions.Multiline);
    private static readonly Regex Cref = new(@"cref\s*=\s*""([^""]*)""");

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FieldToFinish.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>Code with comments and string contents blanked (line breaks kept, so line
    /// numbers still match), except doc-comment crefs, which the compiler binds too.</summary>
    internal static string CodeOnly(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                var end = text.IndexOf('\n', i);
                if (end < 0) end = text.Length;
                var comment = text.Substring(i, end - i);
                foreach (Match m in Cref.Matches(comment)) sb.Append(' ').Append(m.Groups[1].Value);
                i = end;
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 2;
                foreach (var ch in text.Substring(i, end - i)) if (ch == '\n') sb.Append('\n');
                i = end;
                continue;
            }
            if (c == '"' || c == '\'')
            {
                var verbatim = c == '"' && i > 0 && text[i - 1] == '@';
                sb.Append(c);
                i++;
                while (i < text.Length)
                {
                    var d = text[i];
                    if (d == '\n') { sb.Append('\n'); if (!verbatim) break; i++; continue; }
                    if (!verbatim && d == '\\') { i += 2; continue; }
                    if (verbatim && d == '"' && i + 1 < text.Length && text[i + 1] == '"') { i += 2; continue; }
                    if (d == c) { sb.Append(c); i++; break; }
                    i++;
                }
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>Bare uses of <paramref name="name"/> as a type: not qualified (no dot before
    /// it), not a member being set in an initializer (Font = ...) and not a value read as an
    /// expression (var f = Font; -- a control's own Font property).</summary>
    internal static IEnumerable<int> BareUses(string code, string name)
    {
        var pattern = new Regex(@"(?<![\w.@])" + Regex.Escape(name) + @"\b(?!\s*=[^=>])(?!\s*:)(?!\s*[;,)\]])");
        foreach (Match m in pattern.Matches(code))
            yield return code.Take(m.Index).Count(ch => ch == '\n') + 1;
    }

    private static IEnumerable<string> Sources(string root)
    {
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "src", "FieldCodes.Cad"), "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
            yield return f;
        }
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "tests", "LiveSmoke"), "*.cs"))
            yield return f;
    }

    /// <summary>The ambiguous bare names in one file's text.</summary>
    internal static IEnumerable<string> Ambiguities(string label, string text)
    {
        var imported = new HashSet<string>(UsingNamespace.Matches(text).Select(m => m.Groups[1].Value));
        var aliased = new HashSet<string>(UsingAlias.Matches(text).Select(m => m.Groups[1].Value));
        string? code = null;
        foreach (var (name, autodesk, other) in Clashes)
        {
            if (aliased.Contains(name)) continue;
            var a = autodesk.Where(imported.Contains).ToList();
            var b = other.Where(imported.Contains).ToList();
            if (a.Count == 0 || b.Count == 0) continue;
            code ??= CodeOnly(text);
            foreach (var line in BareUses(code, name).Distinct())
                yield return string.Format("{0}({1}): '{2}' is declared in both {3} and {4}; write the full name (or add 'using {2} = ...;')",
                    label, line, name, string.Join(", ", a), string.Join(", ", b));
        }
    }

    /// <summary>The Civil 3D namespaces a file imports plainly, when it may not.</summary>
    internal static IEnumerable<string> ForbiddenCivilImports(string label, string text)
    {
        if (PlainCivilImports.Contains(label)) yield break;
        var code = CodeOnly(text);
        foreach (Match m in PlainCivilUsing.Matches(code))
            yield return string.Format("{0}({1}): 'using {2};' -- name Civil 3D types through an alias instead (using CivDb = Autodesk.Civil.DatabaseServices;). " +
                                       "Civil 3D declares DBObject, Entity and other names AutoCAD and .NET declare too, and its assemblies are not here to check against.",
                label, code.Take(m.Index).Count(ch => ch == '\n') + 1, m.Groups[1].Value);
    }

    private static string Label(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');

    [Fact]
    public void NoPluginFileUsesATypeNameTwoOfItsNamespacesDeclare()
    {
        var root = Root();
        var problems = Sources(root).SelectMany(f => Ambiguities(Label(root, f), File.ReadAllText(f))).ToList();
        Assert.True(problems.Count == 0, "Ambiguous type names (CS0104 in the real build):\n" + string.Join("\n", problems));
    }

    [Fact]
    public void OnlyTheListedOlderFilesImportACivilNamespacePlainly()
    {
        var root = Root();
        var problems = Sources(root).SelectMany(f => ForbiddenCivilImports(Label(root, f), File.ReadAllText(f))).ToList();
        Assert.True(problems.Count == 0, "Plain Civil 3D imports:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void TheListOfOlderFilesOnlyShrinks()
    {
        // A file that no longer imports a Civil namespace plainly comes off the list, so the
        // exemption cannot be reused by new code put in its place.
        var root = Root();
        foreach (var label in PlainCivilImports)
        {
            var path = Path.Combine(root, label);
            Assert.True(File.Exists(path), label + " is listed in PlainCivilImports but no longer exists; take it off the list.");
            Assert.True(PlainCivilUsing.IsMatch(CodeOnly(File.ReadAllText(path))),
                label + " no longer imports a Civil 3D namespace plainly; take it off PlainCivilImports.");
        }
        Assert.Equal(PlainCivilImports.Length, PlainCivilImports.Distinct().Count());
    }

    [Fact]
    public void EveryCivilNameInTheTableIsDeclaredInTheStandIns()
    {
        // So the compile checks fail on it too, as the real build does, not only this lint.
        var root = Root();
        var declared = StandInDeclarations(root);
        foreach (var (name, autodesk, _) in Clashes)
            foreach (var ns in autodesk.Where(n => n.StartsWith("Autodesk.Civil", StringComparison.Ordinal)))
                Assert.True(declared.Contains(ns + "." + name),
                    ns + "." + name + " is in the clash table but no Civil stand-in declares it; add it to tests/CadCompileCheck/Stubs/CivilShadows.cs.");
    }

    /// <summary>Namespace-qualified names of the types the Civil stand-ins declare.</summary>
    private static HashSet<string> StandInDeclarations(string root)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var stubs = Path.Combine(root, "tests", "CadCompileCheck", "Stubs");
        foreach (var file in new[] { "Civil.cs", "CivilShadows.cs", "CivilApiNames.g.cs" }.Select(f => Path.Combine(stubs, f)).Where(File.Exists))
        {
            string? ns = null;
            foreach (var line in CodeOnly(File.ReadAllText(file)).Split('\n'))
            {
                var n = Regex.Match(line, @"^\s*namespace\s+([\w.]+)");
                if (n.Success) { ns = n.Groups[1].Value; continue; }
                foreach (Match t in Regex.Matches(line, @"\b(?:class|struct|enum|interface)\s+(\w+)"))
                    if (ns != null) declared.Add(ns + "." + t.Groups[1].Value);
            }
        }
        return declared;
    }

    [Fact]
    public void TheCheckFindsTheDBObjectThatBrokeTheWindowsBuild()
    {
        // Accountability.cs as it was at fb3d1b2: AutoCAD's and Civil 3D's DatabaseServices both
        // imported, DBObject used bare (CS0104 at 361,55 and 478,44 in the Windows build).
        const string before =
            "using System.Collections.Generic;\nusing System.Reflection;\n" +
            "using Autodesk.AutoCAD.DatabaseServices;\nusing Autodesk.Civil.ApplicationServices;\nusing Autodesk.Civil.DatabaseServices;\n" +
            "class SurfaceRead {\n  public readonly List<KeyValuePair<string, DBObject>> Groups = new List<KeyValuePair<string, DBObject>>();\n" +
            "  static bool? ContainsPoint(DBObject group, uint number) { return null; }\n" +
            "  void Read(Transaction tr, ObjectId id) { var s = tr.GetObject(id, OpenMode.ForRead) as TinSurface; }\n}\n";
        const string label = "src/FieldCodes.Cad/Accountability.cs";
        var found = Ambiguities(label, before).ToList();
        Assert.Equal(2, found.Count);
        Assert.Contains(found, p => p.StartsWith(label + "(7): 'DBObject' is declared in both Autodesk.Civil.DatabaseServices and Autodesk.AutoCAD.DatabaseServices", StringComparison.Ordinal));
        Assert.Contains(found, p => p.StartsWith(label + "(8): 'DBObject'", StringComparison.Ordinal));
        // And the file is not one of the older ones, so its plain Civil imports are refused even
        // for a name the table does not hold.
        Assert.Equal(new[] { 4, 5 }, ForbiddenCivilImports(label, before).Select(p => int.Parse(Regex.Match(p, @"\((\d+)\)").Groups[1].Value)).ToArray());

        // The correction: Civil 3D through aliases, DBObject is AutoCAD's.
        var after = before.Replace("using Autodesk.Civil.ApplicationServices;\nusing Autodesk.Civil.DatabaseServices;\n",
                                   "using CivApp = Autodesk.Civil.ApplicationServices;\nusing CivDb = Autodesk.Civil.DatabaseServices;\n")
                          .Replace("as TinSurface", "as CivDb.TinSurface");
        Assert.Empty(Ambiguities(label, after));
        Assert.Empty(ForbiddenCivilImports(label, after));
        // A listed older file may keep its plain import; the table still guards its bare names.
        Assert.Empty(ForbiddenCivilImports("src/FieldCodes.Cad/CadUtil.cs", before));
        Assert.Equal(2, Ambiguities("src/FieldCodes.Cad/CadUtil.cs", before).Count());
        // A commented-out import is not an import.
        Assert.Empty(ForbiddenCivilImports(label, "// using Autodesk.Civil.DatabaseServices;\nclass X {}"));
    }

    [Fact]
    public void TheCheckFindsTheFontThatBrokeTheWindowsBuild()
    {
        // LayersPage.cs as it was at cf3e6f4: System.Drawing and AutoCAD's DatabaseServices
        // both imported, Font used bare.
        var code = CodeOnly("using System.Drawing;\nusing Autodesk.AutoCAD.DatabaseServices;\n" +
                            "class P {\n  private static readonly Font Italic = new Font(SystemFonts.DefaultFont, FontStyle.Italic);\n" +
                            "  void A(Label l) { l.Font = Italic; var x = new Label { Font = Italic }; }\n" +
                            "  // a Font in a comment\n  string s = \"Font\";\n  System.Drawing.Font ok;\n}\n");
        Assert.Equal(new[] { 4, 4 }, BareUses(code, "Font").ToArray());
        Assert.Contains(5, BareUses(CodeOnly("\n\n\n\n/// <see cref=\"Font\"/>\nclass X {}"), "Font"));
        Assert.Empty(BareUses(CodeOnly("class C : Control { void P() { var font = Font; Use(Font, 1); } }"), "Font"));
    }
}
