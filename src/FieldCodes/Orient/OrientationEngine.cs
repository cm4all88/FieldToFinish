using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FieldCodes.Easements;

namespace FieldCodes.Orient
{
    /// <summary>
    /// Decides which way each survey symbol should point, from what was surveyed around it.
    /// The order is fixed and a stronger tier always wins:
    ///   1. Measured     -- the crew recorded it (azimuth or cardinal, a sign panel line,
    ///                      a luminaire shot).
    ///   2. Relationship -- measured features around it say so (a stop bar, the intersection
    ///                      approach it stands on, a measured arrow, a valve tee and its hydrant).
    ///   3. Alignment    -- along surveyed road or utility linework.
    ///   4. Inferred     -- a feature-specific assumption the surroundings support.
    ///   5. Review       -- nothing reliable: left alone and listed.
    /// Only measured features feed other features' decisions, so one wrong inference cannot
    /// set off a chain of confident wrong ones. Nothing here invents survey geometry: inferred
    /// pipe connections are returned separately and are never drawn as utility linework.
    /// </summary>
    public sealed class OrientationEngine
    {
        private readonly OrientationStandard _std;

        public OrientationEngine(OrientationStandard standard)
        {
            _std = standard ?? OrientationStandard.Pmx();
        }

        public OrientationResult Run(OrientationScene scene)
        {
            var result = new OrientationResult();
            var roads = new RoadModel(scene.Lines, _std);
            result.Intersections = roads.Intersections;

            // Measured directions first: from the description/note when not already supplied.
            foreach (var f in scene.Features)
            {
                if (f.Kind == OrientKind.Other) continue;
                if (!f.MeasuredAzimuth.HasValue)
                {
                    var read = MeasuredDirection.Read(f.Description, f.Note, f.Kind);
                    if (read.Count > 0)
                    {
                        f.MeasuredAzimuth = read[0];
                        f.MeasuredFrom = "recorded in the description/note";
                        foreach (var extra in read.Skip(1)) f.ExtraMeasuredAzimuths.Add(extra);
                    }
                }
                // "ASTO S": the direction of travel, matched to the lane in Marking().
                if (!f.MeasuredAzimuth.HasValue && !f.StatedTravelAzimuth.HasValue && _std.MarkingDirectionWords &&
                    (f.Kind == OrientKind.PavementArrow || f.Kind == OrientKind.PavementWord))
                {
                    string words;
                    var stated = TravelWords.Read(f.Description, f.FullDescription, f.Note, out words);
                    if (stated.HasValue)
                    {
                        f.StatedTravelAzimuth = stated;
                        f.StatedTravelWords = words;
                    }
                }
            }

            var features = scene.Features.Where(f => f.Kind != OrientKind.Other).ToList();
            var measuredArrows = features.Where(f => f.Kind == OrientKind.PavementArrow && f.MeasuredAzimuth.HasValue).ToList();
            var hydrants = features.Where(f => f.Kind == OrientKind.Hydrant).ToList();
            var luminaires = scene.Features.Where(f => _std.IsLuminaire(f.Code)).ToList();
            var panels = scene.Lines.Where(l => l.Role == LineRole.SignPanel).ToList();

            foreach (var f in features)
            {
                var symbol = _std.SymbolFor(f.Code);
                OrientationDecision d;
                // Not required to be turned: only a recorded direction turns it (a sign's shot panel
                // is decided in Sign()).
                if (f.InferFacing == false && !f.MeasuredAzimuth.HasValue && f.Kind != OrientKind.Sign)
                {
                    result.Decisions.Add(NoSupport(Start(f, symbol)));
                    continue;
                }
                switch (f.Kind)
                {
                    case OrientKind.Sign: d = Sign(f, symbol, roads, panels, measuredArrows); break;
                    case OrientKind.StreetLight: d = Light(f, symbol, roads, luminaires); break;
                    case OrientKind.SignalPole: d = Signal(f, symbol, roads); break;
                    case OrientKind.Hydrant: d = Hydrant(f, symbol, roads); break;
                    case OrientKind.PavementArrow:
                    case OrientKind.PavementWord: d = Marking(f, symbol, roads, measuredArrows); break;
                    case OrientKind.WaterValve:
                    case OrientKind.GasValve: d = ValveAlone(f, symbol, roads, scene.Lines); break;
                    default: continue;
                }
                result.Decisions.Add(d);
            }

            SignsOnOnePost(result);
            new ValveClusters(_std, roads).Decide(result, hydrants);

            foreach (var d in result.Decisions) Finish(d);
            return result;
        }

        // ================================================================== signs

        private OrientationDecision Sign(OrientFeature f, SymbolRule symbol, RoadModel roads, IList<OrientLine> panels, IList<OrientFeature> arrows)
        {
            var d = Start(f, symbol);
            if (f.MeasuredAzimuth.HasValue) return Measured(d, f.MeasuredAzimuth.Value, "sign faces " + Deg(f.MeasuredAzimuth.Value) + ", " + f.MeasuredFrom);

            // A shot sign panel: its line is measured; the face is on the side away from the post.
            var panel = panels.Select(p => { P2 foot, t; double dist, s; p.Nearest(f.Location, out foot, out t, out dist, out s); return Tuple.Create(p, foot, t, dist); })
                              .Where(p => p.Item4 <= _std.PanelSnapFeet).OrderBy(p => p.Item4).FirstOrDefault();
            if (panel != null)
            {
                var n = panel.Item3.LeftNormal();
                var a1 = SymbolRotation.AzimuthOf(n);
                var a2 = SymbolRotation.Reverse(a1);
                if (panel.Item4 > 0.2)
                {
                    var face = SymbolRotation.AzimuthOf(panel.Item2 - f.Location);
                    return Measured(d, face, "sign panel " + panel.Item1.Id + " shot; its face is on the side away from the post");
                }
                var guess = InferSignFacing(f, roads, arrows, symbol);
                if (guess != null && guess.Azimuth.HasValue)
                {
                    var pick = SymbolRotation.Difference(a1, guess.Azimuth.Value) <= SymbolRotation.Difference(a2, guess.Azimuth.Value) ? a1 : a2;
                    d.Tier = OrientationTier.Measured;
                    d.Confidence = 0.8;
                    d.Azimuth = pick;
                    d.Evidence.Add(new Evidence(OrientationTier.Measured, 0.9, "sign panel " + panel.Item1.Id + " shot through the post: panel line measured"));
                    d.Evidence.Add(new Evidence(guess.Tier, guess.Confidence, "face side from " + guess.Association));
                    d.Association = guess.Association;
                    return d;
                }
                return Review(d, "panel line measured but the face side is not evident", a1, a2);
            }

            // A private, address or other descriptive sign: never assumed to face a road.
            if (f.InferFacing == false) return NoSupport(d);

            if (symbol != null && symbol.TwoSided)
                return Review(d, "two-sided sign: record which street it names or its facing (e.g. FACES N)", null, null);

            var inferred = InferSignFacing(f, roads, arrows, symbol);
            return inferred ?? Review(d, "no road within " + _std.RoadSearchFeet + " ft", null, null);
        }

        private OrientationDecision InferSignFacing(OrientFeature f, RoadModel roads, IList<OrientFeature> arrows, SymbolRule symbol)
        {
            var d = Start(f, symbol);
            var stop = _std.IsStopSign(f);

            // A stop bar the sign stands beside: it faces the traffic that stops there.
            if (stop)
            {
                var bar = roads.StopBars.Where(b => b.Travel.HasValue && b.Mid.DistanceTo(f.Location) <= _std.StopBarSearchFeet)
                               .Where(b => OnRightOfTravel(f.Location, b.Mid, b.Travel.Value) && Upstream(f.Location, b.Mid, b.Travel.Value) > -25)
                               .OrderBy(b => b.Mid.DistanceTo(f.Location)).FirstOrDefault();
                if (bar != null)
                {
                    d.Tier = OrientationTier.Relationship;
                    d.Confidence = 0.85;
                    d.Azimuth = SymbolRotation.Reverse(SymbolRotation.AzimuthOf(bar.Travel.Value));
                    d.Association = "stop bar " + bar.Line.Id;
                    d.AssociatedPoint = bar.Mid;
                    d.Evidence.Add(new Evidence(OrientationTier.Relationship, 0.85, "stop bar " + bar.Line.Id + " beside it; traffic crosses it heading " +
                                                Deg(SymbolRotation.AzimuthOf(bar.Travel.Value)) + " (" + bar.TravelFrom + ")"));
                    return d;
                }
            }

            // An intersection approach the sign stands on: on the right of the inbound traffic,
            // before the node. Not simply the nearest road: at a corner both roads are near.
            var legs = new List<Tuple<Approach, double, double, double>>();
            foreach (var node in roads.Intersections.Where(n => n.Center.DistanceTo(f.Location) <= _std.IntersectionSearchFeet))
                foreach (var leg in node.Approaches.Where(a => a.InboundAllowed))
                {
                    var rel = f.Location - node.Center;
                    var upstream = P2.Dot(rel, leg.Outward);          // how far back along the leg
                    var lateral = P2.Cross(leg.Inbound, rel);         // negative: on the right of inbound traffic
                    if (upstream < 5 || upstream > _std.IntersectionSearchFeet) continue;
                    if (lateral > -4 || lateral < -60) continue;
                    // Roadside signs stand about a lane and a shoulder off the centre line, a
                    // little before the intersection: far from that, the sign belongs elsewhere.
                    var lateralFit = Math.Exp(-Math.Pow((-lateral - 18) / 12.0, 2));
                    var upstreamFit = 1.0 / (1.0 + Math.Max(0, upstream - 30) / 60.0);
                    var score = lateralFit * upstreamFit;
                    if (score < 0.05) continue;
                    legs.Add(Tuple.Create(leg, score, upstream, lateral));
                }
            if (legs.Count > 0)
            {
                var ranked = legs.OrderByDescending(l => l.Item2).ToList();
                var best = ranked[0];
                var close = ranked.Count > 1 && ranked[1].Item2 > best.Item2 * 0.85 &&
                            SymbolRotation.Difference(SymbolRotation.AzimuthOf(ranked[1].Item1.Inbound), SymbolRotation.AzimuthOf(best.Item1.Inbound)) > 20;
                var facing = SymbolRotation.Reverse(SymbolRotation.AzimuthOf(best.Item1.Inbound));
                if (close)
                    return Review(d, "at the corner of two approaches (" + best.Item1.Name + ", " + ranked[1].Item1.Name + "); record its facing",
                                  facing, SymbolRotation.Reverse(SymbolRotation.AzimuthOf(ranked[1].Item1.Inbound)));
                d.Tier = OrientationTier.Relationship;
                d.Confidence = stop ? 0.8 : 0.65;
                d.Azimuth = facing;
                d.Association = best.Item1.Name;
                d.AssociatedPoint = best.Item1.Node.Center;
                d.Evidence.Add(new Evidence(OrientationTier.Relationship, d.Confidence, string.Format(CultureInfo.InvariantCulture,
                    "on the right of traffic arriving on the {0}, {1:0} ft before the intersection, {2:0} ft off its centre line",
                    best.Item1.Name, Math.Max(0, best.Item3), -best.Item4)));
                return d;
            }

            // A stop sign at an intersection that stands on no approach's right is not guessed
            // from the nearest road: that could face it at traffic leaving the intersection.
            if (stop && roads.Intersections.Any(n => n.Center.DistanceTo(f.Location) <= 60))
                return Review(d, "stop sign at an intersection but on the right of no approach; record its facing", null, null);

            // A stop sign beside a side road drawn by its edges only (no centre line to make an
            // intersection of): it stops the traffic coming up that road to the one it meets.
            if (stop)
            {
                var side = roads.SideRoadAt(f.Location);
                if (side != null)
                {
                    var facing = SymbolRotation.Reverse(SymbolRotation.AzimuthOf(side.TowardMeets));
                    var leaving = SymbolRotation.AzimuthOf(side.TowardMeets);
                    if (P2.Cross(side.TowardMeets, f.Location - side.Foot) >= 0)
                        return Review(d, "stop sign beside side road " + side.Edge.Id + " but on the left of the traffic coming up to " +
                                         side.Meets.Id + "; record its facing", facing, leaving);
                    d.Tier = OrientationTier.Relationship;
                    d.Confidence = 0.7;
                    d.Azimuth = facing;
                    d.Association = "side road " + side.Edge.Id + " at " + side.Meets.Id;
                    d.AssociatedPoint = side.Foot;
                    d.Evidence.Add(new Evidence(OrientationTier.Relationship, 0.7, string.Format(CultureInfo.InvariantCulture,
                        "on the right of traffic coming up side road {0} (edges only, no centre line) to {1}, {2:0} ft away: it stops that traffic",
                        side.Edge.Id, side.Meets.Id, side.MeetsDistance)));
                    return d;
                }
            }

            // A measured arrow in the lane beside it says which way that traffic goes, even where
            // the road is one-way and nobody drew it so.
            var arrow = arrows.Where(a => a.Location.DistanceTo(f.Location) <= 60).OrderBy(a => a.Location.DistanceTo(f.Location)).FirstOrDefault();
            if (arrow != null)
            {
                var t = SymbolRotation.DirectionOf(arrow.MeasuredAzimuth.Value);
                var lateral = Math.Abs(P2.Cross(t, f.Location - arrow.Location));
                if (lateral <= 25)
                {
                    d.Tier = OrientationTier.Relationship;
                    d.Confidence = 0.7;
                    d.Azimuth = SymbolRotation.Reverse(arrow.MeasuredAzimuth.Value);
                    d.Association = "measured arrow " + arrow.Id;
                    d.AssociatedPoint = arrow.Location;
                    d.Evidence.Add(new Evidence(OrientationTier.Relationship, 0.7, "measured arrow " + arrow.Id + " in the lane beside it points " + Deg(arrow.MeasuredAzimuth.Value)));
                    return d;
                }
            }

            // Along the road: two-way traffic keeps right, the sign faces the traffic beside it.
            var travel = roads.TravelAtPoint(f.Location);
            if (travel == null) return null;
            var near2 = SecondRoad(roads, f.Location, travel);
            var face2 = SymbolRotation.Reverse(SymbolRotation.AzimuthOf(travel.Direction));

            // A stop sign with no stop bar, approach or side road found is not faced at the through
            // traffic beside it: through traffic is seldom stopped, and the road it controls was
            // likely not surveyed (or is a driveway). Suggested, not applied.
            if (stop)
                return Review(d, "stop sign beside " + travel.Line.Id + " with no stop bar, intersection approach or side road found; " +
                                 "record which approach it controls (e.g. FACES N)", face2, null);
            if (near2 != null)
            {
                var alt = SymbolRotation.Reverse(SymbolRotation.AzimuthOf(near2.Direction));
                if (SymbolRotation.Difference(alt, face2) > 30)
                    return Review(d, "near two roads (" + travel.Line.Id + ", " + near2.Line.Id + "); it may serve either", face2, alt);
            }
            d.Tier = travel.Tier;
            d.Confidence = travel.Confidence;
            d.Azimuth = face2;
            d.Association = travel.Line.Id;
            d.Evidence.Add(new Evidence(travel.Tier, travel.Confidence, travel.Why + "; the sign faces the traffic beside it"));
            return d;
        }

        /// <summary>Several signs on one post: a measured one stands on its own; any not
        /// measured is listed for review, because they often face different approaches.</summary>
        private void SignsOnOnePost(OrientationResult result)
        {
            var signs = result.Decisions.Where(d => d.Feature.Kind == OrientKind.Sign).ToList();
            var groups = new List<List<OrientationDecision>>();
            foreach (var s in signs)
            {
                var g = groups.FirstOrDefault(x => x.Any(o => o.Feature.Location.DistanceTo(s.Feature.Location) <= _std.SamePostFeet));
                if (g != null) g.Add(s); else groups.Add(new List<OrientationDecision> { s });
            }
            foreach (var g in groups.Where(x => x.Count > 1))
                foreach (var s in g.Where(x => x.Tier != OrientationTier.Measured && x.Tier < OrientationTier.Review))
                {
                    s.Evidence.Add(new Evidence(OrientationTier.Review, 0, g.Count + " signs on one post"));
                    Review(s, g.Count + " signs on one post and this one's facing was not recorded (e.g. FACES N)", s.Azimuth, null);
                }
        }

        // ================================================================== lights and signals

        private OrientationDecision Light(OrientFeature f, SymbolRule symbol, RoadModel roads, IList<OrientFeature> luminaires)
        {
            var d = Start(f, symbol);
            if (symbol != null && !symbol.Directional) return NotDirectional(d);
            if (f.MeasuredAzimuth.HasValue)
            {
                Measured(d, f.MeasuredAzimuth.Value, "arm points " + Deg(f.MeasuredAzimuth.Value) + ", " + f.MeasuredFrom);
                MoreArms(d, f);
                return d;
            }
            var lum = luminaires.Where(l => l.Id != f.Id && l.Location.DistanceTo(f.Location) <= _std.LuminaireSearchFeet && l.Location.DistanceTo(f.Location) > 1)
                                .OrderBy(l => l.Location.DistanceTo(f.Location)).ToList();
            if (lum.Count > 0)
            {
                Measured(d, SymbolRotation.AzimuthOf(lum[0].Location - f.Location), "luminaire " + lum[0].Id + " shot at the end of the arm");
                foreach (var extra in lum.Skip(1).Where(l => l.Location.DistanceTo(f.Location) <= _std.LuminaireSearchFeet / 2))
                    f.ExtraMeasuredAzimuths.Add(SymbolRotation.AzimuthOf(extra.Location - f.Location));
                MoreArms(d, f);
                return d;
            }
            return TowardRoad(d, f, roads, OrientationTier.Alignment, 0.65, "a street light's arm reaches over the road");
        }

        private static void MoreArms(OrientationDecision d, OrientFeature f)
        {
            if (f.ExtraMeasuredAzimuths.Count == 0) return;
            d.Association = (f.ExtraMeasuredAzimuths.Count + 1) + " arms";
            d.Evidence.Add(new Evidence(OrientationTier.Measured, 0.9, "more arms recorded: " +
                string.Join(", ", f.ExtraMeasuredAzimuths.Select(Deg).ToArray()) + " (the symbol shows the first)"));
        }

        private OrientationDecision Signal(OrientFeature f, SymbolRule symbol, RoadModel roads)
        {
            var d = Start(f, symbol);
            if (symbol != null && !symbol.Directional) return NotDirectional(d);
            if (f.MeasuredAzimuth.HasValue)
            {
                Measured(d, f.MeasuredAzimuth.Value, "mast arm points " + Deg(f.MeasuredAzimuth.Value) + ", " + f.MeasuredFrom);
                MoreArms(d, f);
                return d;
            }

            // At an intersection the arm reaches across an approach: toward that leg's centre line.
            var node = roads.Intersections.Where(n => n.Center.DistanceTo(f.Location) <= _std.IntersectionSearchFeet)
                            .OrderBy(n => n.Center.DistanceTo(f.Location)).FirstOrDefault();
            if (node != null)
            {
                var spans = new List<Tuple<Approach, P2, double>>();
                foreach (var leg in node.Approaches.Where(a => a.InboundAllowed))
                {
                    P2 foot, t; double dist, s;
                    if (!leg.Axis.Nearest(f.Location, out foot, out t, out dist, out s)) continue;
                    var upstream = P2.Dot(f.Location - node.Center, leg.Outward);
                    if (upstream < -20 || upstream > 80 || dist < 8 || dist > 80) continue;
                    spans.Add(Tuple.Create(leg, foot, dist));
                }
                var ranked = spans.OrderBy(s => s.Item3).ToList();
                if (ranked.Count == 1 || (ranked.Count > 1 && ranked[1].Item3 > ranked[0].Item3 * 1.4))
                {
                    d.Tier = OrientationTier.Relationship;
                    d.Confidence = 0.6;
                    d.Azimuth = SymbolRotation.AzimuthOf(ranked[0].Item2 - f.Location);
                    d.Association = ranked[0].Item1.Name;
                    d.AssociatedPoint = ranked[0].Item2;
                    d.Evidence.Add(new Evidence(OrientationTier.Relationship, 0.6, "signal pole at the " + ranked[0].Item1.Name + "; the arm reaches across that approach"));
                    return d;
                }
                if (ranked.Count > 1)
                    return Review(d, "signal pole serves one of several approaches (" + string.Join(", ", ranked.Select(r => r.Item1.Name).ToArray()) + "); record the arm direction",
                                  SymbolRotation.AzimuthOf(ranked[0].Item2 - f.Location), SymbolRotation.AzimuthOf(ranked[1].Item2 - f.Location));
            }
            return TowardRoad(d, f, roads, OrientationTier.Alignment, 0.55, "a signal arm reaches over the road");
        }

        // ================================================================== hydrants

        private OrientationDecision Hydrant(OrientFeature f, SymbolRule symbol, RoadModel roads)
        {
            var d = Start(f, symbol);
            if (symbol != null && !symbol.Directional) return NotDirectional(d);
            if (f.MeasuredAzimuth.HasValue) return Measured(d, f.MeasuredAzimuth.Value, "pumper nozzle faces " + Deg(f.MeasuredAzimuth.Value) + ", " + f.MeasuredFrom);
            return TowardRoad(d, f, roads, OrientationTier.Inferred, 0, "the pumper nozzle faces the street (fire engine access)", 40);
        }

        /// <summary>Points the symbol at the nearest road (edge first, then centre line), and
        /// sends a corner case with two roads in different directions to review.</summary>
        private OrientationDecision TowardRoad(OrientationDecision d, OrientFeature f, RoadModel roads, OrientationTier tier, double confidence, string why, double within = 0)
        {
            var reach = within > 0 ? within : _std.RoadSearchFeet;
            var candidates = new List<Tuple<OrientLine, P2, double>>();
            foreach (var l in roads.Edges.Concat(roads.Axes))
            {
                P2 foot, t; double dist, s;
                if (l.Nearest(f.Location, out foot, out t, out dist, out s) && dist <= reach && dist > 0.3)
                    candidates.Add(Tuple.Create(l, foot, dist));
            }
            if (candidates.Count == 0) return Review(d, "no road within " + reach + " ft", null, null);
            var ranked = candidates.OrderBy(c => c.Item3).ToList();
            var best = ranked[0];
            var az = SymbolRotation.AzimuthOf(best.Item2 - f.Location);
            var rival = ranked.Skip(1).FirstOrDefault(c => c.Item3 < best.Item3 * 1.3 &&
                                                         SymbolRotation.Difference(SymbolRotation.AzimuthOf(c.Item2 - f.Location), az) > 45);
            if (rival != null)
                return Review(d, "two roads at a similar distance (" + best.Item1.Id + ", " + rival.Item1.Id + ")", az,
                              SymbolRotation.AzimuthOf(rival.Item2 - f.Location));
            d.Tier = tier;
            d.Confidence = confidence > 0 ? confidence : (best.Item3 <= 15 ? 0.7 : 0.6);
            d.Azimuth = az;
            d.Association = best.Item1.Id;
            d.AssociatedPoint = best.Item2;
            d.Evidence.Add(new Evidence(tier, d.Confidence, string.Format(CultureInfo.InvariantCulture, "{0}: toward {1}, {2:0.0} ft away", why, best.Item1.Id, best.Item3)));
            return d;
        }

        // ================================================================== pavement markings

        private OrientationDecision Marking(OrientFeature f, SymbolRule symbol, RoadModel roads, IList<OrientFeature> arrows)
        {
            var d = Start(f, symbol);
            var what = f.Kind == OrientKind.PavementWord ? "reads to traffic heading" : "points";
            if (f.MeasuredAzimuth.HasValue) return Measured(d, f.MeasuredAzimuth.Value, "marking " + what + " " + Deg(f.MeasuredAzimuth.Value) + ", " + f.MeasuredFrom);
            if (f.StatedTravelAzimuth.HasValue) return StatedTravel(f, symbol, roads, arrows);
            return InferMarking(f, symbol, roads, arrows);
        }

        /// <summary>
        /// A marking whose direction of travel the crew wrote ("ASTO S"). The written compass
        /// direction settles which way along the road; the road settles the exact angle. Of the
        /// road lines within reach, the one whose direction (at the point nearest the arrow, so a
        /// curve is followed) is closest to the written direction is used -- at an intersection
        /// that is the street the arrow is on, not the one crossing it. With no road near, the
        /// arrow points the written direction on the drawing's grid. A road that would turn the
        /// written direction by more than the setting allows is put up for review.
        /// </summary>
        private OrientationDecision StatedTravel(OrientFeature f, SymbolRule symbol, RoadModel roads, IList<OrientFeature> arrows)
        {
            var d = Start(f, symbol);
            var stated = f.StatedTravelAzimuth.Value;
            var written = "\"" + f.StatedTravelWords + "\" (" + TravelWords.Describe(stated) + ")";

            if (_std.MarkingFollowRoad)
            {
                var candidates = roads.LinesNear(f.Location, _std.MarkingRoadFeet)
                    .Select(x =>
                    {
                        var az = SymbolRotation.AzimuthOf(x.Item2);
                        var along = SymbolRotation.Difference(az, stated) <= 90 ? az : SymbolRotation.Reverse(az);
                        return new { Line = x.Item1, Azimuth = along, Turn = SymbolRotation.Difference(along, stated), Distance = x.Item3 };
                    })
                    .ToList();
                if (candidates.Count > 0)
                {
                    // Lines running the same way (lane lines, centre line, edges of one street) are
                    // all equally right; of those the nearest gives the angle at this spot.
                    var bestTurn = candidates.Min(c => c.Turn);
                    var best = candidates.Where(c => c.Turn <= bestTurn + 10).OrderBy(c => c.Distance).First();
                    if (best.Turn > _std.MarkingMaxTurnDegrees)
                        return Review(d, string.Format(CultureInfo.InvariantCulture,
                            "the crew wrote {0}, but the road here runs {1} / {2} ({3:0} degrees off); check the description",
                            written, Deg(best.Azimuth), Deg(SymbolRotation.Reverse(best.Azimuth)), best.Turn), stated, best.Azimuth);
                    d.Tier = OrientationTier.Measured;
                    d.Confidence = 0.9;
                    d.Azimuth = best.Azimuth;
                    d.Association = best.Line.Id;
                    d.Evidence.Add(new Evidence(OrientationTier.Measured, 0.9, string.Format(CultureInfo.InvariantCulture,
                        "the crew wrote {0}; along {1} ({2:0.0} ft away) that is {3}", written, best.Line.Id, best.Distance, Deg(best.Azimuth))));
                    NoteDisagreement(d, f, symbol, roads, arrows);
                    return d;
                }
            }

            d.Tier = OrientationTier.Measured;
            d.Confidence = 0.85;
            d.Azimuth = stated;
            d.Evidence.Add(new Evidence(OrientationTier.Measured, 0.85, _std.MarkingFollowRoad
                ? string.Format(CultureInfo.InvariantCulture, "the crew wrote {0}; no road line within {1:0} ft, so it points {2} on the drawing's grid",
                                written, _std.MarkingRoadFeet, Deg(stated))
                : "the crew wrote " + written + "; it points " + Deg(stated) + " as written (matching to the road is off)"));
            NoteDisagreement(d, f, symbol, roads, arrows);
            return d;
        }

        /// <summary>Says so when the lane reasoning (stop bar, approach, keep right) points the
        /// other way: the crew's direction is used, and the report shows the conflict.</summary>
        private void NoteDisagreement(OrientationDecision d, OrientFeature f, SymbolRule symbol, RoadModel roads, IList<OrientFeature> arrows)
        {
            var inferred = InferMarking(f, symbol, roads, arrows);
            if (!inferred.Azimuth.HasValue || inferred.Tier == OrientationTier.Review || !d.Azimuth.HasValue) return;
            if (SymbolRotation.Difference(inferred.Azimuth.Value, d.Azimuth.Value) <= 90) return;
            var why = inferred.Evidence.Count > 0 ? inferred.Evidence[0].Text : "the lane";
            d.Evidence.Add(new Evidence(OrientationTier.Inferred, 0, "note: " + why + " suggests " + Deg(inferred.Azimuth.Value) +
                                                                     "; the crew's direction was used"));
        }

        /// <summary>A marking with no direction recorded: the stop bar it approaches, the
        /// intersection leg it is on, else the lane it is in (traffic keeps right).</summary>
        private OrientationDecision InferMarking(OrientFeature f, SymbolRule symbol, RoadModel roads, IList<OrientFeature> arrows)
        {
            var d = Start(f, symbol);

            // Approaching a stop bar or an intersection: inbound traffic.
            var bar = roads.StopBars.Where(b => b.Travel.HasValue && b.Mid.DistanceTo(f.Location) <= 60 && Upstream(f.Location, b.Mid, b.Travel.Value) > -5)
                           .Where(b => Math.Abs(P2.Cross(b.Travel.Value, f.Location - b.Mid)) <= b.Line.Length / 2 + 6)
                           .OrderBy(b => b.Mid.DistanceTo(f.Location)).FirstOrDefault();
            if (bar != null)
            {
                d.Tier = OrientationTier.Relationship;
                d.Confidence = 0.8;
                d.Azimuth = SymbolRotation.AzimuthOf(bar.Travel.Value);
                d.Association = "stop bar " + bar.Line.Id;
                d.AssociatedPoint = bar.Mid;
                d.Evidence.Add(new Evidence(OrientationTier.Relationship, 0.8, "in the lane approaching stop bar " + bar.Line.Id + " (" + bar.TravelFrom + ")"));
                return d;
            }
            foreach (var node in roads.Intersections.Where(n => n.Center.DistanceTo(f.Location) <= 200).OrderBy(n => n.Center.DistanceTo(f.Location)))
            {
                var leg = node.Approaches.Where(a => a.InboundAllowed)
                              .Select(a => Tuple.Create(a, P2.Dot(f.Location - node.Center, a.Outward), P2.Cross(a.Inbound, f.Location - node.Center)))
                              .Where(a => a.Item2 > 10 && a.Item2 < 200 && a.Item3 < 0.5 && a.Item3 > -30)
                              .OrderBy(a => Math.Abs(a.Item3)).FirstOrDefault();
                if (leg == null) continue;
                d.Tier = OrientationTier.Relationship;
                d.Confidence = 0.7;
                d.Azimuth = SymbolRotation.AzimuthOf(leg.Item1.Inbound);
                d.Association = leg.Item1.Name;
                d.AssociatedPoint = node.Center;
                d.Evidence.Add(new Evidence(OrientationTier.Relationship, 0.7, string.Format(CultureInfo.InvariantCulture,
                    "in an inbound lane of the {0}, {1:0} ft before the intersection", leg.Item1.Name, leg.Item2)));
                return d;
            }

            var travel = roads.TravelAtPoint(f.Location, true);
            if (travel == null) return Review(d, "no road linework around it", null, null);
            d.Tier = travel.Tier;
            d.Confidence = travel.Confidence;
            d.Azimuth = SymbolRotation.AzimuthOf(travel.Direction);
            d.Association = travel.Line.Id;
            d.Evidence.Add(new Evidence(travel.Tier, travel.Confidence, travel.Why));
            return d;
        }

        // ================================================================== valves (alone)

        private OrientationDecision ValveAlone(OrientFeature f, SymbolRule symbol, RoadModel roads, IList<OrientLine> lines)
        {
            var d = Start(f, symbol);
            if (f.MeasuredAzimuth.HasValue) return Measured(d, f.MeasuredAzimuth.Value, "pipe axis " + Deg(f.MeasuredAzimuth.Value) + ", " + f.MeasuredFrom);
            var role = f.Kind == OrientKind.WaterValve ? LineRole.WaterMain : LineRole.GasMain;
            var main = lines.Where(l => l.Role == role)
                            .Select(l => { P2 foot, t; double dist, s; l.Nearest(f.Location, out foot, out t, out dist, out s); return Tuple.Create(l, t, dist); })
                            .Where(x => x.Item3 <= _std.MainSnapFeet).OrderBy(x => x.Item3).FirstOrDefault();
            if (main != null)
            {
                d.Tier = OrientationTier.Alignment;
                d.Confidence = 0.85;
                d.Azimuth = SymbolRotation.AzimuthOf(main.Item2);
                d.Association = main.Item1.Id;
                d.Evidence.Add(new Evidence(OrientationTier.Alignment, 0.85, string.Format(CultureInfo.InvariantCulture,
                    "on surveyed/marked {0} {1}, {2:0.0} ft away", f.Kind == OrientKind.WaterValve ? "water line" : "gas line", main.Item1.Id, main.Item3)));
                return d;
            }
            // Left for the cluster pass; a valve nothing explains stays in review.
            return Review(d, "no measured direction and no water/gas line within " + _std.MainSnapFeet + " ft", null, null);
        }

        // ================================================================== shared

        internal static OrientationDecision Start(OrientFeature f, SymbolRule symbol)
        {
            return new OrientationDecision { Feature = f, Tier = OrientationTier.Review, Confidence = 0, Symbol = symbol };
        }

        internal static OrientationDecision Measured(OrientationDecision d, double az, string why)
        {
            d.Tier = OrientationTier.Measured;
            d.Confidence = 0.95;
            d.Azimuth = az;
            d.ReviewReason = null;
            d.Evidence.Add(new Evidence(OrientationTier.Measured, 0.95, why));
            return d;
        }

        internal static OrientationDecision Review(OrientationDecision d, string why, double? suggestion, double? alternative)
        {
            d.Tier = OrientationTier.Review;
            d.Confidence = 0;
            d.ReviewReason = why;
            d.Azimuth = suggestion;
            d.AlternativeAzimuth = alternative;
            return d;
        }

        /// <summary>Left as surveyed: its standard turns it only by a recorded direction, and nothing recorded
        /// its direction.</summary>
        internal static OrientationDecision NoSupport(OrientationDecision d)
        {
            d.Tier = OrientationTier.NoSupport;
            d.Confidence = 0;
            d.Evidence.Add(new Evidence(OrientationTier.NoSupport, 0,
                "its drafting standard turns it only by a recorded direction, and none is recorded (a measured azimuth, FACES N, a shot panel): left as surveyed, not assumed to face a road"));
            return d;
        }

        private static OrientationDecision NotDirectional(OrientationDecision d)
        {
            d.Tier = OrientationTier.NotDirectional;
            d.Confidence = 1;
            d.Evidence.Add(new Evidence(OrientationTier.NotDirectional, 1, "symbol looks the same at every rotation; left as drawn"));
            return d;
        }

        /// <summary>Rotation for the symbol: only for decisions that are not review, and never
        /// for a symbol that looks the same at every rotation.</summary>
        internal static void Finish(OrientationDecision d)
        {
            if (d.Symbol != null && !d.Symbol.Directional)
            {
                d.MarkerRotation = null;
                if (d.Tier != OrientationTier.Review && d.Tier != OrientationTier.NotDirectional)
                    d.Evidence.Add(new Evidence(OrientationTier.NotDirectional, 1, "symbol is symmetric: direction kept for the report, marker not rotated"));
                return;
            }
            d.MarkerRotation = d.Tier < OrientationTier.Review && d.Azimuth.HasValue && d.Symbol != null
                ? SymbolRotation.MarkerRadians(d.Symbol, d.Azimuth.Value)
                : null;
        }

        private static bool OnRightOfTravel(P2 p, P2 onPath, P2 travel)
        {
            return P2.Cross(travel, p - onPath) < 0;
        }

        /// <summary>How far before a point on the path (positive: approaching it).</summary>
        private static double Upstream(P2 p, P2 onPath, P2 travel)
        {
            return P2.Dot(onPath - p, travel);
        }

        private TravelAt SecondRoad(RoadModel roads, P2 p, TravelAt first)
        {
            TravelAt best = null;
            foreach (var axis in roads.Axes.Where(a => a != first.Line))
            {
                P2 foot, t; double dist, s;
                if (!axis.Nearest(p, out foot, out t, out dist, out s) || dist > first.Distance * 1.4 || dist > _std.RoadSearchFeet) continue;
                if (s < 1 || s > axis.Length - 1) continue;   // only roads the point is beside, not beyond their end
                var side = P2.Cross(t, p - foot);
                var dir = side < 0 ? t : t * -1;
                if (best == null || dist < best.Distance)
                    best = new TravelAt { Direction = dir, Line = axis, Distance = dist, Tier = OrientationTier.Alignment, Confidence = 0.6 };
            }
            return best;
        }

        internal static string Deg(double az) { return az.ToString("0", CultureInfo.InvariantCulture) + " deg"; }
    }
}
