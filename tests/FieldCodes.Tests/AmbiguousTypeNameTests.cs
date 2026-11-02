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
/// Civil 3D names that clash with AutoCAD's. tests/RealApiCompileCheck compiles against the
/// real assemblies and is the authority; this is the quick check that needs nothing installed.
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
        // Civil 3D (AeccDbMgd) declares these in its DatabaseServices too.
        ("Entity", new[] { "Autodesk.Civil.DatabaseServices" }, new[] { "Autodesk.AutoCAD.DatabaseServices" }),
        ("Surface", new[] { "Autodesk.Civil.DatabaseServices" }, new[] { "Autodesk.AutoCAD.DatabaseServices" }),
        ("Section", new[] { "Autodesk.Civil.DatabaseServices" }, new[] { "Autodesk.AutoCAD.DatabaseServices" }),
    };

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

    [Fact]
    public void NoPluginFileUsesATypeNameTwoOfItsNamespacesDeclare()
    {
        var root = Root();
        var problems = new List<string>();
        foreach (var file in Sources(root))
        {
            var text = File.ReadAllText(file);
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
                    problems.Add(string.Format("{0}({1}): '{2}' is declared in both {3} and {4}; write the full name (or add 'using {2} = ...;')",
                        Path.GetRelativePath(root, file), line, name, string.Join(", ", a), string.Join(", ", b)));
            }
        }
        Assert.True(problems.Count == 0, "Ambiguous type names (CS0104 in the real build):\n" + string.Join("\n", problems));
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
