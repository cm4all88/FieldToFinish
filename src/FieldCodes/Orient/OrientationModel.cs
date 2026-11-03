using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Easements;

namespace FieldCodes.Orient
{
    /// <summary>What a point is, for orientation.</summary>
    public enum OrientKind
    {
        Other = 0,
        Sign,
        /// <summary>A street light with an arm (the plain light symbol is round).</summary>
        StreetLight,
        /// <summary>A traffic signal pole: its mast arm reaches over the approach it serves.</summary>
        SignalPole,
        Hydrant,
        WaterValve,
        GasValve,
        /// <summary>Straight, turn and combined arrows: they point the way traffic travels.</summary>
        PavementArrow,
        /// <summary>STOP, ONLY, SCHOOL...: they read to the approaching driver.</summary>
        PavementWord
    }

    /// <summary>
    /// How an orientation was decided, in the order they are tried. Lower is stronger; a
    /// measured orientation always wins over anything inferred.
    /// </summary>
    public enum OrientationTier
    {
        /// <summary>Recorded by the crew: an azimuth or cardinal in the description or note,
        /// or a shot defining the feature's own direction (a sign panel line).</summary>
        Measured = 1,
        /// <summary>From measured features around it: a stop bar, an intersection approach,
        /// a measured arrow, a valve cluster and its hydrant.</summary>
        Relationship = 2,
        /// <summary>Along surveyed road or utility linework (two-way right-hand traffic for
        /// roads; the mapped or painted line for valves).</summary>
        Alignment = 3,
        /// <summary>A feature-specific assumption supported by what is around it (a hydrant's
        /// pumper nozzle faces the street).</summary>
        Inferred = 4,
        /// <summary>Nothing reliable: left as it is and listed for review.</summary>
        Review = 5,
        /// <summary>The symbol looks the same at every rotation: nothing to orient.</summary>
        NotDirectional = 6,
        /// <summary>Its standard does not require it turned and nothing recorded its direction (a
        /// private or address sign): left as surveyed, never assumed to face a road, and not a
        /// review item.</summary>
        NoSupport = 7
    }

    /// <summary>One point to orient.</summary>
    public sealed class OrientFeature
    {
        public string Id { get; set; }
        public string Code { get; set; }
        public OrientKind Kind { get; set; }
        public P2 Location { get; set; }
        public double Elevation { get; set; }

        /// <summary>The raw description and the crew's note, for measured directions and for
        /// telling a stop sign from another sign.</summary>
        public string Description { get; set; }
        /// <summary>False: its drafting standard does not require it turned (a private or address
        /// sign), so only a direction the crew recorded turns it -- never an inference from the
        /// roads around it. Null: inferred as its kind allows.</summary>
        public bool? InferFacing { get; set; }
        /// <summary>Civil 3D's full description (the description key's wording, e.g. "STRAIGHT
        /// ARROW S"); a written direction is looked for here too.</summary>
        public string FullDescription { get; set; }
        public string Note { get; set; }

        /// <summary>A direction the crew recorded, as a survey azimuth (degrees clockwise from
        /// north) of the symbol's reference direction: a sign's face, a hydrant's nozzle, an
        /// arm, a pipe axis. Null when none.</summary>
        public double? MeasuredAzimuth { get; set; }
        public string MeasuredFrom { get; set; }

        /// <summary>A pavement marking's direction of travel as the crew wrote it ("ASTO S":
        /// 180), before it is matched to the lane; null when none was written.</summary>
        public double? StatedTravelAzimuth { get; set; }
        /// <summary>The words it was read from ("S", "SOUTHBOUND", "S/B").</summary>
        public string StatedTravelWords { get; set; }

        /// <summary>Further measured directions (a second arm, a second pipe).</summary>
        public IList<double> ExtraMeasuredAzimuths { get; set; }

        /// <summary>The marker's rotation now, CAD radians.</summary>
        public double CurrentRotation { get; set; }

        public OrientFeature() { ExtraMeasuredAzimuths = new List<double>(); }

        public override string ToString() { return Code + " " + Id + " " + Location; }
    }

    /// <summary>What a line is, for orientation.</summary>
    public enum LineRole
    {
        Other = 0,
        /// <summary>Road centre: two-way traffic keeps to the right of it.</summary>
        Centerline,
        /// <summary>One-way road or carriageway, drawn in the direction of travel.</summary>
        OneWay,
        /// <summary>Edge of pavement or curb.</summary>
        Edge,
        LaneLine,
        StopBar,
        Crosswalk,
        WaterMain,
        GasMain,
        /// <summary>A shot sign panel: the panel's own line.</summary>
        SignPanel
    }

    /// <summary>Surveyed linework, read only.</summary>
    public sealed class OrientLine
    {
        public string Id { get; set; }
        public string Layer { get; set; }
        public LineRole Role { get; set; }
        public IList<P2> Points { get; set; }

        public OrientLine() { Points = new List<P2>(); }

        public double Length
        {
            get
            {
                var l = 0.0;
                for (var i = 1; i < Points.Count; i++) l += Points[i - 1].DistanceTo(Points[i]);
                return l;
            }
        }

        /// <summary>The closest point on the line, the unit tangent there (in the drawn
        /// direction), the distance, and the station along the line.</summary>
        public bool Nearest(P2 p, out P2 foot, out P2 tangent, out double distance, out double station)
        {
            foot = default(P2); tangent = default(P2); distance = double.MaxValue; station = 0;
            if (Points.Count < 2) return false;
            var run = 0.0;
            for (var i = 1; i < Points.Count; i++)
            {
                var a = Points[i - 1];
                var b = Points[i];
                var ab = b - a;
                var len = ab.Length;
                if (len < 1e-9) continue;
                var t = Math.Max(0, Math.Min(1, P2.Dot(p - a, ab) / (len * len)));
                var f = a + ab * t;
                var d = f.DistanceTo(p);
                if (d < distance)
                {
                    distance = d;
                    foot = f;
                    tangent = ab.Normalized();
                    station = run + t * len;
                }
                run += len;
            }
            return distance < double.MaxValue;
        }

        public P2 Start { get { return Points[0]; } }
        public P2 End { get { return Points[Points.Count - 1]; } }

        /// <summary>Unit direction leaving the line at its start (inward) or end (outward).</summary>
        public P2 StartDirection
        {
            get
            {
                for (var i = 1; i < Points.Count; i++)
                    if (Points[i].DistanceTo(Points[0]) > 1e-6) return (Points[i] - Points[0]).Normalized();
                return new P2(1, 0);
            }
        }

        public P2 EndDirection
        {
            get
            {
                var n = Points.Count - 1;
                for (var i = n - 1; i >= 0; i--)
                    if (Points[n].DistanceTo(Points[i]) > 1e-6) return (Points[n] - Points[i]).Normalized();
                return new P2(1, 0);
            }
        }
    }

    /// <summary>One reason behind a decision.</summary>
    public sealed class Evidence
    {
        public OrientationTier Tier { get; set; }
        public string Text { get; set; }
        public double Confidence { get; set; }
        public Evidence(OrientationTier tier, double confidence, string text) { Tier = tier; Confidence = confidence; Text = text; }
        public override string ToString() { return Text; }
    }

    /// <summary>
    /// A probable underground connection read from surface evidence (a valve cluster and its
    /// hydrant). Always inferred: it is shown and reported, never drawn as utility linework.
    /// </summary>
    public sealed class InferredConnection
    {
        public string Kind { get; set; }          // "water main", "hydrant lateral"
        public P2 From { get; set; }
        public P2 To { get; set; }
        public IList<string> FeatureIds { get; set; }
        public double Confidence { get; set; }
        public InferredConnection() { FeatureIds = new List<string>(); }
    }

    /// <summary>The outcome for one point.</summary>
    public sealed class OrientationDecision
    {
        public OrientFeature Feature { get; set; }

        /// <summary>The symbol standard for the feature's code (null: none configured).</summary>
        public SymbolRule Symbol { get; set; }

        public OrientationTier Tier { get; set; }
        public double Confidence { get; set; }

        /// <summary>The direction the symbol's reference should point, survey azimuth. Null
        /// for review or not-directional.</summary>
        public double? Azimuth { get; set; }

        /// <summary>A second direction worth showing for review (the other candidate).</summary>
        public double? AlternativeAzimuth { get; set; }

        /// <summary>The marker rotation that achieves <see cref="Azimuth"/> for this symbol,
        /// CAD radians, counter-clockwise from east, normalised to the symbol's period.</summary>
        public double? MarkerRotation { get; set; }

        public IList<Evidence> Evidence { get; set; }
        public string ReviewReason { get; set; }

        /// <summary>What the feature was associated with: an approach, a stop bar, a cluster.</summary>
        public string Association { get; set; }
        public P2? AssociatedPoint { get; set; }

        public OrientationDecision() { Evidence = new List<Evidence>(); }

        public bool Applies(double minimumConfidence, OrientationTier weakestTier)
        {
            return MarkerRotation.HasValue && Tier <= weakestTier && Tier < OrientationTier.Review &&
                   Confidence >= minimumConfidence;
        }

        public string Summary()
        {
            var az = Azimuth.HasValue ? Azimuth.Value.ToString("0.0", CultureInfo.InvariantCulture) : "-";
            return string.Format(CultureInfo.InvariantCulture, "{0} {1}: {2} az {3} ({4:0.00}){5}",
                Feature.Code, Feature.Id, Tier, az, Confidence,
                string.IsNullOrEmpty(ReviewReason) ? "" : " REVIEW: " + ReviewReason);
        }
    }

    /// <summary>Everything the engine reads.</summary>
    public sealed class OrientationScene
    {
        public IList<OrientFeature> Features { get; set; }
        public IList<OrientLine> Lines { get; set; }
        public OrientationScene() { Features = new List<OrientFeature>(); Lines = new List<OrientLine>(); }
    }

    /// <summary>Everything the engine decided.</summary>
    public sealed class OrientationResult
    {
        public IList<OrientationDecision> Decisions { get; set; }
        public IList<InferredConnection> Connections { get; set; }
        public IList<Intersection> Intersections { get; set; }
        public OrientationResult()
        {
            Decisions = new List<OrientationDecision>();
            Connections = new List<InferredConnection>();
            Intersections = new List<Intersection>();
        }

        public OrientationDecision For(string id) { return Decisions.FirstOrDefault(d => d.Feature.Id == id); }
    }
}
