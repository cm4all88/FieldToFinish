using FieldCodes;
using FieldCodes.Compare;

namespace FieldCodes.Tests;

/// <summary>Builders for comparison scenarios: survey and consultant datasets made of
/// plain geometry, run through the real engine with the built-in profile.</summary>
public static class CF
{
    public static CompareProfile Profile()
    {
        var p = CompareProfile.CreateDefault();
        // The test datasets are small islands; the consultant-extent rule would make
        // everything beyond their bounds "outside limits". Scenarios that need limits
        // set them explicitly.
        p.Boundary.UseConsultantExtentWhenNoBoundary = false;
        // The fixed PMX-COMP-NP-* names keep the layer expectations in these tests short;
        // NonplotLayeringTests covers the default suffix mode.
        p.NonplotLayers.Mode = NonplotLayerNames.ModeFixed;
        return p;
    }

    public static CompareDataset Survey(string name = "survey.dwg") => new CompareDataset { Source = CompareSource.Survey, Name = name };
    public static CompareDataset Consultant(string name = "consultant.dwg") => new CompareDataset { Source = CompareSource.Consultant, Name = name };

    public static CompareFeature Line(string handle, string layer, params double[] xy)
    {
        var f = new CompareFeature { Handle = handle, Kind = FeatureKind.Line, EntityType = "LWPOLYLINE", Layer = layer, Vertices = new List<CPoint>(), OwnerPath = "" };
        for (var i = 0; i + 1 < xy.Length; i += 2) f.Vertices.Add(new CPoint(xy[i], xy[i + 1]));
        return f;
    }

    public static CompareFeature Line3(string handle, string layer, params double[] xyz)
    {
        var f = new CompareFeature { Handle = handle, Kind = FeatureKind.Line, EntityType = "POLYLINE3D", Layer = layer, Vertices = new List<CPoint>(), OwnerPath = "" };
        for (var i = 0; i + 2 < xyz.Length; i += 3) f.Vertices.Add(new CPoint(xyz[i], xyz[i + 1], xyz[i + 2]));
        return f;
    }

    public static CompareFeature Figure(string handle, string figureName, string layer, params double[] xy)
    {
        var f = Line(handle, layer, xy);
        f.EntityType = "SURVEYFIGURE";
        f.Description = figureName;
        return f;
    }

    public static CompareFeature Block(string handle, string layer, string block, double x, double y, double? z = null, Dictionary<string, string>? attributes = null)
    {
        return new CompareFeature
        {
            Handle = handle, Kind = FeatureKind.Point, EntityType = "INSERT", Layer = layer, BlockName = block,
            Position = z.HasValue ? new CPoint(x, y, z.Value) : new CPoint(x, y), Elevation = z, Attributes = attributes, OwnerPath = ""
        };
    }

    public static CompareFeature Cogo(string handle, string description, double x, double y, double z, string layer = "V-NODE")
    {
        return new CompareFeature
        {
            Handle = handle, Kind = FeatureKind.Point, EntityType = "COGOPOINT", Layer = layer, Description = description,
            Position = new CPoint(x, y, z), Elevation = z, OwnerPath = ""
        };
    }

    public static CompareFeature Text(string handle, string layer, string text, double x, double y, string? owner = null)
    {
        return new CompareFeature
        {
            Handle = handle, Kind = FeatureKind.Annotation, EntityType = "TEXT", Layer = layer, Description = text,
            Position = new CPoint(x, y), AssociatedFeatureId = owner, OwnerPath = ""
        };
    }

    public static ComparisonResult Run(CompareDataset survey, CompareDataset consultant, CompareProfile? profile = null)
    {
        profile ??= Profile();
        var result = new ComparisonEngine(profile).Run(survey, consultant, null!);
        ChangePlanner.Build(result, profile);
        return result;
    }

    public static FeatureResult R(ComparisonResult result, string handle)
    {
        var r = result.Find(handle);
        Assert.NotNull(r);
        return r!;
    }
}
