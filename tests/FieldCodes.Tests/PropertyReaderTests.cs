using System.Reflection;

namespace FieldCodes.Tests;

/// <summary>
/// PropertyReader reads Civil 3D members by reflection without the AmbiguousMatchException that
/// Type.GetProperty(name) throws for a property declared again lower in the class hierarchy. At
/// 65d1c05 that exception, caught and dropped, left every point style's name and every point label
/// unread in the accountability smoke test (HY "marker: style (?)", SG "could not be read").
/// </summary>
public sealed class PropertyReaderTests
{
    private class StyleBaseLike { public string Name => "base name"; public int Count => 1; }
    // Declared again with another type, as a C++/CLI wrapper can: GetProperty("Name") is ambiguous.
    private class PointStyleLike : StyleBaseLike { public new object Name => "derived name"; public string MarkerSymbolName { get; set; } = "PMX-WFH"; }
    private class Throws { public string Name => throw new InvalidOperationException("not available in this context"); }

    [Fact]
    public void GetPropertyThrowsOnARedeclaredPropertyAndTheReaderDoesNot()
    {
        // The failure mode, reproduced: this is what the readers used to call.
        Assert.Throws<AmbiguousMatchException>(() => typeof(PointStyleLike).GetProperty("Name", BindingFlags.Public | BindingFlags.Instance));

        Assert.Equal("derived name", PropertyReader.Read(new PointStyleLike(), "Name"));
        Assert.Equal(typeof(PointStyleLike), PropertyReader.Find(typeof(PointStyleLike), "Name")!.DeclaringType);
        Assert.Equal("PMX-WFH", PropertyReader.Read(new PointStyleLike(), "MarkerSymbolName"));
        Assert.Equal(1, PropertyReader.Read(new PointStyleLike(), "Count"));
    }

    [Fact]
    public void WhatCannotBeReadSaysWhy()
    {
        Assert.False(PropertyReader.TryRead(new PointStyleLike(), "LabelStyleId", out var value, out var problem));
        Assert.Null(value);
        Assert.Contains("has no readable LabelStyleId", problem);

        Assert.False(PropertyReader.TryRead(new Throws(), "Name", out _, out problem));
        Assert.Contains("InvalidOperationException", problem);
        Assert.Contains("not available in this context", problem);
        Assert.Null(PropertyReader.Read(new Throws(), "Name"));

        Assert.False(PropertyReader.TryRead(null, "Name", out _, out problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void AWritablePropertyIsFoundTheSameWay()
    {
        Assert.NotNull(PropertyReader.Find(typeof(PointStyleLike), "MarkerSymbolName", writable: true));
        Assert.Null(PropertyReader.Find(typeof(PointStyleLike), "Name", writable: true));     // read-only
    }

    [Fact]
    public void ThePluginReadsCivilMembersThroughTheReaderOrTyped()
    {
        // The accountability readers no longer call Type.GetProperty(name) themselves.
        var root = Root();
        foreach (var file in new[] { "Accountability.cs", Path.Combine("Compare", "CogoPointPlotting.cs") })
        {
            var text = File.ReadAllText(Path.Combine(root, "src", "FieldCodes.Cad", file));
            Assert.DoesNotContain(".GetProperty(", text);
        }
        var marker = File.ReadAllText(Path.Combine(root, "src", "FieldCodes.Cad", "Accountability.cs"));
        Assert.Contains("pointStyle.Name", marker);
        Assert.Contains("pointStyle.MarkerType.ToString()", marker);
        Assert.Contains("pointStyle.MarkerSymbolName", marker);
        var label = File.ReadAllText(Path.Combine(root, "src", "FieldCodes.Cad", "Compare", "CogoPointPlotting.cs"));
        Assert.Contains("point.LabelStyleId", label);
        Assert.Contains("labelStyle.Name", label);
    }

    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "FieldToFinish.sln"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
