using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using FieldCodes.Linework;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>One inventory row with what the Lines page needs to make it
    /// actionable: the candidate features, the status and the governing settings.</summary>
    public sealed class FtfLineItem
    {
        public LineworkRow Row { get; set; }
        public IList<FieldCodes.LineFeatureRule> Candidates { get; set; }
        public FieldCodes.Review.LineStatus Status { get; set; }
        public IList<FieldCodes.Review.LineSetting> Settings { get; set; }

        /// <summary>Where its label would go, for identified lines (also those of
        /// unknown length, which the row itself leaves blank).</summary>
        public string LabelLayerText { get; set; }
    }

    public sealed class FtfLineworkResult
    {
        public IList<LineworkRow> Rows { get; set; }
        public string RulesError { get; set; }

        /// <summary>The same lines as <see cref="Rows"/>, in the same order, with
        /// status and governing settings.</summary>
        public IList<FtfLineItem> Items { get; set; }

        public int Identified { get; set; }
        public int Ambiguous { get; set; }
        public int Unidentified { get; set; }

        /// <summary>FTF finishing curves found among the linework (driplines,
        /// leaders), by kind. Not survey linework; reported separately.</summary>
        public IDictionary<string, int> FtfOwnedByKind { get; set; }

        public FtfLineworkResult()
        {
            Rows = new List<LineworkRow>();
            Items = new List<FtfLineItem>();
            FtfOwnedByKind = new Dictionary<string, int>();
        }
    }

    /// <summary>
    /// The linework inventory: every existing line object in model space, identified
    /// against the configured line features. Strictly read-only -- entities are
    /// opened for read, nothing is created, modified or stamped, and the transaction
    /// commits having changed nothing. Civil 3D created this linework; FTF is only
    /// looking at it.
    ///
    /// Identification order per entity:
    ///   1. SurveyFigure.Name (RWC3 -> RWC) -- the survey's own identity, exact.
    ///   2. FeatureLine.Name, the same way.
    ///   3. TrimbleName XData left by the TBC export, matched exactly against a
    ///      configured code, name or label.
    ///   4. The entity's layer, for plain polylines/lines/arcs.
    ///
    /// UNTESTED against a drawing; the identification it delegates to is unit tested.
    /// </summary>
    internal static class FtfLineworkService
    {
        public static FtfLineworkResult Scan()
        {
            var result = new FtfLineworkResult();

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return result;

            var db = doc.Database;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                LineworkCatalog catalog;
                FieldCodes.Settings.LineLabelSettings ls;
                double unitsPerFoot;
                try
                {
                    var cfg = FtfSession.Rules(db);
                    var settings = FtfSession.SettingsFor(db, cfg);
                    catalog = new LineworkCatalog(cfg.LineFeatures, cfg.IgnoreLineNames);
                    ls = settings.LineLabels;
                    unitsPerFoot = settings.General.UnitsPerFoot;
                }
                catch (ConfigException ex)
                {
                    result.RulesError = ex.Message;
                    tr.Commit();
                    return result;
                }

                var layerCatalog = LayerNames(db, tr);
                var ms = CadUtil.ModelSpace(db, tr, OpenMode.ForRead);
                // The shots every line was drawn through: identity (point numbers) for the CSV, and
                // point notes for identification, as the line label passes use them.
                var sources = SourcePointReader.Read(db, tr);

                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;

                    var entity = tr.GetObject(id, OpenMode.ForRead, false, true) as AcEntity;
                    if (entity == null) continue;

                    // FTF's own output is finishing, not survey linework -- counted by
                    // kind so the inventory can say so, never listed as survey lines.
                    var stamp = Ownership.Read(entity);
                    if (stamp != null)
                    {
                        if (entity is Curve)
                        {
                            var kind = stamp.Kind.ToString();
                            int n;
                            result.FtfOwnedByKind.TryGetValue(kind, out n);
                            result.FtfOwnedByKind[kind] = n + 1;
                        }
                        continue;
                    }

                    string figureName = null;
                    string entityType = null;

                    var figure = entity as Autodesk.Civil.DatabaseServices.SurveyFigure;
                    if (figure != null)
                    {
                        figureName = figure.Name;
                        entityType = "Survey Figure";
                    }
                    else
                    {
                        var featureLine = entity as Autodesk.Civil.DatabaseServices.FeatureLine;
                        if (featureLine != null)
                        {
                            figureName = featureLine.Name;
                            entityType = "Feature Line";
                        }
                        else if (entity is Polyline || entity is Polyline2d ||
                                 entity is Polyline3d)
                        {
                            entityType = "Polyline";
                        }
                        else if (entity is Line)
                        {
                            entityType = "Line";
                        }
                        else if (entity is Arc)
                        {
                            entityType = "Arc";
                        }
                        else
                        {
                            continue;   // not linework
                        }
                    }

                    var trimbleName = TrimbleNameOf(entity);
                    var vertices = LineworkCommands.VerticesOf((Curve)entity).ToList();
                    var shots = new List<string>();
                    var notesHere = new List<string>();
                    foreach (var v in vertices)
                        foreach (var p in sources.At(v.X, v.Y))
                        {
                            if (shots.Count == 0 || shots[shots.Count - 1] != p.Number) shots.Add(p.Number);
                            if (p.HasDescription) notesHere.Add(p.RawDescription);
                        }
                    var notes = PointNotes.Consensus(notesHere, catalog);
                    var row = catalog.Identify(entityType, figureName, entity.Layer,
                                               LengthOf(entity), trimbleName, notes);
                    row.Handle = entity.Handle.ToString();
                    row.VertexCount = vertices.Count;
                    row.Shots = shots;

                    var candidates = row.Source == LineIdentitySource.PointNotes
                        ? notes.Features
                        : row.Source != LineIdentitySource.None
                            ? catalog.Candidates(figureName, trimbleName, entity.Layer)
                            : (IList<FieldCodes.LineFeatureRule>)new List<FieldCodes.LineFeatureRule>();

                    // Preview exactly what FTFLINELABELS would do: label text from the
                    // configured standard, count, spacing and side from the same maths
                    // and the same precedence the engine uses.
                    if (row.Source != LineIdentitySource.None && row.Length.HasValue)
                    {
                        var label = LineworkCatalog.UnanimousLabel(candidates);
                        var side = LineSideParser.Resolve(row.SourceSide, candidates,
                            LineSideParser.ParsePlacement(ls.DefaultPlacement));

                        row.ProposedAction = LineLabelPlanner.Describe(
                            label,
                            row.Length.Value / unitsPerFoot,
                            ls.MinLengthFeet, ls.RepeatIntervalFeet, ls.EndClearanceFeet,
                            side, ls.SideOffsetFeet);

                        // The same resolver FTFLABELLINE and the bulk pass use, so
                        // the review can never disagree with a placed label.
                        row.LabelLayerText = LabelLayerResolver.Resolve(
                            entity.Layer, candidates, layerCatalog,
                            ls.DefaultLabelLayer).Describe();
                    }

                    result.Rows.Add(row);

                    var layerText = row.LabelLayerText;
                    if (string.IsNullOrEmpty(layerText) && candidates.Count > 0)
                        layerText = LabelLayerResolver.Resolve(entity.Layer, candidates,
                            layerCatalog, ls.DefaultLabelLayer).Describe();
                    result.Items.Add(new FtfLineItem
                    {
                        Row = row,
                        Candidates = candidates,
                        LabelLayerText = layerText,
                        Status = FieldCodes.Review.LineDiagnostics.Classify(row, candidates, ls, unitsPerFoot),
                        Settings = FieldCodes.Review.LineDiagnostics.GoverningSettings(row, candidates, ls, layerText)
                    });

                    if (row.Source == LineIdentitySource.None) result.Unidentified++;
                    else if (row.Codes != null && row.Codes.IndexOf('/') >= 0) result.Ambiguous++;
                    else result.Identified++;
                }

                tr.Commit();
            }

            return result;
        }

        /// <summary>The drawing's actual layer table as the resolver's catalog. The
        /// resolver can only return layers from here or explicitly configured ones.</summary>
        internal static System.Collections.Generic.HashSet<string> LayerNames(
            Database db, Transaction tr)
        {
            var names = new List<string>();
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in table)
            {
                var record = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                names.Add(record.Name);
            }
            return LabelLayerResolver.BuildCatalog(names);
        }

        /// <summary>
        /// TrimbleName XData surviving from the TBC export, or null. This is the one
        /// piece of source identity the export preserves on some entities -- either
        /// the TBC feature name ("Edge of Pavement") or, should a future export carry
        /// it, the raw field coding ("ASPH L"). Read-only, like everything here.
        /// </summary>
        internal static string TrimbleNameOf(AcEntity entity)
        {
            using (var xdata = entity.GetXDataForApplication("TrimbleName"))
            {
                if (xdata == null) return null;

                foreach (TypedValue tv in xdata)
                {
                    if (tv.TypeCode != (int)DxfCode.ExtendedDataAsciiString) continue;
                    var text = tv.Value as string;
                    if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
                }
            }
            return null;
        }

        private static double? LengthOf(AcEntity entity)
        {
            var curve = entity as Curve;
            if (curve == null) return null;

            try
            {
                return curve.GetDistanceAtParameter(curve.EndParam);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                return null;    // degenerate geometry has no length
            }
        }
    }
}
