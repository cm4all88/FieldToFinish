using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Easements;

namespace FieldCodes.Orient
{
    /// <summary>
    /// Valves read as groups. A common water arrangement near a hydrant is a tee: two valves
    /// on the main, in line with each other, and a third on the hydrant lateral, off the main
    /// on the way to the hydrant. Recognised from surface positions only, so:
    ///   * a cluster is linked to a hydrant only when the geometry fits (the lateral valve lies
    ///     off the main line, toward the hydrant, between the main and the hydrant);
    ///   * a cluster with no hydrant, or one whose valves do not form a tee, is not forced into
    ///     one -- road-parallel valves get a main direction, anything else goes to review;
    ///   * the probable main and lateral are returned as InferredConnections, never as utility
    ///     linework, and never override a surveyed or marked line.
    /// A valve already oriented by measurement or by a surveyed line keeps that orientation.
    /// </summary>
    internal sealed class ValveClusters
    {
        private readonly OrientationStandard _std;
        private readonly RoadModel _roads;

        public ValveClusters(OrientationStandard std, RoadModel roads)
        {
            _std = std;
            _roads = roads;
        }

        public void Decide(OrientationResult result, IList<OrientFeature> hydrants)
        {
            foreach (var kind in new[] { OrientKind.WaterValve, OrientKind.GasValve })
            {
                // A valve whose standard does not require it turned is never inferred from its neighbours.
                var valves = result.Decisions.Where(d => d.Feature.Kind == kind && d.Feature.InferFacing != false).ToList();
                foreach (var cluster in Clusters(valves))
                {
                    var open = cluster.Where(d => d.Tier >= OrientationTier.Review).ToList();
                    if (open.Count == 0) continue;     // every valve already measured or on a line
                    if (kind == OrientKind.WaterValve && TryHydrant(cluster, hydrants, result)) continue;
                    RoadParallel(cluster);
                }
            }
        }

        private List<List<OrientationDecision>> Clusters(IList<OrientationDecision> valves)
        {
            var groups = new List<List<OrientationDecision>>();
            foreach (var v in valves)
            {
                var joins = groups.Where(g => g.Any(o => o.Feature.Location.DistanceTo(v.Feature.Location) <= _std.ValveClusterFeet)).ToList();
                var merged = new List<OrientationDecision> { v };
                foreach (var g in joins) { merged.AddRange(g); groups.Remove(g); }
                groups.Add(merged);
            }
            return groups;
        }

        // ------------------------------------------------------------------ hydrant tee

        private bool TryHydrant(List<OrientationDecision> cluster, IList<OrientFeature> hydrants, OrientationResult result)
        {
            var centre = new P2(cluster.Average(c => c.Feature.Location.X), cluster.Average(c => c.Feature.Location.Y));
            var near = hydrants.Where(h => h.Location.DistanceTo(centre) <= _std.HydrantSearchFeet)
                               .OrderBy(h => h.Location.DistanceTo(centre)).ToList();
            if (near.Count == 0) return false;
            if (near.Count > 1 && near[1].Location.DistanceTo(centre) < near[0].Location.DistanceTo(centre) * 1.2)
            {
                foreach (var v in cluster.Where(c => c.Tier >= OrientationTier.Review))
                    OrientationEngine.Review(v, "valve cluster between two hydrants (" + near[0].Id + ", " + near[1].Id + "); which it serves is not evident", null, null);
                return true;
            }
            var h0 = near[0];
            var road = RoadDirection(centre);

            if (cluster.Count >= 3) return Tee(cluster, h0, road, result);
            if (cluster.Count == 2) return Pair(cluster, h0, road, result);
            return Single(cluster[0], h0, road, result);
        }

        private bool Tee(List<OrientationDecision> cluster, OrientFeature hydrant, P2? road, OrientationResult result)
        {
            var options = new List<Tuple<double, OrientationDecision, List<OrientationDecision>, P2, P2, string>>();
            for (var li = 0; li < cluster.Count; li++)
            {
                var lateral = cluster[li];
                var mains = cluster.Where((c, i) => i != li).ToList();
                for (var a = 0; a < mains.Count; a++)
                    for (var b = a + 1; b < mains.Count; b++)
                    {
                        var m1 = mains[a].Feature.Location;
                        var m2 = mains[b].Feature.Location;
                        var span = m2 - m1;
                        if (span.Length < 1.0) continue;
                        var m = span.Normalized();
                        // Every other main must be on the line too.
                        if (mains.Any(o => Math.Abs(P2.Cross(m, o.Feature.Location - m1)) > 2.0)) continue;
                        var l = lateral.Feature.Location;
                        var t = m1 + m * P2.Dot(l - m1, m);
                        var offset = l.DistanceTo(t);
                        if (offset < 1.0) continue;
                        var along = P2.Dot(t - m1, m) / span.Length;
                        if (along < -0.3 || along > 1.3) continue;
                        var lat = (l - t).Normalized();
                        var toHydrant = hydrant.Location - t;
                        if (toHydrant.Length <= offset) continue;          // the valve is not between the main and the hydrant
                        var angLat = Angle(lat, toHydrant.Normalized());
                        if (angLat > 35) continue;
                        var perp = Math.Abs(90 - Angle(m, lat));
                        if (perp > 35) continue;
                        var roadFit = road.HasValue ? 1.0 - Math.Min(1.0, AxisAngle(m, road.Value) / 25.0) : 0.5;
                        var score = 0.4 * (1 - angLat / 35) + 0.3 * (1 - perp / 35) + 0.3 * roadFit;
                        var why = string.Format(CultureInfo.InvariantCulture,
                            "tee: {0} and {1} in line (main{2}); {3} {4:0.0} ft off the main toward hydrant {5} (off by {6:0} deg)",
                            mains[a].Feature.Id, mains[b].Feature.Id, road.HasValue ? string.Format(CultureInfo.InvariantCulture, ", {0:0} deg from the road", AxisAngle(m, road.Value)) : "",
                            lateral.Feature.Id, offset, hydrant.Id, angLat);
                        options.Add(Tuple.Create(score, lateral, mains, t, m, why));
                    }
            }
            if (options.Count == 0)
            {
                foreach (var v in cluster.Where(c => c.Tier >= OrientationTier.Review))
                    OrientationEngine.Review(v, "valves near hydrant " + hydrant.Id + " do not form a recognisable tee", null, null);
                return true;
            }
            var ranked = options.OrderByDescending(o => o.Item1).ToList();
            var best = ranked[0];
            if (best.Item1 < 0.45 || (ranked.Count > 1 && ranked[1].Item2 != best.Item2 && ranked[1].Item1 > best.Item1 - 0.12))
            {
                foreach (var v in cluster.Where(c => c.Tier >= OrientationTier.Review))
                    OrientationEngine.Review(v, "valve cluster at hydrant " + hydrant.Id + ": more than one valve could be the lateral", null, null);
                return true;
            }

            var conf = Math.Round(0.6 + 0.25 * best.Item1, 2);
            var mainAz = SymbolRotation.AzimuthOf(best.Item5);
            var latAz = SymbolRotation.AzimuthOf((best.Item2.Feature.Location - best.Item4).Normalized());
            foreach (var mv in best.Item3)
                Assign(mv, mainAz, conf, "on the main of a hydrant tee: " + best.Item6, "water main (tee at hydrant " + hydrant.Id + ")", best.Item4);
            Assign(best.Item2, latAz, conf, "hydrant lateral valve: " + best.Item6, "hydrant lateral to " + hydrant.Id, hydrant.Location);

            var ends = best.Item3.Select(v => v.Feature.Location).Concat(new[] { best.Item4 }).ToList();
            var s = ends.Min(p => P2.Dot(p - best.Item4, best.Item5));
            var e = ends.Max(p => P2.Dot(p - best.Item4, best.Item5));
            result.Connections.Add(new InferredConnection
            {
                Kind = "water main (inferred)", From = best.Item4 + best.Item5 * (s - 3), To = best.Item4 + best.Item5 * (e + 3), Confidence = conf,
                FeatureIds = best.Item3.Select(v => v.Feature.Id).ToList()
            });
            result.Connections.Add(new InferredConnection
            {
                Kind = "hydrant lateral (inferred)", From = best.Item4, To = hydrant.Location, Confidence = conf,
                FeatureIds = new List<string> { best.Item2.Feature.Id, hydrant.Id }
            });
            return true;
        }

        private bool Pair(List<OrientationDecision> cluster, OrientFeature hydrant, P2? road, OrientationResult result)
        {
            if (!road.HasValue)
            {
                foreach (var v in cluster.Where(c => c.Tier >= OrientationTier.Review))
                    OrientationEngine.Review(v, "two valves near hydrant " + hydrant.Id + " but no road to tell the main's direction", null, null);
                return true;
            }
            var a = cluster[0];
            var b = cluster[1];
            var pair = b.Feature.Location - a.Feature.Location;

            // Both on the main: in line with each other and with the road.
            if (AxisAngle(pair.Normalized(), road.Value) <= 15)
            {
                var az = SymbolRotation.AzimuthOf(pair.Normalized());
                foreach (var v in cluster)
                    Assign(v, az, 0.6, "two valves in line, along the road, near hydrant " + hydrant.Id + ": both on the main", "water main near hydrant " + hydrant.Id, null);
                return true;
            }

            // One on the main, one on the lateral: the lateral one lies between the main and the hydrant.
            foreach (var pick in new[] { Tuple.Create(a, b), Tuple.Create(b, a) })
            {
                var main = pick.Item1.Feature.Location;
                var lat = pick.Item2.Feature.Location;
                var t = main + road.Value * P2.Dot(lat - main, road.Value);
                var offset = lat.DistanceTo(t);
                var toHydrant = hydrant.Location - t;
                if (offset < 1.0 || toHydrant.Length <= offset) continue;
                if (Angle((lat - t).Normalized(), toHydrant.Normalized()) > 30) continue;
                Assign(pick.Item1, SymbolRotation.AzimuthOf(road.Value), 0.6, "valve on the main (along the road) beside hydrant " + hydrant.Id + "'s lateral", "water main near hydrant " + hydrant.Id, t);
                Assign(pick.Item2, SymbolRotation.AzimuthOf((lat - t).Normalized()), 0.6,
                       string.Format(CultureInfo.InvariantCulture, "valve {0:0.0} ft off the main toward hydrant {1}: probable lateral valve", offset, hydrant.Id),
                       "hydrant lateral to " + hydrant.Id, hydrant.Location);
                result.Connections.Add(new InferredConnection { Kind = "hydrant lateral (inferred)", From = t, To = hydrant.Location, Confidence = 0.6,
                                                                FeatureIds = new List<string> { pick.Item2.Feature.Id, hydrant.Id } });
                return true;
            }
            foreach (var v in cluster.Where(c => c.Tier >= OrientationTier.Review))
                OrientationEngine.Review(v, "two valves near hydrant " + hydrant.Id + " in no recognisable arrangement", null, null);
            return true;
        }

        private bool Single(OrientationDecision v, OrientFeature hydrant, P2? road, OrientationResult result)
        {
            if (v.Tier < OrientationTier.Review) return true;
            var dist = v.Feature.Location.DistanceTo(hydrant.Location);
            if (dist > 25 || !road.HasValue) return false;
            // Between the hydrant and the road, on the way out to the main: possibly its lateral valve.
            var toRoad = RoadFoot(hydrant.Location);
            if (toRoad.HasValue)
            {
                var dir = (toRoad.Value - hydrant.Location).Normalized();
                var rel = v.Feature.Location - hydrant.Location;
                if (P2.Dot(rel, dir) > 0 && Angle(rel.Normalized(), dir) <= 30)
                {
                    v.Tier = OrientationTier.Inferred;
                    v.Confidence = 0.5;
                    v.Azimuth = SymbolRotation.AzimuthOf(dir);
                    v.ReviewReason = null;
                    v.Association = "possible lateral of hydrant " + hydrant.Id;
                    v.AssociatedPoint = hydrant.Location;
                    v.Evidence.Add(new Evidence(OrientationTier.Inferred, 0.5, "lone valve between hydrant " + hydrant.Id + " and the road: possibly its lateral valve"));
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ no hydrant

        private void RoadParallel(List<OrientationDecision> cluster)
        {
            var open = cluster.Where(c => c.Tier >= OrientationTier.Review).ToList();
            if (cluster.Count < 2) return;      // a lone valve with nothing to go on stays in review
            var a = cluster[0].Feature.Location;
            var b = cluster.OrderByDescending(c => c.Feature.Location.DistanceTo(a)).First().Feature.Location;
            var dir = (b - a).Normalized();
            var inLine = cluster.All(c => Math.Abs(P2.Cross(dir, c.Feature.Location - a)) <= 2.0);
            var road = RoadDirection(new P2((a.X + b.X) / 2, (a.Y + b.Y) / 2));
            if (inLine && road.HasValue && AxisAngle(dir, road.Value) <= 20)
            {
                foreach (var v in open)
                    Assign(v, SymbolRotation.AzimuthOf(dir), 0.6, cluster.Count + " valves in line along the road: probably on one main", "main along the road", null, OrientationTier.Alignment);
                return;
            }
            foreach (var v in open)
                OrientationEngine.Review(v, cluster.Count + " valves together with no hydrant and no line through them; pipe direction not evident", null, null);
        }

        // ------------------------------------------------------------------ helpers

        private void Assign(OrientationDecision v, double az, double conf, string why, string association, P2? point, OrientationTier tier = OrientationTier.Relationship)
        {
            if (v.Tier < OrientationTier.Review)
            {
                // Measured or on a surveyed line already: keep it, note the relationship.
                v.Evidence.Add(new Evidence(tier, conf, "also: " + why));
                if (v.Association == null) v.Association = association;
                return;
            }
            v.Tier = tier;
            v.Confidence = conf;
            v.Azimuth = az;
            v.ReviewReason = null;
            v.Association = association;
            v.AssociatedPoint = point;
            v.Evidence.Add(new Evidence(tier, conf, why));
        }

        private P2? RoadDirection(P2 p)
        {
            P2 foot, t; double d;
            if (_roads.NearestAxis(p, 80, out foot, out t, out d) != null) return t;
            if (_roads.NearestEdge(p, 80, out foot, out t, out d) != null) return t;
            return null;
        }

        private P2? RoadFoot(P2 p)
        {
            P2 foot, t; double d;
            if (_roads.NearestEdge(p, 60, out foot, out t, out d) != null) return foot;
            if (_roads.NearestAxis(p, 80, out foot, out t, out d) != null) return foot;
            return null;
        }

        private static double Angle(P2 a, P2 b)
        {
            var c = Math.Max(-1, Math.Min(1, P2.Dot(a.Normalized(), b.Normalized())));
            return Math.Acos(c) * 180 / Math.PI;
        }

        /// <summary>Angle between two axes, ignoring direction: [0, 90].</summary>
        private static double AxisAngle(P2 a, P2 b)
        {
            var x = Angle(a, b);
            return Math.Min(x, 180 - x);
        }
    }
}
