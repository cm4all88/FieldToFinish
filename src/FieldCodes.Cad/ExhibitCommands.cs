using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Easements;
using FieldCodes.Exhibits;
using FieldCodes.Settings;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace FieldCodes.Cad
{
    /// <summary>
    /// FTFEXHIBIT and FTFEXHIBITREBUILD: easement exhibits as editable paper-space layouts, built
    /// from the same easement records the drawing and the legal drafts use. The profile decides
    /// the sheet; FTF frames the view, drafts the repetitive annotation, and lists what a drafter
    /// should look at. A rebuild keeps hand moves of sheet furniture and never overwrites hand edits.
    /// </summary>
    public sealed class ExhibitCommands
    {
        // ============================================================== FTFEXHIBIT

        [CommandMethod("FTFEXHIBIT", CommandFlags.Modal)]
        public void MakeExhibit()
        {
            string switchTo = null;
            FtfSession.Run("FTFEXHIBIT", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                var records = DrawingStore.LoadEasements(db, tr);
                if (records.Count == 0) { ed.WriteMessage("\nFTFEXHIBIT: no FTF easements are stored in this drawing.\n"); return; }

                // 1. The easements.
                var chosen = new List<EasementRecord>();
                while (true)
                {
                    var r = EasementProductionCommands.PickAny(ed, tr, records, chosen.Count == 0 ? "\nSelect an easement for the exhibit: " : "\nNext easement <done>: ", chosen.Count > 0);
                    if (r == null) break;
                    if (chosen.All(c => c.Id != r.Id)) { chosen.Add(r); ed.WriteMessage("\n  {0}", r.Title); }
                }
                if (chosen.Count == 0) { ed.WriteMessage("\nFTFEXHIBIT: cancelled.\n"); return; }
                var related = records.Where(r => chosen.All(c => c.Id != r.Id) &&
                                                 chosen.Any(c => (c.GroupId != null && c.GroupId == r.GroupId) || (!string.IsNullOrWhiteSpace(c.ExhibitGroup) && c.ExhibitGroup == r.ExhibitGroup))).ToList();
                if (related.Count > 0)
                {
                    var include = EasementCommands.Keyword(ed, "\nInclude " + related.Count + " grouped easement(s) (" + string.Join(", ", related.Select(r => r.Title).ToArray()) + ") [Yes/No] <Yes>: ", "Yes", "Yes", "No");
                    if (include == null) return;
                    if (include == "Yes") chosen.AddRange(related);
                }

                // 2. The exhibit profile, information, scale and orientation.
                var exhibit = new ExhibitRecord();
                exhibit.Sources.AddRange(chosen.Select(r => new ExhibitSource { EasementId = r.Id, Title = r.Title }));
                var legal = chosen.Select(r => r.Legal).FirstOrDefault(l => l != null);
                exhibit.Info = new ExhibitInfo
                {
                    Title = "EXHIBIT B",
                    County = legal != null && !string.IsNullOrWhiteSpace(legal.County) ? legal.County + " COUNTY, " + (legal.State ?? string.Empty) : null,
                    Purpose = DefaultPurpose(chosen, settings.Easements),
                    Parcel = legal != null ? legal.ParcelDescription : null,
                    Sheet = "1 OF 1",
                    Date = DateTime.Now.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture)
                };
                var request = new ExhibitRequest { Exhibit = exhibit, ProfileNames = FtfSettings.ListProfiles().ToList(), Profile = DrawingStore.ReadProfileName(db), Scales = settings.Exhibits.ScaleList() };
                if (!(EasementInspectCommands.Headless() ? AskOnCommandLine(ed, request, settings) : ShowInfo(request)))
                {
                    ed.WriteMessage("\nFTFEXHIBIT: cancelled.\n");
                    return;
                }
                var xs = ExhibitProfile(request.Profile, settings);
                exhibit.ProfileName = request.Profile;
                exhibit.RotateAllowed = request.AllowRotate;
                exhibit.ScaleChosenByUser = request.Scale.HasValue;

                // 3. The layout, never replacing one without asking.
                var name = exhibit.Info.Fill(xs.SheetNameFormat);
                if (string.IsNullOrWhiteSpace(name)) name = "EXHIBIT";
                var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                if (layouts.Contains(name))
                {
                    var answer = EasementCommands.Keyword(ed, "\nLayout \"" + name + "\" already exists [Replace/NewName/Cancel] <NewName>: ", "NewName", "Replace", "NewName", "Cancel");
                    if (answer == null || answer == "Cancel") { ed.WriteMessage("\nFTFEXHIBIT: cancelled -- nothing changed.\n"); return; }
                    if (answer == "Replace")
                    {
                        foreach (var old in DrawingStore.LoadExhibits(db, tr).Where(x => string.Equals(x.LayoutName, name, StringComparison.OrdinalIgnoreCase)))
                            DrawingStore.DeleteExhibit(db, tr, old.Id);
                        LayoutManager.Current.DeleteLayout(name);
                    }
                    else
                    {
                        var n = 2;
                        while (layouts.Contains(name + " (" + n + ")")) n++;
                        name = name + " (" + n + ")";
                    }
                }
                exhibit.LayoutName = name;

                var builder = new ExhibitBuilder(db, tr, ed, settings, xs, rules.Version, exhibit, chosen, null);
                if (!builder.Build(request.Scale)) return;
                DrawingStore.SaveExhibit(db, tr, exhibit);
                Report(ed, "FTFEXHIBIT", exhibit);
                switchTo = name;
            });
            SwitchTo(switchTo);
        }

        // ======================================================= FTFEXHIBITREBUILD

        [CommandMethod("FTFEXHIBITREBUILD", CommandFlags.Modal)]
        public void RebuildExhibit()
        {
            FtfSession.Run("FTFEXHIBITREBUILD", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var settings = FtfSession.SettingsFor(db, rules);
                var records = DrawingStore.LoadEasements(db, tr);
                var exhibits = DrawingStore.LoadExhibits(db, tr);
                if (exhibits.Count == 0) { ed.WriteMessage("\nFTFEXHIBITREBUILD: no FTF exhibits in this drawing.\n"); return; }

                var current = exhibits.FirstOrDefault(x => string.Equals(x.LayoutName, LayoutManager.Current.CurrentLayout, StringComparison.OrdinalIgnoreCase));
                var fallback = current ?? exhibits[0];
                var answer = ed.GetString(new PromptStringOptions("\nExhibit layout to rebuild (" + string.Join(", ", exhibits.Select(x => x.LayoutName).ToArray()) + ") <" + fallback.LayoutName + ">: ") { AllowSpaces = true });
                if (answer.Status == PromptStatus.Cancel) return;
                var exhibit = answer.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(answer.StringResult)
                    ? exhibits.FirstOrDefault(x => string.Equals(x.LayoutName, answer.StringResult.Trim(), StringComparison.OrdinalIgnoreCase))
                    : fallback;
                if (exhibit == null) { ed.WriteMessage("\nFTFEXHIBITREBUILD: no exhibit layout by that name.\n"); return; }

                var stale = ExhibitPlanner.StaleSources(exhibit, records);
                foreach (var s in stale) ed.WriteMessage("\n  - " + s);
                var chosen = exhibit.Sources.Select(s => records.FirstOrDefault(r => r.Id == s.EasementId)).Where(r => r != null).ToList();
                if (chosen.Count == 0) { ed.WriteMessage("\nFTFEXHIBITREBUILD: none of the exhibit's easements are stored any more.\n"); return; }

                var xs = ExhibitProfile(exhibit.ProfileName, settings);
                var previous = exhibit.Items.ToList();
                var builder = new ExhibitBuilder(db, tr, ed, settings, xs, rules.Version, exhibit, chosen, previous);
                if (!builder.Build(exhibit.ScaleChosenByUser ? exhibit.Scale : (double?)null)) return;
                exhibit.RebuiltUtc = DateTime.UtcNow;

                DrawingStore.SaveExhibit(db, tr, exhibit);
                Report(ed, "FTFEXHIBITREBUILD", exhibit);
            });
        }

        /// <summary>"UTILITY EASEMENT", or "UTILITY AND TEMPORARY CONSTRUCTION EASEMENTS" for a group.</summary>
        internal static string DefaultPurpose(IList<EasementRecord> records, EasementSettings es)
        {
            var purposes = records.Select(r => (r.IsTemporary ? es.TemporaryPurpose : r.Purpose) ?? string.Empty)
                                  .Select(p => p.Trim().ToUpperInvariant()).Where(p => p.Length > 0).Distinct().ToList();
            if (purposes.Count == 0) return null;
            return string.Join(" AND ", purposes.ToArray()) + (purposes.Count > 1 ? " EASEMENTS" : " EASEMENT");
        }

        private static void Report(Editor ed, string command, ExhibitRecord exhibit)
        {
            ed.WriteMessage("\n{0}: layout \"{1}\" at 1\" = {2}'{3}; {4} generated item(s).", command, exhibit.LayoutName,
                            exhibit.Scale.ToString("0.##", CultureInfo.InvariantCulture),
                            Math.Abs(exhibit.RotationDegrees) > 1e-9 ? ", view turned " + exhibit.RotationDegrees.ToString("0.#", CultureInfo.InvariantCulture) + " degrees" : ", north up",
                            exhibit.Items.Count);
            if (exhibit.Review.Count == 0) ed.WriteMessage("\n  Review: nothing flagged -- still check the sheet before it goes out.");
            else
            {
                ed.WriteMessage("\n  Review -- {0} item(s) for a drafter to look at:", exhibit.Review.Count);
                foreach (var r in exhibit.Review) ed.WriteMessage("\n  ? " + r);
            }
            ed.WriteMessage("\n");
        }

        private static void SwitchTo(string layout)
        {
            if (layout == null || EasementInspectCommands.Headless()) return;
            try { LayoutManager.Current.CurrentLayout = layout; }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
        }

        /// <summary>The exhibit section of the chosen profile, or the drawing's own.</summary>
        internal static ExhibitSettings ExhibitProfile(string profile, FtfSettings current)
        {
            if (string.IsNullOrWhiteSpace(profile)) return current.Exhibits;
            try
            {
                var path = FtfSettings.ProfilePath(profile);
                if (File.Exists(path)) return FtfSettings.Load(path).Exhibits ?? current.Exhibits;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ConfigException) { }
            return current.Exhibits;
        }

        // ========================================================== information

        /// <summary>What the drafter chose for a new exhibit.</summary>
        internal sealed class ExhibitRequest
        {
            public ExhibitRecord Exhibit;
            public List<string> ProfileNames;
            public string Profile;
            public List<double> Scales;
            public double? Scale;
            public bool AllowRotate;
        }

        private static bool AskOnCommandLine(Editor ed, ExhibitRequest request, FtfSettings settings)
        {
            var info = request.Exhibit.Info;
            var fields = new List<KeyValuePair<string, Action<string>>>
            {
                new KeyValuePair<string, Action<string>>("Exhibit title <" + info.Title + ">", v => info.Title = v),
                new KeyValuePair<string, Action<string>>("Location (e.g. NE 1/4 OF SECTION 27, TOWNSHIP 28 N, RANGE 5 E, W.M.)", v => info.Location = v),
                new KeyValuePair<string, Action<string>>("Project", v => info.Project = v),
                new KeyValuePair<string, Action<string>>("Parcel" + (string.IsNullOrWhiteSpace(info.Parcel) ? string.Empty : " <" + info.Parcel + ">"), v => info.Parcel = v),
                new KeyValuePair<string, Action<string>>("Owner", v => info.Owner = v),
                new KeyValuePair<string, Action<string>>("APN", v => info.Apn = v),
                new KeyValuePair<string, Action<string>>("County" + (string.IsNullOrWhiteSpace(info.County) ? string.Empty : " <" + info.County + ">"), v => info.County = v),
                new KeyValuePair<string, Action<string>>("Purpose <" + info.Purpose + ">", v => info.Purpose = v),
                new KeyValuePair<string, Action<string>>("Sheet <" + info.Sheet + ">", v => info.Sheet = v),
                new KeyValuePair<string, Action<string>>("Prepared by", v => info.PreparedBy = v),
                new KeyValuePair<string, Action<string>>("Date <" + info.Date + ">", v => info.Date = v),
                new KeyValuePair<string, Action<string>>("Project number", v => info.ProjectNumber = v),
                new KeyValuePair<string, Action<string>>("Client", v => info.Client = v),
                new KeyValuePair<string, Action<string>>("Checked by", v => info.CheckedBy = v),
                new KeyValuePair<string, Action<string>>("Revision", v => info.Revision = v)
            };
            foreach (var f in fields)
            {
                var answer = ed.GetString(new PromptStringOptions("\n" + f.Key + ": ") { AllowSpaces = true });
                if (answer.Status == PromptStatus.Cancel) return false;
                if (answer.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(answer.StringResult)) f.Value(answer.StringResult.Trim().ToUpperInvariant());
            }
            var profile = ed.GetString(new PromptStringOptions("\nExhibit profile (" + (request.ProfileNames.Count == 0 ? "none saved" : string.Join(", ", request.ProfileNames.ToArray())) + ") <" + (string.IsNullOrWhiteSpace(request.Profile) ? "drawing settings" : request.Profile) + ">: ") { AllowSpaces = true });
            if (profile.Status == PromptStatus.Cancel) return false;
            if (profile.Status == PromptStatus.OK && !string.IsNullOrWhiteSpace(profile.StringResult))
            {
                var match = request.ProfileNames.FirstOrDefault(p => string.Equals(p, profile.StringResult.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match == null) ed.WriteMessage("\n  No profile \"{0}\"; using the drawing's settings.", profile.StringResult.Trim());
                request.Profile = match ?? request.Profile;
            }
            var scale = ed.GetString(new PromptStringOptions("\nScale, feet per inch (" + string.Join(", ", request.Scales.Select(s => s.ToString("0.##", CultureInfo.InvariantCulture)).ToArray()) + ") <Auto>: ") { AllowSpaces = false });
            if (scale.Status == PromptStatus.Cancel) return false;
            double value;
            if (scale.Status == PromptStatus.OK && double.TryParse(scale.StringResult, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0) request.Scale = value;
            var xs = ExhibitProfile(request.Profile, settings);
            var rotate = EasementCommands.Keyword(ed, "\nView [NorthUp/Rotate] <" + (string.Equals(xs.Orientation, "AllowRotate", StringComparison.OrdinalIgnoreCase) ? "Rotate" : "NorthUp") + ">: ",
                                                  string.Equals(xs.Orientation, "AllowRotate", StringComparison.OrdinalIgnoreCase) ? "Rotate" : "NorthUp", "NorthUp", "Rotate");
            if (rotate == null) return false;
            request.AllowRotate = rotate == "Rotate";
            return true;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool ShowInfo(ExhibitRequest request)
        {
            using (var form = new Ui.ExhibitInfoForm(request))
                return AcadApp.ShowModalDialog(form) == System.Windows.Forms.DialogResult.OK;
        }

        // =========================================================== hand changes

        internal static Point3d? PositionOf(AcEntity entity)
        {
            var mtext = entity as MText;
            if (mtext != null) return mtext.Location;
            var block = entity as BlockReference;
            if (block != null) return block.Position;
            var table = entity as Table;
            if (table != null) return table.Position;
            var leader = entity as MLeader;
            if (leader != null) return leader.TextLocation;
            // AutoCAD works a dimension's text position and line point out again when it draws it, so a
            // dimension is placed by its first extension point and how far its line stands off.
            var aligned = entity as AlignedDimension;
            if (aligned != null)
            {
                var along = aligned.XLine2Point - aligned.XLine1Point;
                if (along.Length < 1e-9) return aligned.XLine1Point;
                var normal = new Vector3d(-along.Y, along.X, 0).GetNormal();
                return aligned.XLine1Point + normal * normal.DotProduct(aligned.DimLinePoint - aligned.XLine1Point);
            }
            var dim = entity as Dimension;
            if (dim != null) return dim.TextPosition;
            var vp = entity as Viewport;
            if (vp != null) return vp.CenterPoint;
            var pl = entity as Polyline;
            if (pl != null && pl.NumberOfVertices > 0) return pl.GetPoint3dAt(0);
            return null;
        }

        internal static string TextOf(AcEntity entity)
        {
            var mtext = entity as MText;
            if (mtext != null) return mtext.Contents;
            var leader = entity as MLeader;
            if (leader != null && leader.ContentType == ContentType.MTextContent && leader.MText != null) return leader.MText.Contents;
            var table = entity as Table;
            if (table != null)
            {
                var cells = new List<string>();
                for (var r = 0; r < table.Rows.Count; r++)
                    for (var c = 0; c < table.Columns.Count; c++) cells.Add(table.Cells[r, c].TextString);
                return string.Join("|", cells.ToArray());
            }
            return null;
        }

        internal static bool Moved(AcEntity entity, ExhibitItem item)
        {
            var at = entity == null ? null : PositionOf(entity);
            return at.HasValue && (Math.Abs(at.Value.X - item.X) > 0.005 || Math.Abs(at.Value.Y - item.Y) > 0.005);
        }

        internal static bool Edited(AcEntity entity, ExhibitItem item)
        {
            if (entity == null || item.Text == null) return false;
            var text = TextOf(entity);
            return text != null && text != item.Text;
        }
    }

    /// <summary>
    /// Builds (or rebuilds) one exhibit layout. All sheet coordinates are paper inches.
    /// </summary>
    internal sealed class ExhibitBuilder
    {
        private readonly Database _db;
        private readonly Transaction _tr;
        private readonly Editor _ed;
        private readonly FtfSettings _settings;
        private readonly ExhibitSettings _xs;
        private readonly string _version;
        private readonly ExhibitRecord _exhibit;
        private readonly List<EasementRecord> _records;
        private readonly Dictionary<string, ExhibitItem> _previous;
        private readonly List<ExhibitReviewItem> _notes = new List<ExhibitReviewItem>();
        private readonly List<string> _notPlaced = new List<string>();
        private readonly HashSet<string> _keptEdited = new HashSet<string>();
        private readonly HashSet<string> _userErased = new HashSet<string>();
        private readonly Dictionary<string, Point3d> _userPositions = new Dictionary<string, Point3d>();
        private readonly List<SheetBox> _boxes = new List<SheetBox>();
        private readonly double _upf;
        private BlockTableRecord _space;
        private ObjectId _textStyle;
        private double _scale;
        private double _twist;
        private P2 _viewCenter;
        private P2 _vpCenter;

        public ExhibitBuilder(Database db, Transaction tr, Editor ed, FtfSettings settings, ExhibitSettings xs, string version,
                              ExhibitRecord exhibit, List<EasementRecord> records, List<ExhibitItem> previous)
        {
            _db = db; _tr = tr; _ed = ed; _settings = settings; _xs = xs; _version = version; _exhibit = exhibit; _records = records;
            _previous = previous == null ? null : previous.GroupBy(i => i.Key).ToDictionary(g => g.Key, g => g.First());
            _upf = settings.General.UnitsPerFoot;
        }

        private bool Rebuilding { get { return _previous != null; } }

        public bool Build(double? forcedScale)
        {
            // A paper-space viewport can only be switched on while its layout is current, so the
            // layout is made current for the build and the drafter's layout is restored afterwards.
            var manager = LayoutManager.Current;
            var before = manager.CurrentLayout;
            try
            {
                return BuildOnLayout(forcedScale);
            }
            finally
            {
                if (!string.Equals(manager.CurrentLayout, before, StringComparison.OrdinalIgnoreCase))
                {
                    try { manager.CurrentLayout = before; }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { }
                }
            }
        }

        private bool BuildOnLayout(double? forcedScale)
        {
            Ownership.EnsureRegApp(_db, _tr);
            if (!Layout()) return false;
            _textStyle = Setup.DrawingResources.FindTextStyle(_db, _tr, _xs.TextStyle);
            if (!string.IsNullOrWhiteSpace(_xs.TextStyle) && _textStyle.IsNull) Note("Warning", null, "Text style \"" + _xs.TextStyle + "\" is not in this drawing; the current style is used.");
            ApplyLineweights();

            if (Rebuilding) ClearForRebuild();
            _exhibit.Items.Clear();

            var viewport = Viewport(forcedScale);
            if (viewport == null) return false;
            _modelText = ModelTextObstacles(viewport);
            Hatches();

            Sheet();
            Easements();
            Tables();
            Review();

            _exhibit.Sources.Clear();
            _exhibit.Sources.AddRange(_records.Select(r => new ExhibitSource { EasementId = r.Id, Title = r.Title, Fingerprint = ExhibitPlanner.SourceFingerprint(r) }));
            return true;
        }

        // ================================================================ layout

        private bool Layout()
        {
            var layouts = (DBDictionary)_tr.GetObject(_db.LayoutDictionaryId, OpenMode.ForRead);
            var blank = false;
            if (!layouts.Contains(_exhibit.LayoutName))
            {
                if (Rebuilding)
                {
                    _ed.WriteMessage("\nThe exhibit layout \"{0}\" was deleted; it is created again.", _exhibit.LayoutName);
                    _previous.Clear();
                }
                var manager = LayoutManager.Current;
                if (!string.IsNullOrWhiteSpace(_xs.TemplateLayout) && layouts.Contains(_xs.TemplateLayout))
                    manager.CopyLayout(_xs.TemplateLayout, _exhibit.LayoutName);
                else if (!string.IsNullOrWhiteSpace(_xs.TemplateLayout) && ImportLayout(_xs.TemplateFile, _xs.TemplateLayout, _exhibit.LayoutName))
                    Note("Info", null, "The layout was made from \"" + _xs.TemplateLayout + "\" in " + Path.GetFileName(_xs.TemplateFile) + ".");
                else
                {
                    if (!string.IsNullOrWhiteSpace(_xs.TemplateLayout)) Note("Warning", null, "Template layout \"" + _xs.TemplateLayout + "\" is not in this drawing" + (string.IsNullOrWhiteSpace(_xs.TemplateFile) ? string.Empty : " or in " + _xs.TemplateFile) + "; a blank layout was used.");
                    manager.CreateLayout(_exhibit.LayoutName);
                    blank = true;
                }
                layouts = (DBDictionary)_tr.GetObject(_db.LayoutDictionaryId, OpenMode.ForRead);
            }
            if (!string.Equals(LayoutManager.Current.CurrentLayout, _exhibit.LayoutName, StringComparison.OrdinalIgnoreCase))
                LayoutManager.Current.CurrentLayout = _exhibit.LayoutName;
            var layout = (Layout)_tr.GetObject(layouts.GetAt(_exhibit.LayoutName), OpenMode.ForWrite);
            _space = (BlockTableRecord)_tr.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);
            if (blank)
            {
                // The viewport AutoCAD adds to a new layout is replaced by the profile's.
                foreach (ObjectId id in _space)
                {
                    var vp = _tr.GetObject(id, OpenMode.ForRead) as Viewport;
                    if (vp == null || vp.Number == 1 || Ownership.Read(vp) != null) continue;
                    vp.UpgradeOpen();
                    vp.Erase();
                }
            }
            if (!Rebuilding) SetPaper(layout);
            return true;
        }

        private void SetPaper(Layout layout)
        {
            if (string.IsNullOrWhiteSpace(_xs.PlotDevice) || string.IsNullOrWhiteSpace(_xs.MediaName)) return;
            try
            {
                var validator = PlotSettingsValidator.Current;
                using (var plot = new PlotSettings(layout.ModelType))
                {
                    plot.CopyFrom(layout);
                    validator.SetPlotConfigurationName(plot, _xs.PlotDevice, _xs.MediaName);
                    validator.SetPlotPaperUnits(plot, PlotPaperUnit.Inches);
                    if (!string.IsNullOrWhiteSpace(_xs.PlotStyleTable))
                    {
                        if (validator.GetPlotStyleSheetList().Cast<string>().Any(s => string.Equals(s, _xs.PlotStyleTable, StringComparison.OrdinalIgnoreCase)))
                            validator.SetCurrentStyleSheet(plot, _xs.PlotStyleTable);
                        else
                            Note("Warning", null, "Plot style table \"" + _xs.PlotStyleTable + "\" is not installed on this computer; the layout keeps its own.");
                    }
                    layout.CopyFrom(plot);
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                Note("Warning", null, "The sheet size could not be set from the profile (" + _xs.PlotDevice + ", " + _xs.MediaName + "): " + ex.Message + ". Set the page setup by hand.");
            }
        }

        /// <summary>
        /// Brings a layout in from a template drawing: its page setup and everything on it except the
        /// paper-space viewport itself. Blocks, layers and styles it uses come along; any already in
        /// this drawing are kept as they are.
        /// </summary>
        private bool ImportLayout(string file, string layoutName, string newName)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return false;
            try
            {
                using (var source = new Database(false, true))
                {
                    source.ReadDwgFile(file, FileShare.Read, true, string.Empty);
                    using (var st = source.TransactionManager.StartOpenCloseTransaction())
                    {
                        var dict = (DBDictionary)st.GetObject(source.LayoutDictionaryId, OpenMode.ForRead);
                        if (!dict.Contains(layoutName)) return false;
                        var sourceLayout = (Layout)st.GetObject(dict.GetAt(layoutName), OpenMode.ForRead);
                        var sourceSpace = (BlockTableRecord)st.GetObject(sourceLayout.BlockTableRecordId, OpenMode.ForRead);
                        var ids = new ObjectIdCollection();
                        var first = true;
                        foreach (ObjectId id in sourceSpace)
                        {
                            if (first && id.ObjectClass.DxfName == "VIEWPORT") { first = false; continue; }     // the sheet's own viewport
                            first = false;
                            ids.Add(id);
                        }
                        var layoutId = LayoutManager.Current.CreateLayout(newName);
                        var layout = (Layout)_tr.GetObject(layoutId, OpenMode.ForWrite);
                        layout.CopyFrom(sourceLayout);
                        if (ids.Count > 0) source.WblockCloneObjects(ids, layout.BlockTableRecordId, new IdMapping(), DuplicateRecordCloning.Ignore, false);
                        st.Commit();
                    }
                }
                return true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                Note("Warning", null, "The template layout could not be brought in from " + file + ": " + ex.Message);
                return false;
            }
            catch (IOException ex)
            {
                Note("Warning", null, "The template layout could not be brought in from " + file + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>A block definition by name: this drawing's own, or brought in from the profile's block library.</summary>
        private ObjectId BlockDef(string name, string key, string library = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return ObjectId.Null;
            var blocks = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
            if (blocks.Has(name)) return blocks[name];
            library = string.IsNullOrWhiteSpace(library) ? _xs.BlockLibrary : library;
            if (!string.IsNullOrWhiteSpace(library) && File.Exists(library))
            {
                try
                {
                    using (var source = new Database(false, true))
                    {
                        source.ReadDwgFile(library, FileShare.Read, true, string.Empty);
                        ObjectId sourceId;
                        using (var st = source.TransactionManager.StartOpenCloseTransaction())
                        {
                            var sourceBlocks = (BlockTable)st.GetObject(source.BlockTableId, OpenMode.ForRead);
                            sourceId = sourceBlocks.Has(name) ? sourceBlocks[name] : ObjectId.Null;
                            st.Commit();
                        }
                        if (!sourceId.IsNull)
                        {
                            source.WblockCloneObjects(new ObjectIdCollection { sourceId }, _db.BlockTableId, new IdMapping(), DuplicateRecordCloning.Ignore, false);
                            blocks = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
                            if (blocks.Has(name))
                            {
                                Note("Info", key, "Block \"" + name + "\" was brought in from the block library " + Path.GetFileName(library) + ".");
                                return blocks[name];
                            }
                        }
                    }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    Note("Warning", key, "Block \"" + name + "\" could not be brought in from " + library + ": " + ex.Message);
                    return ObjectId.Null;
                }
                catch (IOException ex)
                {
                    Note("Warning", key, "Block \"" + name + "\" could not be brought in from " + library + ": " + ex.Message);
                    return ObjectId.Null;
                }
            }
            Note("Warning", key, "Block \"" + name + "\" is not in this drawing" + (string.IsNullOrWhiteSpace(library) ? string.Empty : " or the block library " + library) + ".");
            return ObjectId.Null;
        }

        /// <summary>
        /// Inserts a sheet block with its attributes. Attributes the profile maps to an exhibit field get that
        /// value; every other attribute keeps exactly what the block defines, and is listed for the drafter.
        /// </summary>
        private BlockReference InsertSheetBlock(ObjectId defId, Point3d at, string key, string kind, string layer, bool keepPosition, string text, double scale = 1)
        {
            var reference = new BlockReference(at, defId);
            if (_userErased.Contains(key)) { reference.Dispose(); return null; }
            reference.SetDatabaseDefaults(_db);
            if (Math.Abs(scale - 1) > 1e-12) reference.ScaleFactors = new Scale3d(scale);
            Add(reference, key, kind, FtfEntityKind.ExhibitSymbol, layer, text, keepPosition, at);
            if (reference.ObjectId.IsNull) return null;
            var def = (BlockTableRecord)_tr.GetObject(defId, OpenMode.ForRead);
            if (!def.HasAttributeDefinitions) return reference;

            var map = _xs.AttributeMap();
            var tokens = _exhibit.Info.Tokens();
            var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "TITLE", "{title}" }, { "LOCATION", "{location}" }, { "PROJECT", "{project}" }, { "PARCEL", "{parcel}" }, { "OWNER", "{owner}" },
                { "APN", "{apn}" }, { "COUNTY", "{county}" }, { "PURPOSE", "{purpose}" }, { "SHEET", "{sheet}" }, { "PREPAREDBY", "{preparedBy}" },
                { "DATE", "{date}" }, { "PROJECTNUMBER", "{projectNumber}" }, { "CLIENT", "{client}" }, { "CHECKEDBY", "{checkedBy}" }, { "REVISION", "{revision}" }
            };
            var untouched = new List<string>();
            var filled = new List<string>();
            foreach (ObjectId id in def)
            {
                var attribute = _tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;
                if (attribute == null || attribute.Constant) continue;
                var attRef = new AttributeReference();
                attRef.SetAttributeFromBlock(attribute, reference.BlockTransform);
                string template = null;
                var mapped = map.FirstOrDefault(m => string.Equals(m.Key, attribute.Tag, StringComparison.OrdinalIgnoreCase));
                if (mapped.Key != null) template = mapped.Value;
                else if (map.Count == 0) defaults.TryGetValue(attribute.Tag, out template);
                var value = template == null ? null : _exhibit.Info.Fill(template);
                if (!string.IsNullOrWhiteSpace(value)) { attRef.TextString = value.ToUpperInvariant(); filled.Add(attribute.Tag); }
                else if (template == null) untouched.Add(attribute.Tag);
                reference.AttributeCollection.AppendAttribute(attRef);
                _tr.AddNewlyCreatedDBObject(attRef, true);
            }
            if (untouched.Count > 0)
                Note("Info", key, "\"" + def.Name + "\" attributes left as the block defines them (FTF does not know what they hold): " + string.Join(", ", untouched.ToArray()) + ".");
            return reference;
        }

        private void ApplyLineweights()
        {
            foreach (var pair in ExhibitSettings.Split(_xs.Lineweights))
            {
                var parts = pair.Split('=');
                double mm;
                if (parts.Length != 2 || !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out mm)) continue;
                var table = (LayerTable)_tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
                var name = parts[0].Trim();
                if (!table.Has(name)) continue;
                var layer = (LayerTableRecord)_tr.GetObject(table[name], OpenMode.ForRead);
                if (layer.LineWeight != LineWeight.ByLineWeightDefault) continue;     // the office has already set it
                LineWeight weight;
                if (!Enum.TryParse("LineWeight" + ((int)Math.Round(mm * 100)).ToString("000", CultureInfo.InvariantCulture), out weight)) continue;
                layer.UpgradeOpen();
                layer.LineWeight = weight;
            }
        }

        // ========================================================== rebuild prep

        private void ClearForRebuild()
        {
            var owned = Ownership.FindOwnedInLayouts(_db, _tr, _exhibit.LayoutName, s => s.PointNumber == _exhibit.Id);
            var byHandle = owned.ToDictionary(kv => kv.Key.Handle.ToString(), kv => kv.Key);
            foreach (var item in _previous.Values)
            {
                ObjectId id;
                if (item.Handle == null || !byHandle.TryGetValue(item.Handle, out id))
                {
                    if (item.Kind != "VIEWPORT")
                    {
                        _userErased.Add(item.Key);
                        Note("Info", item.Key, Describe(item) + " was erased by hand; it is not drawn again.");
                    }
                    continue;
                }
                var entity = (AcEntity)_tr.GetObject(id, OpenMode.ForRead);
                if (ExhibitCommands.Edited(entity, item))
                {
                    _keptEdited.Add(item.Key);
                    continue;                                  // decided when its replacement text is known
                }
                if (item.KeepPosition && ExhibitCommands.Moved(entity, item))
                    _userPositions[item.Key] = ExhibitCommands.PositionOf(entity).Value;
                if (item.Kind == "VIEWPORT") continue;
                entity.UpgradeOpen();
                entity.Erase();
            }
        }

        private static string Describe(ExhibitItem item)
        {
            switch (item.Kind)
            {
                case "TABLE": return item.Key == "AREATABLE" ? "The area table" : "The line/curve table";
                case "SYMBOL": return item.Key == "NORTH" ? "The north arrow" : item.Key == "SCALEBAR" ? "The scale bar" : "The legend";
                case "TITLE": return "The title";
                case "STAMP": return "The stamp place";
                default: return "\"" + (item.Text ?? item.Key).Replace("\\P", " / ") + "\"";
            }
        }

        // ============================================================== viewport

        private Viewport Viewport(double? forcedScale)
        {
            Viewport vp = null;
            ExhibitItem old = null;
            if (Rebuilding && _previous.TryGetValue("VIEWPORT", out old))
            {
                var entity = EasementCommands.Resolve(_db, _tr, old.Handle);
                vp = entity as Viewport;
            }
            var created = false;
            if (vp == null)
            {
                // A template layout's own main viewport is used; otherwise one is made where the profile says.
                vp = LargestViewport();
                if (vp == null)
                {
                    vp = new Viewport();
                    vp.SetDatabaseDefaults(_db);
                    _space.AppendEntity(vp);
                    _tr.AddNewlyCreatedDBObject(vp, true);
                    created = true;
                }
                else vp.UpgradeOpen();
            }
            else vp.UpgradeOpen();

            var points = ExtentPoints();
            if (points.Count == 0) { _ed.WriteMessage("\nFTFEXHIBIT: the easements have no stored geometry.\n"); return null; }

            if (created || vp.Width < 0.1)
            {
                vp.CenterPoint = new Point3d(_xs.ViewportLeftIn + _xs.ViewportWidthIn / 2, _xs.ViewportBottomIn + _xs.ViewportHeightIn / 2, 0);
                vp.Width = _xs.ViewportWidthIn;
                vp.Height = _xs.ViewportHeightIn;
            }
            var layersBefore = (LayerTable)_tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            var viewportLayerIsNew = !string.IsNullOrWhiteSpace(_xs.ViewportLayer) && !layersBefore.Has(_xs.ViewportLayer);
            vp.LayerId = ProductionLayers.Get(_db, _tr, _xs.ViewportLayer, _settings);
            var viewportLayer = (LayerTableRecord)_tr.GetObject(vp.LayerId, OpenMode.ForRead);
            if (viewportLayerIsNew && viewportLayer.IsPlottable)
            {
                // A viewport layer FTF creates does not plot: the frame is a working edge, not a line on the sheet.
                viewportLayer.UpgradeOpen();
                viewportLayer.IsPlottable = false;
            }
            _viewportFramePlots = viewportLayer.IsPlottable;

            // A view the drafter adjusted on the sheet is kept on a rebuild.
            var userView = Rebuilding && old != null && ViewChanged(vp);
            if (userView)
            {
                _scale = 1.0 / vp.CustomScale / _upf;
                _twist = vp.TwistAngle * 180 / Math.PI;
                var dcs = new P2(vp.ViewCenter.X, vp.ViewCenter.Y);
                _viewCenter = ExhibitPlanner.Rotate(dcs, -_twist);
                Note("Info", "VIEWPORT", "The viewport's view was adjusted by hand; its scale and centre are kept.");
            }
            else
            {
                var fit = ExhibitPlanner.Choose(points, vp.Width, vp.Height, _xs.ScaleList(), _xs.FitMargin, _exhibit.RotateAllowed, _upf, forcedScale, null);
                _scale = fit.Scale;
                _twist = fit.RotationDegrees;
                _viewCenter = fit.ViewCenter;
                if (!fit.Fits)
                    Note("Error", "VIEWPORT", "Everything does not fit in the viewport at 1\" = " + fit.Scale.ToString("0.##", CultureInfo.InvariantCulture) + "'; choose a smaller scale, a larger viewport or a larger sheet.");
                if (Math.Abs(_twist) > 1e-9) Note("Info", "VIEWPORT", "The view is turned: " + fit.Reason + ". The survey geometry is not rotated.");
                vp.ViewDirection = Vector3d.ZAxis;
                vp.ViewTarget = Point3d.Origin;
                vp.TwistAngle = _twist * Math.PI / 180;
                var dcs = ExhibitPlanner.Rotate(_viewCenter, _twist);
                vp.ViewCenter = new Point2d(dcs.X, dcs.Y);
                vp.CustomScale = 1.0 / (_scale * _upf);
                SetAnnotationScale(vp);
            }
            vp.On = true;
            ApplyViewportLayers(vp);
            HiddenAnnotation(vp);
            if (_xs.LockViewport) vp.Locked = true;

            _vpCenter = new P2(vp.CenterPoint.X, vp.CenterPoint.Y);
            _exhibit.Scale = _scale;
            _exhibit.RotationDegrees = _twist;
            _exhibit.ViewCenter = _viewCenter;
            _exhibit.ViewportHandle = vp.Handle.ToString();
            Stamp(vp, "VIEWPORT", "VIEWPORT", FtfEntityKind.ExhibitViewport, null, false, vp.CenterPoint);
            _viewport = new SheetRect(vp.CenterPoint.X - vp.Width / 2, vp.CenterPoint.Y - vp.Height / 2, vp.CenterPoint.X + vp.Width / 2, vp.CenterPoint.Y + vp.Height / 2);
            return vp;
        }

        private SheetRect _viewport;
        private bool _viewportFramePlots = true;

        /// <summary>
        /// The viewport's annotation scale matches its scale, so annotative labels already in the drawing
        /// (lot and parcel names, APN tags, monument notes) show in the exhibit as they do on office sheets.
        /// A matching scale is added to the drawing's list when it has none.
        /// </summary>
        private void SetAnnotationScale(Viewport vp)
        {
            try
            {
                var scales = _db.ObjectContextManager.GetContextCollection("ACDB_ANNOTATIONSCALES");
                AnnotationScale match = null;
                foreach (ObjectContext context in scales)
                {
                    var a = context as AnnotationScale;
                    if (a == null || a.DrawingUnits <= 0) continue;
                    if (Math.Abs(a.Scale - vp.CustomScale) <= vp.CustomScale * 1e-6) { match = a; break; }
                }
                if (match == null)
                {
                    match = new AnnotationScale
                    {
                        Name = "1\" = " + _scale.ToString("0.##", CultureInfo.InvariantCulture) + "'",
                        PaperUnits = 1,
                        DrawingUnits = _scale * _upf
                    };
                    scales.AddContext(match);
                    Note("Info", "VIEWPORT", "Annotation scale " + match.Name + " was added to the drawing for the exhibit viewport.");
                }
                vp.AnnotationScale = match;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                Note("Warning", "VIEWPORT", "The viewport's annotation scale could not be set (" + ex.Message + "); annotative labels may not show in it.");
            }
        }

        private bool ViewChanged(Viewport vp)
        {
            var expectedScale = 1.0 / (_exhibit.Scale * _upf);
            var dcs = ExhibitPlanner.Rotate(_exhibit.ViewCenter, _exhibit.RotationDegrees);
            return Math.Abs(vp.CustomScale - expectedScale) > expectedScale * 1e-6 ||
                   Math.Abs(vp.TwistAngle - _exhibit.RotationDegrees * Math.PI / 180) > 1e-6 ||
                   Math.Abs(vp.ViewCenter.X - dcs.X) > 0.01 * _exhibit.Scale || Math.Abs(vp.ViewCenter.Y - dcs.Y) > 0.01 * _exhibit.Scale;
        }

        private Viewport LargestViewport()
        {
            Viewport best = null;
            foreach (ObjectId id in _space)
            {
                var vp = _tr.GetObject(id, OpenMode.ForRead) as Viewport;
                if (vp == null || vp.Number == 1 || vp.Width < 0.1) continue;
                if (Ownership.Read(vp) != null) continue;
                if (best == null || vp.Width * vp.Height > best.Width * best.Height) best = vp;
            }
            return best;
        }

        /// <summary>
        /// Sets what this viewport shows, layer by layer, from the profile's rules -- in this viewport
        /// only; model space and other viewports are untouched. A layer the drafter changed in the
        /// viewport by hand since the last build is left as the drafter set it, now and on later rebuilds.
        /// </summary>
        private void ApplyViewportLayers(Viewport vp)
        {
            // The profile's rules, with this exhibit's own choice for overhead power and other exhibits' hatches.
            var rules = _xs.LayerRules(_exhibit.OverheadPower, _exhibit.OtherHatches);
            if (_exhibit.ViewportLayers == null) _exhibit.ViewportLayers = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (_exhibit.UserLayers == null) _exhibit.UserLayers = new List<string>();
            var previous = new Dictionary<string, bool>(_exhibit.ViewportLayers, StringComparer.OrdinalIgnoreCase);
            var frozenNow = new HashSet<string>(vp.GetFrozenLayers().Cast<ObjectId>().Select(id => ((LayerTableRecord)_tr.GetObject(id, OpenMode.ForRead)).Name), StringComparer.OrdinalIgnoreCase);
            var table = (LayerTable)_tr.GetObject(_db.LayerTableId, OpenMode.ForRead);

            var byLayer = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in table)
            {
                var layer = (LayerTableRecord)_tr.GetObject(id, OpenMode.ForRead);
                if (layer.IsDependent) continue;
                var action = ExhibitSettings.ActionFor(rules, layer.Name);
                if (action != null) byLayer[layer.Name] = action;
            }
            var relevance = LayerRelevance(byLayer.Where(kv => kv.Value == ExhibitSettings.Relevant).Select(kv => kv.Key).ToList(), vp);

            var freeze = new ObjectIdCollection();
            var thaw = new ObjectIdCollection();
            var applied = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var newlyUser = new List<string>();
            foreach (var kv in byLayer)
            {
                var name = kv.Key;
                var isFrozen = frozenNow.Contains(name);
                bool before;
                if (Rebuilding && previous.TryGetValue(name, out before) && before != isFrozen && !_exhibit.UserLayers.Contains(name, StringComparer.OrdinalIgnoreCase))
                    { _exhibit.UserLayers.Add(name); newlyUser.Add(name); }
                if (kv.Value == ExhibitSettings.User || _exhibit.UserLayers.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

                bool wantFrozen;
                if (kv.Value == ExhibitSettings.Hide) wantFrozen = true;
                else if (kv.Value == ExhibitSettings.Show) wantFrozen = false;
                else
                {
                    Relevance r;
                    wantFrozen = !(relevance.TryGetValue(name, out r) && r.Related > 0);
                    if (r != null && !wantFrozen && r.Unrelated > 0)
                        Note("Warning", "VIEWPORT", "Layer " + name + " also shows " + r.Unrelated + " object(s) of easements not on this exhibit; hide them in the viewport if they do not belong.");
                }
                if (wantFrozen != isFrozen) (wantFrozen ? freeze : thaw).Add(table[name]);
                applied[name] = wantFrozen;
            }
            if (freeze.Count > 0) vp.FreezeLayersInViewport(freeze.Cast<ObjectId>().GetEnumerator());
            if (thaw.Count > 0) vp.ThawLayersInViewport(thaw.Cast<ObjectId>().GetEnumerator());
            _exhibit.ViewportLayers = applied;
            if (newlyUser.Count > 0)
                Note("Info", "VIEWPORT", "Viewport layer visibility changed by hand is kept: " + string.Join(", ", newlyUser.ToArray()) + ".");
            var power = applied.Where(kv => ExhibitSettings.Split(_xs.OverheadPowerLayers).Any(p => ExhibitSettings.Wildcard(p, kv.Key))).ToList();
            if (power.Count > 0)
                Note("Info", "VIEWPORT", "Overhead power (" + string.Join(", ", power.Select(kv => kv.Key).ToArray()) + ") is " + (power.All(kv => kv.Value) ? "hidden" : "shown") +
                     " in this viewport, per " + (_exhibit.OverheadPower != null ? "this exhibit's choice" : "the profile") + "; model space is unchanged.");
            var hatches = applied.Where(kv => kv.Value && ExhibitSettings.Split(_xs.OtherHatchLayers).Any(p => ExhibitSettings.Wildcard(p, kv.Key))).Select(kv => kv.Key).ToList();
            if (hatches.Count > 0)
                Note("Info", "VIEWPORT", "Other exhibits' hatch layers hidden in this viewport: " + string.Join(", ", hatches.ToArray()) + "; model space is unchanged.");
        }

        // ============================================================ obstacles

        private List<SheetRect> _modelText = new List<SheetRect>();
        private readonly List<Tuple<P2, P2>> _modelLines = new List<Tuple<P2, P2>>();

        /// <summary>
        /// Text already in the drawing that shows in the viewport (lot and parcel names, APN tags, street names):
        /// exhibit labels and leaders keep off it where they can. Sizes are paper sizes at the viewport scale.
        /// </summary>
        private List<SheetRect> ModelTextObstacles(Viewport vp)
        {
            var rects = new List<SheetRect>();
            try
            {
                var frozen = new HashSet<ObjectId>(vp.GetFrozenLayers().Cast<ObjectId>());
                AnnotationScale current = null;
                try { current = vp.AnnotationScale; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                double cannoPaperPerUnit = 0;
                try { var c = _db.Cannoscale; if (c != null && c.DrawingUnits > 0) cannoPaperPerUnit = c.PaperUnits / c.DrawingUnits; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                var toPaper = 1.0 / (_scale * _upf);
                var ms = (BlockTableRecord)_tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForRead);
                var layers = new Dictionary<ObjectId, bool>();
                foreach (ObjectId id in ms)
                {
                    if (rects.Count > 4000 || _modelLines.Count > 40000) break;
                    var cls = id.ObjectClass.DxfName;
                    if (cls == "LINE" || cls == "LWPOLYLINE" || cls == "ARC")
                    {
                        // Linework showing in the view, as paper segments: tables look for space clear of it.
                        var curve = _tr.GetObject(id, OpenMode.ForRead) as Curve;
                        if (curve == null || frozen.Contains(curve.LayerId)) continue;
                        var owner = Ownership.Read(curve);
                        if (owner != null && _records.Any(r => r.Id == owner.PointNumber)) continue;     // the exhibit's own easement lines are counted already
                        bool shows;
                        if (!layers.TryGetValue(curve.LayerId, out shows))
                        {
                            var layer = (LayerTableRecord)_tr.GetObject(curve.LayerId, OpenMode.ForRead);
                            shows = !layer.IsOff && !layer.IsFrozen;
                            layers[curve.LayerId] = shows;
                        }
                        if (!shows) continue;
                        string why;
                        var courses = EasementCommands.Extract(curve, out why);
                        if (courses == null) continue;
                        foreach (var c in courses)
                        {
                            var steps = c.Kind == CourseKind.Arc ? Math.Max(2, (int)Math.Ceiling(c.Sweep / (Math.PI / 12))) : 1;
                            for (var k = 0; k < steps; k++)
                            {
                                var a = Paper(c.PointAt(c.Length * k / steps));
                                var b = Paper(c.PointAt(c.Length * (k + 1) / steps));
                                if (Math.Max(a.X, b.X) < _viewport.MinX || Math.Min(a.X, b.X) > _viewport.MaxX || Math.Max(a.Y, b.Y) < _viewport.MinY || Math.Min(a.Y, b.Y) > _viewport.MaxY) continue;
                                _modelLines.Add(Tuple.Create(a, b));
                            }
                        }
                        continue;
                    }
                    if (cls != "MTEXT" && cls != "TEXT" && cls != "MULTILEADER") continue;
                    var entity = _tr.GetObject(id, OpenMode.ForRead) as AcEntity;
                    if (entity == null || frozen.Contains(entity.LayerId)) continue;
                    bool visible;
                    if (!layers.TryGetValue(entity.LayerId, out visible))
                    {
                        var layer = (LayerTableRecord)_tr.GetObject(entity.LayerId, OpenMode.ForRead);
                        visible = !layer.IsOff && !layer.IsFrozen;
                        layers[entity.LayerId] = visible;
                    }
                    if (!visible) continue;
                    var stamp = Ownership.Read(entity);
                    if (stamp != null && _records.Any(r => r.Id == stamp.PointNumber)) continue;     // the exhibit's own easement text is placed with the easement
                    Extents3d e;
                    try { e = entity.GeometricExtents; } catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                    var factor = toPaper;
                    if (entity.Annotative == AnnotativeStates.True)
                    {
                        // Shown at the viewport's scale only if it supports it; its size there is its paper size.
                        bool supported;
                        try { supported = current != null && entity.HasContext(current); } catch (Autodesk.AutoCAD.Runtime.Exception) { supported = false; }
                        if (!supported || cannoPaperPerUnit <= 0) continue;
                        factor = cannoPaperPerUnit;
                    }
                    // Turned text (street names, plat names along a line) is a row of small boxes along its baseline, not
                    // one large box around it, so the space beside it still counts as free.
                    var mtext = entity as MText;
                    var dbtext = entity as DBText;
                    var angle = (mtext != null ? mtext.Rotation : dbtext != null ? dbtext.Rotation : 0) + _twist * Math.PI / 180;
                    var widthIn = (mtext != null ? mtext.ActualWidth : dbtext != null ? e.MaxPoint.X - e.MinPoint.X : e.MaxPoint.X - e.MinPoint.X) * factor;
                    var heightIn = (mtext != null ? mtext.ActualHeight : dbtext != null ? dbtext.Height : e.MaxPoint.Y - e.MinPoint.Y) * factor;
                    var mid = Paper(new P2((e.MinPoint.X + e.MaxPoint.X) / 2, (e.MinPoint.Y + e.MaxPoint.Y) / 2));
                    if (entity is MLeader || Math.Abs(Math.Sin(2 * angle)) < 0.05 || widthIn <= 0 || heightIn <= 0)
                    {
                        // Square to the sheet: the drawing box, sized for the viewport and turned with the view.
                        var halfW = (e.MaxPoint.X - e.MinPoint.X) / 2 * factor;
                        var halfH = (e.MaxPoint.Y - e.MinPoint.Y) / 2 * factor;
                        var twist = _twist * Math.PI / 180;
                        var w2 = Math.Abs(halfW * Math.Cos(twist)) + Math.Abs(halfH * Math.Sin(twist));
                        var h2 = Math.Abs(halfW * Math.Sin(twist)) + Math.Abs(halfH * Math.Cos(twist));
                        var whole = new SheetRect(mid.X - w2, mid.Y - h2, mid.X + w2, mid.Y + h2);
                        if (whole.MaxX < _viewport.MinX || whole.MinX > _viewport.MaxX || whole.MaxY < _viewport.MinY || whole.MinY > _viewport.MaxY) continue;
                        rects.Add(whole);
                        continue;
                    }
                    var dir = new P2(Math.Cos(angle), Math.Sin(angle));
                    var pieces = Math.Max(1, (int)Math.Ceiling(widthIn / heightIn));
                    for (var k = 0; k < pieces; k++)
                    {
                        var along = -widthIn / 2 + widthIn * (k + 0.5) / pieces;
                        var c = new P2(mid.X + dir.X * along, mid.Y + dir.Y * along);
                        var half = Math.Min(heightIn, widthIn / pieces) * 0.6;
                        var rect = new SheetRect(c.X - half, c.Y - half, c.X + half, c.Y + half);
                        if (rect.MaxX < _viewport.MinX || rect.MinX > _viewport.MaxX || rect.MaxY < _viewport.MinY || rect.MinY > _viewport.MaxY) continue;
                        rects.Add(rect);
                    }
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
            return rects;
        }

        // ============================================================== hatches

        /// <summary>
        /// Easement hatches print the profile's line spacing at this exhibit's scale. Only the hatch's pattern scale
        /// changes -- never the easement. A scale the drafter set by hand is noticed, recorded and kept.
        /// </summary>
        private void Hatches()
        {
            if (_xs.HatchSpacingIn <= 0 && string.IsNullOrWhiteSpace(_xs.HatchSpacings)) return;
            var unitsPerPaperInch = _scale * _upf;
            var exhibits = DrawingStore.LoadExhibits(_db, _tr);
            foreach (var r in _records)
            {
                var changed = false;
                var set = false;
                if (r.HatchScales == null) r.HatchScales = new Dictionary<string, double>();
                foreach (var kv in Ownership.FindOwned(_db, _tr, s => s.PointNumber == r.Id && s.Kind == FtfEntityKind.EasementHatch))
                {
                    var hatch = _tr.GetObject(kv.Key, OpenMode.ForRead) as Hatch;
                    if (hatch == null || string.Equals(hatch.PatternName, "SOLID", StringComparison.OrdinalIgnoreCase)) continue;
                    var spacing = ExhibitQaCommands.LineSpacing(hatch);
                    var wanted = _xs.HatchSpacingFor(hatch.PatternName);
                    if (!spacing.HasValue || wanted <= 0) continue;
                    var handle = hatch.Handle.ToString();
                    double last;
                    var hasLast = r.HatchScales.TryGetValue(handle, out last);
                    var target = HatchScaling.PatternScaleFor(hatch.PatternScale, spacing.Value, wanted, unitsPerPaperInch);
                    switch (HatchScaling.Decide(hatch.PatternScale, hasLast ? last : (double?)null, r.HatchPatternScaleByHand, target))
                    {
                        case HatchAction.RecordHand:
                            r.HatchPatternScaleByHand = hatch.PatternScale;
                            changed = true;
                            Note("Info", "VIEWPORT", r.Title + ": its hatch pattern scale was changed by hand to " + hatch.PatternScale.ToString("0.###", CultureInfo.InvariantCulture) +
                                 "; FTF keeps it and no longer rescales this hatch for exhibits.");
                            break;
                        case HatchAction.KeepHand:
                            SetPatternScale(hatch, r.HatchPatternScaleByHand.Value);
                            r.HatchScales[handle] = r.HatchPatternScaleByHand.Value;
                            changed = true;
                            Note("Info", "VIEWPORT", r.Title + ": the hatch pattern scale set by hand (" + r.HatchPatternScaleByHand.Value.ToString("0.###", CultureInfo.InvariantCulture) + ") is kept.");
                            break;
                        case HatchAction.Set:
                            SetPatternScale(hatch, target);
                            r.HatchScales[handle] = target;
                            changed = set = true;
                            break;
                        default:
                            if (!hasLast && !r.HatchPatternScaleByHand.HasValue) { r.HatchScales[handle] = hatch.PatternScale; changed = true; }
                            break;
                    }
                }
                if (changed) DrawingStore.SaveEasement(_db, _tr, r);
                if (!set) continue;
                Note("Info", "VIEWPORT", r.Title + ": hatch scaled to print its lines at the profile's spacing at 1\" = " +
                     _scale.ToString("0.##", CultureInfo.InvariantCulture) + "' (the easement itself is unchanged).");
                foreach (var other in exhibits.Where(x => x.Id != _exhibit.Id && Math.Abs(x.Scale - _scale) > 1e-9 && x.Sources.Any(s => s.EasementId == r.Id)))
                    Note("Warning", "VIEWPORT", r.Title + " is also on " + other.LayoutName + " at 1\" = " + other.Scale.ToString("0.##", CultureInfo.InvariantCulture) +
                         "'; its hatch is one object, so it now prints at this exhibit's spacing there too.");
            }
        }

        private static void SetPatternScale(Hatch hatch, double scale)
        {
            hatch.UpgradeOpen();
            hatch.PatternScale = scale;
            hatch.SetHatchPattern(hatch.PatternType, hatch.PatternName);
            hatch.EvaluateHatch(true);
        }

        private sealed class Relevance
        {
            public int Related;
            public int Unrelated;
            public int Other;
        }

        /// <summary>
        /// For "Relevant" layers: whether each holds, inside the viewport's view, something of the easements
        /// on this exhibit or one of their source objects (the parcel, trim lines, followed lines, POC point);
        /// and how much else it holds there.
        /// </summary>
        /// <summary>
        /// Annotative labels already in the drawing (lot names, plat names, APN tags) show in a viewport only at
        /// annotation scales they support. When some in view do not support this viewport's scale they are
        /// hidden on the sheet: say how many and at which scales they would show. The scale is not changed.
        /// </summary>
        private void HiddenAnnotation(Viewport vp)
        {
            AnnotationScale current;
            try { current = vp.AnnotationScale; }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return; }
            if (current == null) return;
            var halfW = vp.Width / 2 * _scale * _upf;
            var halfH = vp.Height / 2 * _scale * _upf;
            var corners = new[] { new P2(-halfW, -halfH), new P2(halfW, -halfH), new P2(halfW, halfH), new P2(-halfW, halfH) }
                .Select(c => ExhibitPlanner.Rotate(c, -_twist) + _viewCenter).ToList();
            double minX = corners.Min(c => c.X), minY = corners.Min(c => c.Y), maxX = corners.Max(c => c.X), maxY = corners.Max(c => c.Y);
            var frozen = new HashSet<ObjectId>(vp.GetFrozenLayers().Cast<ObjectId>());
            var scales = _db.ObjectContextManager.GetContextCollection("ACDB_ANNOTATIONSCALES");
            var hidden = 0;
            var elsewhere = new Dictionary<string, int>();
            var ms = (BlockTableRecord)_tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                var entity = _tr.GetObject(id, OpenMode.ForRead) as AcEntity;
                if (entity == null || entity.Annotative != AnnotativeStates.True || frozen.Contains(entity.LayerId)) continue;
                Point3d at;
                var mtext = entity as MText;
                var text = entity as DBText;
                if (mtext != null) at = mtext.Location;
                else if (text != null) at = text.Position;
                else continue;
                if (at.X < minX || at.X > maxX || at.Y < minY || at.Y > maxY) continue;
                bool supported;
                try { supported = entity.HasContext(current); }
                catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                if (supported) continue;
                hidden++;
                foreach (ObjectContext context in scales)
                {
                    bool has;
                    try { has = entity.HasContext(context); }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                    if (!has) continue;
                    int n;
                    elsewhere.TryGetValue(context.Name, out n);
                    elsewhere[context.Name] = n + 1;
                }
            }
            if (hidden == 0) return;
            var shownAt = elsewhere.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key).ToList();
            Note("Warning", "VIEWPORT", hidden + " annotative label(s) in view (lot, plat or APN text) do not show at " + current.Name +
                 (shownAt.Count > 0 ? "; they show at " + string.Join(", ", shownAt.ToArray()) : string.Empty) +
                 ". Choose that scale, or add " + current.Name + " to those labels, if they belong on the exhibit.");
        }

        private Dictionary<string, Relevance> LayerRelevance(IList<string> layers, Viewport vp)
        {
            var result = new Dictionary<string, Relevance>(StringComparer.OrdinalIgnoreCase);
            if (layers.Count == 0) return result;
            foreach (var l in layers) result[l] = new Relevance();
            var shown = new HashSet<string>(_records.Select(r => r.Id));
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in _records)
            {
                foreach (var s in (r.RouteSources ?? new List<GeometrySource>())) if (s != null && s.Handle != null) sources.Add(s.Handle);
                if (r.Parcel != null && r.Parcel.Handle != null) sources.Add(r.Parcel.Handle);
                foreach (var s in (r.LotLines ?? new List<GeometrySource>()).Concat(r.CommencementTieFollows ?? new List<GeometrySource>())) if (s.Handle != null) sources.Add(s.Handle);
                if (r.PointOfCommencement != null && r.PointOfCommencement.Handle != null) sources.Add(r.PointOfCommencement.Handle);
                if (r.TerminusTiePoint != null && r.TerminusTiePoint.Handle != null) sources.Add(r.TerminusTiePoint.Handle);
            }
            // The model area the viewport shows (a box around it when the view is turned).
            var halfW = vp.Width / 2 * _scale * _upf;
            var halfH = vp.Height / 2 * _scale * _upf;
            var corners = new[] { new P2(-halfW, -halfH), new P2(halfW, -halfH), new P2(halfW, halfH), new P2(-halfW, halfH) }
                .Select(c => ExhibitPlanner.Rotate(c, -_twist) + _viewCenter).ToList();
            double minX = corners.Min(c => c.X), minY = corners.Min(c => c.Y), maxX = corners.Max(c => c.X), maxY = corners.Max(c => c.Y);

            var ms = (BlockTableRecord)_tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                var entity = _tr.GetObject(id, OpenMode.ForRead) as AcEntity;
                Relevance counts;
                if (entity == null || !result.TryGetValue(entity.Layer, out counts)) continue;
                try
                {
                    var e = entity.GeometricExtents;
                    if (e.MaxPoint.X < minX || e.MinPoint.X > maxX || e.MaxPoint.Y < minY || e.MinPoint.Y > maxY) continue;
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                var stamp = Ownership.Read(entity);
                if (stamp != null && shown.Contains(stamp.PointNumber ?? string.Empty)) counts.Related++;
                else if (sources.Contains(entity.Handle.ToString())) counts.Related++;
                else if (stamp != null && stamp.Kind >= FtfEntityKind.EasementBoundary && stamp.Kind < FtfEntityKind.ExhibitViewport) counts.Unrelated++;
                else counts.Other++;
            }
            return result;
        }

        /// <summary>Everything the view should show: easement boundaries, holes, components, lots, POC and tie corners.</summary>
        private List<P2> ExtentPoints()
        {
            var points = new List<P2>();
            Action<IEnumerable<CourseData>> addCourses = courses =>
            {
                if (courses == null) return;
                foreach (var d in courses) { points.Add(d.Course.Start); points.Add(d.Course.PointAt(d.Course.Length / 2)); points.Add(d.Course.End); }
            };
            foreach (var r in _records)
            {
                addCourses(r.BoundaryCourses);
                foreach (var h in r.Holes ?? new List<List<CourseData>>()) addCourses(h);
                foreach (var c in r.Components ?? new List<EasementComponent>()) addCourses(c.BoundaryCourses);
                if (r.PointOfCommencement != null) points.Add(r.PointOfCommencement.Point);
                addCourses(r.TieCourses());
                if (r.TerminusTiePoint != null) points.Add(r.TerminusTiePoint.Point);
                if (r.IsPortion && r.Parcel != null)
                {
                    string problem;
                    var courses = EasementAreaCommands.LotCourses(_db, _tr, r, _settings.Easements.ToleranceFt * _upf, out problem);
                    if (courses != null) foreach (var c in courses) { points.Add(c.Start); points.Add(c.End); }
                }
            }
            return points;
        }

        // ================================================================ sheet

        private void Sheet()
        {
            var th = _xs.TextHeightIn;
            if (_xs.DrawBorder && string.IsNullOrWhiteSpace(_xs.TitleBlockPath) && string.IsNullOrWhiteSpace(_xs.TitleBlockName) && string.IsNullOrWhiteSpace(_xs.TemplateLayout))
            {
                var m = _xs.MarginIn;
                var own = _xs.BorderRightIn > _xs.BorderLeftIn && _xs.BorderTopIn > _xs.BorderBottomIn;
                double left = own ? _xs.BorderLeftIn : m, bottom = own ? _xs.BorderBottomIn : m;
                double right = own ? _xs.BorderRightIn : _xs.SheetWidthIn - m, top = own ? _xs.BorderTopIn : _xs.SheetHeightIn - m;
                var border = new Polyline();
                border.AddVertexAt(0, new Point2d(left, bottom), 0, 0, 0);
                border.AddVertexAt(1, new Point2d(right, bottom), 0, 0, 0);
                border.AddVertexAt(2, new Point2d(right, top), 0, 0, 0);
                border.AddVertexAt(3, new Point2d(left, top), 0, 0, 0);
                border.Closed = true;
                Add(border, "BORDER", "BORDER", FtfEntityKind.ExhibitBorder, _xs.BorderLayer, null, true, border.GetPoint3dAt(0));
            }
            if (!string.IsNullOrWhiteSpace(_xs.TitleBlockPath)) TitleBlock();
            else if (!string.IsNullOrWhiteSpace(_xs.TitleBlockName))
            {
                var defId = BlockDef(_xs.TitleBlockName, "BORDER");
                if (!defId.IsNull)
                {
                    var at = Position("BORDER", new Point3d(_xs.TitleBlockX, _xs.TitleBlockY, 0));
                    InsertSheetBlock(defId, at, "BORDER", "TITLEBLOCK", _xs.BorderLayer, true, _xs.TitleBlockName);
                }
            }
            foreach (var b in _xs.SheetBlockList())
            {
                var defId = BlockDef(b.Name, "BLOCK:" + b.Name);
                if (defId.IsNull) continue;
                var at = Position("BLOCK:" + b.Name, new Point3d(b.X, b.Y, 0));
                var inserted = InsertSheetBlock(defId, at, "BLOCK:" + b.Name, "SYMBOL", string.IsNullOrWhiteSpace(b.Layer) ? _xs.SymbolLayer : b.Layer, true, b.Name, b.Scale);
                if (inserted != null)
                    foreach (var p in b.Properties) SetDynamic(inserted, p.Key, p.Value, "BLOCK:" + b.Name);
            }

            if (_xs.DrawTitle)
            {
                var lines = _xs.TitleLines.Split('|').Select(l => _exhibit.Info.Fill(l)).Where(l => l.Length > 0).ToList();
                if (lines.Count > 0)
                {
                    var escaped = lines.Select(EasementCommands.Escape).ToList();
                    // Office titles often set "EXHIBIT B" larger than the lines under it.
                    var contents = Math.Abs(_xs.TitleFirstLineScale - 1) > 1e-9 && escaped.Count > 1
                        ? "{\\H" + _xs.TitleFirstLineScale.ToString("0.#####", CultureInfo.InvariantCulture) + "x;" + escaped[0] + "\\P}" + string.Join("\\P", escaped.Skip(1).ToArray())
                        : string.Join("\\P", escaped.ToArray());
                    var width = _xs.TitleWidthIn > 0 ? _xs.TitleWidthIn : 2 * Math.Min(_xs.TitleX - _xs.MarginIn, _xs.SheetWidthIn - _xs.MarginIn - _xs.TitleX) - 0.2;
                    var titleStyle = string.IsNullOrWhiteSpace(_xs.TitleTextStyle) ? ObjectId.Null : Setup.DrawingResources.FindTextStyle(_db, _tr, _xs.TitleTextStyle);
                    if (!string.IsNullOrWhiteSpace(_xs.TitleTextStyle) && titleStyle.IsNull) Note("Warning", "TITLE", "Title text style \"" + _xs.TitleTextStyle + "\" is not in this drawing; the text style is used.");
                    Text("TITLE", "TITLE", contents, new Point3d(_xs.TitleX, _xs.TitleY, 0), _xs.TitleTextHeightIn, AttachmentPoint.TopCenter, 0,
                         string.IsNullOrWhiteSpace(_xs.TitleLayer) ? _xs.AnnotationLayer : _xs.TitleLayer, true, "Title", width, false, titleStyle);
                }
            }
            var info = _xs.InfoLines.Split('|').Select(l => _exhibit.Info.Fill(l)).Where(l => l.Length > 0 && !l.EndsWith(":", StringComparison.Ordinal)).ToList();
            if (info.Count > 0)
                Text("INFO", "INFO", string.Join("\\P", info.Select(EasementCommands.Escape).ToArray()), new Point3d(_xs.InfoX, _xs.InfoY, 0), th * 0.9,
                     AttachmentPoint.TopLeft, 0, _xs.AnnotationLayer, true, "Exhibit information", _xs.SheetWidthIn - _xs.MarginIn - _xs.InfoX - 0.1);
            var notes = ExhibitSettings.Split(_xs.Notes.Replace("\r", string.Empty).Replace("\n", "|"));
            if (notes.Count > 0)
                Text("NOTES", "NOTES", string.Join("\\P", notes.Select(EasementCommands.Escape).ToArray()), new Point3d(_xs.NotesX, _xs.NotesY, 0), th * 0.9,
                     AttachmentPoint.TopLeft, 0, _xs.AnnotationLayer, true, "Standard notes",
                     (_xs.NotesX < _xs.InfoX ? _xs.InfoX : _xs.SheetWidthIn - _xs.MarginIn) - _xs.NotesX - 0.1);

            _exhibit.NorthArrowVerified = false;
            if (_xs.DrawNorthArrow) NorthArrow();
            if (_xs.DrawScaleBar) ScaleBar();
            if (_xs.DrawLegend) Legend();
            StampPlace();
        }

        /// <summary>
        /// The surveyor's stamp: a marked place for it, or the stamp block the surveyor chose (FTFEXHIBITSTAMP).
        /// FTF never picks a surveyor or a seal.
        /// </summary>
        private void StampPlace()
        {
            var mode = (_xs.StampMode ?? ExhibitSettings.StampNone).Trim();
            if (string.Equals(mode, ExhibitSettings.StampNone, StringComparison.OrdinalIgnoreCase)) return;
            var at = Position("STAMP", new Point3d(_xs.StampX, _xs.StampY, 0));
            var layer = string.IsNullOrWhiteSpace(_xs.StampLayer) ? _xs.SymbolLayer : _xs.StampLayer;
            if (string.Equals(mode, ExhibitSettings.StampBlock, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_exhibit.StampBlock))
            {
                var chosen = BlockDef(_exhibit.StampBlock, "STAMP", _xs.StampLibrary);
                if (!chosen.IsNull)
                {
                    InsertSheetBlock(chosen, at, "STAMP", "STAMP", layer, true, _exhibit.StampBlock);
                    Note("Info", "STAMP", "Stamp block \"" + _exhibit.StampBlock + "\" placed as chosen with FTFEXHIBITSTAMP; the surveyor is responsible for the stamp.");
                    return;
                }
            }
            if (!string.IsNullOrWhiteSpace(_xs.StampPlaceholderBlock))
            {
                var placeholder = BlockDef(_xs.StampPlaceholderBlock, "STAMP", _xs.StampLibrary);
                if (!placeholder.IsNull)
                {
                    InsertSheetBlock(placeholder, at, "STAMP", "STAMP", layer, true, _xs.StampPlaceholderBlock);
                    Note("Info", "STAMP", "Stamp placeholder only -- the surveyor places the stamp (FTFEXHIBITSTAMP).");
                    return;
                }
            }
            var size = _xs.StampSizeIn > 0 ? _xs.StampSizeIn : 1.5;
            var th = _xs.TextHeightIn;
            var defId = Definition("FTF_STAMP_PLACEHOLDER_" + ((int)Math.Round(size * 100)).ToString(CultureInfo.InvariantCulture), append =>
            {
                append(new Circle(Point3d.Origin, Vector3d.ZAxis, size / 2));
                var text = new MText { Contents = "SURVEYOR'S\\PSTAMP", TextHeight = th, Attachment = AttachmentPoint.MiddleCenter, Location = Point3d.Origin };
                if (!_textStyle.IsNull) text.TextStyleId = _textStyle;
                append(text);
            });
            Add(new BlockReference(at, defId), "STAMP", "STAMP", FtfEntityKind.ExhibitSymbol, layer, null, true, at);
            Note("Info", "STAMP", "Stamp placeholder only -- the surveyor places the stamp (FTFEXHIBITSTAMP).");
        }

        private void TitleBlock()
        {
            if (!File.Exists(_xs.TitleBlockPath)) { Note("Warning", "BORDER", "Title block drawing \"" + _xs.TitleBlockPath + "\" was not found."); return; }
            try
            {
                var blockName = "FTF_TITLEBLOCK_" + new string(Path.GetFileNameWithoutExtension(_xs.TitleBlockPath).Where(char.IsLetterOrDigit).ToArray());
                var blocks = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
                ObjectId defId;
                if (blocks.Has(blockName)) defId = blocks[blockName];
                else
                    using (var source = new Database(false, true))
                    {
                        source.ReadDwgFile(_xs.TitleBlockPath, FileShare.Read, true, string.Empty);
                        defId = _db.Insert(blockName, source, true);
                    }
                var at = Position("BORDER", new Point3d(0, 0, 0));
                var reference = new BlockReference(at, defId);
                _space.AppendEntity(reference);
                _tr.AddNewlyCreatedDBObject(reference, true);
                var def = (BlockTableRecord)_tr.GetObject(defId, OpenMode.ForRead);
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "TITLE", _exhibit.Info.Title }, { "LOCATION", _exhibit.Info.Location }, { "PROJECT", _exhibit.Info.Project }, { "PARCEL", _exhibit.Info.Parcel },
                    { "OWNER", _exhibit.Info.Owner }, { "APN", _exhibit.Info.Apn }, { "COUNTY", _exhibit.Info.County }, { "PURPOSE", _exhibit.Info.Purpose },
                    { "SHEET", _exhibit.Info.Sheet }, { "PREPAREDBY", _exhibit.Info.PreparedBy }, { "DATE", _exhibit.Info.Date }
                };
                foreach (ObjectId id in def)
                {
                    var attribute = _tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;
                    if (attribute == null || attribute.Constant) continue;
                    var attRef = new AttributeReference();
                    attRef.SetAttributeFromBlock(attribute, reference.BlockTransform);
                    string value;
                    if (values.TryGetValue(attribute.Tag, out value) && !string.IsNullOrWhiteSpace(value)) attRef.TextString = value.ToUpperInvariant();
                    reference.AttributeCollection.AppendAttribute(attRef);
                    _tr.AddNewlyCreatedDBObject(attRef, true);
                }
                reference.LayerId = ProductionLayers.Get(_db, _tr, _xs.BorderLayer, _settings);
                Stamp(reference, "BORDER", "TITLEBLOCK", FtfEntityKind.ExhibitBorder, null, true, at);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                Note("Warning", "BORDER", "The title block could not be inserted: " + ex.Message);
            }
        }

        // =============================================================== symbols

        private void NorthArrow()
        {
            var size = _xs.NorthArrowSizeIn;
            ObjectId defId;
            if (!string.IsNullOrWhiteSpace(_xs.NorthArrowBlock) && ((BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead)).Has(_xs.NorthArrowBlock))
                defId = ((BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead))[_xs.NorthArrowBlock];
            else
            {
                if (!string.IsNullOrWhiteSpace(_xs.NorthArrowBlock)) Note("Warning", "NORTH", "North arrow block \"" + _xs.NorthArrowBlock + "\" is not in this drawing; a simple arrow is drawn.");
                defId = Definition("FTF_NORTH_ARROW_" + ((int)Math.Round(size * 100)).ToString(CultureInfo.InvariantCulture), append =>
                {
                    var tip = new Point2d(0, size / 2);
                    var arrow = new Polyline();
                    arrow.AddVertexAt(0, tip, 0, 0, 0);
                    arrow.AddVertexAt(1, new Point2d(size * 0.22, -size / 4), 0, 0, 0);
                    arrow.AddVertexAt(2, new Point2d(0, -size * 0.08), 0, 0, 0);
                    arrow.AddVertexAt(3, new Point2d(-size * 0.22, -size / 4), 0, 0, 0);
                    arrow.Closed = true;
                    append(arrow);
                    append(new Solid(new Point3d(0, size / 2, 0), new Point3d(-size * 0.22, -size / 4, 0), new Point3d(0, -size * 0.08, 0)));
                    var n = new MText { Contents = "N", TextHeight = size * 0.3, Attachment = AttachmentPoint.TopCenter, Location = new Point3d(0, -size * 0.3, 0) };
                    if (!_textStyle.IsNull) n.TextStyleId = _textStyle;
                    append(n);
                });
            }
            var at = Position("NORTH", new Point3d(_xs.NorthArrowX, _xs.NorthArrowY, 0));
            var reference = new BlockReference(at, defId) { Rotation = _twist * Math.PI / 180 };
            Add(reference, "NORTH", "SYMBOL", FtfEntityKind.ExhibitSymbol, _xs.SymbolLayer, null, true, at);
            // The whole block turns with the view: north on the sheet is the view's turn from up.
            _exhibit.NorthArrowVerified = !reference.ObjectId.IsNull && Math.Abs(NorthArrowCheck.Normalize(reference.Rotation * 180 / Math.PI - _twist)) <= 0.1;
        }

        /// <summary>
        /// Turns the north arrow inside a scale bar block by its dynamic rotation property, then checks the result from
        /// the block's own geometry: the parts that moved must have turned by the view's turn. Either direction of the
        /// property is tried; nothing is claimed unless the geometry shows it.
        /// </summary>
        private bool TurnNorthArrow(BlockReference reference, string property)
        {
            DynamicBlockReferenceProperty p = null;
            if (reference.IsDynamicBlock)
                foreach (DynamicBlockReferenceProperty candidate in reference.DynamicBlockReferencePropertyCollection)
                    if (string.Equals(candidate.PropertyName, property, StringComparison.OrdinalIgnoreCase) && !candidate.ReadOnly) { p = candidate; break; }
            if (p == null) return false;
            try
            {
                p.Value = 0.0;
                var before = BlockPoints(reference);
                foreach (var sign in new[] { 1.0, -1.0 })
                {
                    p.Value = sign * _twist * Math.PI / 180;
                    var measured = NorthArrowCheck.MeasuredTurn(before, BlockPoints(reference));
                    if (NorthArrowCheck.Matches(measured, _twist)) return true;
                }
                p.Value = 0.0;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
            catch (InvalidCastException) { }
            return false;
        }

        /// <summary>Points of a block reference's visible geometry on the sheet, in a stable order.</summary>
        private static List<P2> BlockPoints(BlockReference reference)
        {
            var points = new List<P2>();
            var parts = new DBObjectCollection();
            try { reference.Explode(parts); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return points; }
            foreach (DBObject o in parts)
            {
                using (o)
                {
                    var line = o as Line;
                    if (line != null) { points.Add(new P2(line.StartPoint.X, line.StartPoint.Y)); points.Add(new P2(line.EndPoint.X, line.EndPoint.Y)); continue; }
                    var pl = o as Polyline;
                    if (pl != null) { for (var i = 0; i < pl.NumberOfVertices; i++) { var v = pl.GetPoint3dAt(i); points.Add(new P2(v.X, v.Y)); } continue; }
                    var solid = o as Solid;
                    if (solid != null) { for (short i = 0; i < 4; i++) { var v = solid.GetPointAt(i); points.Add(new P2(v.X, v.Y)); } continue; }
                    var circle = o as Circle;
                    if (circle != null) { points.Add(new P2(circle.Center.X, circle.Center.Y)); continue; }
                    var arc = o as Arc;
                    if (arc != null) { points.Add(new P2(arc.StartPoint.X, arc.StartPoint.Y)); points.Add(new P2(arc.EndPoint.X, arc.EndPoint.Y)); continue; }
                }
            }
            return points;
        }

        private void ScaleBar()
        {
            if (!string.IsNullOrWhiteSpace(_xs.ScaleBarBlock))
            {
                var barDef = BlockDef(_xs.ScaleBarBlock, "SCALEBAR");
                if (!barDef.IsNull)
                {
                    var barAt = Position("SCALEBAR", new Point3d(_xs.ScaleBarX, _xs.ScaleBarY, 0));
                    var state = (_xs.ScaleBarVisibility ?? string.Empty).Replace("{scale}", _scale.ToString("0.##", CultureInfo.InvariantCulture));
                    var reference = InsertSheetBlock(barDef, barAt, "SCALEBAR", "SYMBOL", _xs.SymbolLayer, true, state.Length > 0 ? state : _xs.ScaleBarBlock);
                    if (reference != null && state.Length > 0) SetVisibility(reference, state);
                    if (reference != null && Math.Abs(_twist) <= 1e-9) _exhibit.NorthArrowVerified = true;       // north up: the block's arrow points up
                    else if (reference != null)
                    {
                        var turned = !string.IsNullOrWhiteSpace(_xs.NorthArrowProperty) && TurnNorthArrow(reference, _xs.NorthArrowProperty);
                        _exhibit.NorthArrowVerified = turned;
                        if (turned)
                            Note("Info", "SCALEBAR", "The view is turned " + _twist.ToString("0.#", CultureInfo.InvariantCulture) + " degrees; the north arrow in block \"" + _xs.ScaleBarBlock +
                                 "\" was turned to match and checked from the block's geometry.");
                        else
                            Note("Error", "SCALEBAR", "The view is turned " + _twist.ToString("0.#", CultureInfo.InvariantCulture) + " degrees and the north arrow in block \"" + _xs.ScaleBarBlock + "\" could not be " +
                                 (string.IsNullOrWhiteSpace(_xs.NorthArrowProperty) ? "turned (the profile names no rotation property for it)" : "turned and checked with its \"" + _xs.NorthArrowProperty + "\" property") +
                                 ", so it does not point north. Turn it by hand, use a north-up view, or set the profile's north arrow property.");
                    }
                    return;
                }
            }
            var marks = ExhibitPlanner.ScaleBar(_scale, _xs.ScaleBarLengthIn);
            var th = _xs.TextHeightIn;
            var name = "FTF_SCALEBAR_" + _scale.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', '_') + "_" + ((int)Math.Round(_xs.ScaleBarLengthIn * 100)).ToString(CultureInfo.InvariantCulture) +
                       "_" + ((int)Math.Round(th * 1000)).ToString(CultureInfo.InvariantCulture);
            var scaleText = _xs.ScaleTextFormat.Replace("{scale}", _scale.ToString("0.##", CultureInfo.InvariantCulture));
            var defId = Definition(name, append =>
            {
                var h = th * 0.9;
                for (var i = 0; i < marks.Count - 1; i++)
                {
                    double x0 = marks[i].Value, x1 = marks[i + 1].Value;
                    var box = new Polyline();
                    box.AddVertexAt(0, new Point2d(x0, 0), 0, 0, 0);
                    box.AddVertexAt(1, new Point2d(x1, 0), 0, 0, 0);
                    box.AddVertexAt(2, new Point2d(x1, h), 0, 0, 0);
                    box.AddVertexAt(3, new Point2d(x0, h), 0, 0, 0);
                    box.Closed = true;
                    append(box);
                    if (i % 2 == 0) append(new Solid(new Point3d(x0, 0, 0), new Point3d(x1, 0, 0), new Point3d(x0, h, 0), new Point3d(x1, h, 0)));
                }
                foreach (var m in new[] { marks[0], marks[marks.Count / 2], marks[marks.Count - 1] })
                {
                    var label = new MText { Contents = m.Key.ToString("0.##", CultureInfo.InvariantCulture), TextHeight = th * 0.85, Attachment = AttachmentPoint.BottomCenter, Location = new Point3d(m.Value, h + th * 0.3, 0) };
                    if (!_textStyle.IsNull) label.TextStyleId = _textStyle;
                    append(label);
                }
                var text = new MText { Contents = EasementCommands.Escape(scaleText), TextHeight = th * 0.85, Attachment = AttachmentPoint.TopCenter, Location = new Point3d(marks[marks.Count - 1].Value / 2, -th * 0.4, 0) };
                if (!_textStyle.IsNull) text.TextStyleId = _textStyle;
                append(text);
            });
            var at = Position("SCALEBAR", new Point3d(_xs.ScaleBarX, _xs.ScaleBarY, 0));
            Add(new BlockReference(at, defId), "SCALEBAR", "SYMBOL", FtfEntityKind.ExhibitSymbol, _xs.SymbolLayer, scaleText, true, at);
        }

        /// <summary>Sets one dynamic property of a sheet block by name; says so when the block has no such property or value.</summary>
        private void SetDynamic(BlockReference reference, string property, string value, string key)
        {
            if (!reference.IsDynamicBlock) { Note("Warning", key, "Block \"" + reference.Name + "\" is not a dynamic block; " + property + " = " + value + " was not set."); return; }
            foreach (DynamicBlockReferenceProperty p in reference.DynamicBlockReferencePropertyCollection)
            {
                if (!string.Equals(p.PropertyName, property, StringComparison.OrdinalIgnoreCase) || p.ReadOnly) continue;
                var allowed = p.GetAllowedValues();
                var match = allowed == null || allowed.Length == 0 ? null : allowed.FirstOrDefault(a => string.Equals(Convert.ToString(a, CultureInfo.InvariantCulture), value, StringComparison.OrdinalIgnoreCase));
                if (match == null) { Note("Warning", key, "Block property " + property + " has no value \"" + value + "\"; it was left as the block defines it."); return; }
                p.Value = match;
                return;
            }
            Note("Warning", key, "The block has no property \"" + property + "\"; it was left as the block defines it.");
        }

        /// <summary>Picks the dynamic block visibility state named for the exhibit scale; says so when the block has none.</summary>
        private void SetVisibility(BlockReference reference, string state)
        {
            if (!reference.IsDynamicBlock) { Note("Warning", "SCALEBAR", "Block \"" + _xs.ScaleBarBlock + "\" is not a dynamic block; its scale cannot be set to " + state + "."); return; }
            foreach (DynamicBlockReferenceProperty p in reference.DynamicBlockReferencePropertyCollection)
            {
                if (p.ReadOnly) continue;
                var allowed = p.GetAllowedValues();
                if (allowed == null || allowed.Length == 0) continue;
                var match = allowed.FirstOrDefault(a => string.Equals(Convert.ToString(a, CultureInfo.InvariantCulture), state, StringComparison.OrdinalIgnoreCase));
                if (match == null) continue;
                p.Value = match;
                return;
            }
            Note("Error", "SCALEBAR", "Block \"" + _xs.ScaleBarBlock + "\" has no visibility state \"" + state + "\"; the scale shown on the sheet is wrong until it is set by hand.");
        }

        private void Legend()
        {
            var es = _settings.Easements;
            var th = _xs.TextHeightIn;
            var entries = new List<Tuple<string, string, string>>();     // text, hatch pattern, layer
            foreach (var r in _records)
            {
                var layer = r.IsArea ? es.AreaLayer : r.IsTemporary ? es.TemporaryLayer : es.BoundaryLayer;
                var pattern = r.IsArea ? es.AreaHatchPattern : r.IsTemporary ? es.TemporaryHatchPattern : (es.DrawHatch ? es.HatchPattern : string.Empty);
                var name = r.IsArea ? EasementAnnotation.AreaTitle(r.Purpose, es) : (r.IsTemporary ? "TEMPORARY " : r.GroupId != null ? "PERMANENT " : string.Empty) + (r.IsTemporary ? es.TemporaryPurpose.Replace("TEMPORARY ", string.Empty) : r.Purpose) + " EASEMENT";
                var text = (string.IsNullOrWhiteSpace(_xs.LegendFormat) ? "{name}" : _xs.LegendFormat)
                    .Replace("{name}", name).Replace("{sqft}", r.DisplayAreaSquareFeet.ToString("N0", CultureInfo.InvariantCulture));
                if (entries.All(e => e.Item1 != text)) entries.Add(Tuple.Create(text, pattern, layer));
            }
            if (entries.Count == 0) return;
            var key = "FTF_LEGEND_" + _exhibit.Id;
            var blocks = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
            if (blocks.Has(key))
            {
                // Redefined on each build so it follows the easements shown.
                var old = (BlockTableRecord)_tr.GetObject(blocks[key], OpenMode.ForWrite);
                foreach (ObjectId id in old) { var e = _tr.GetObject(id, OpenMode.ForWrite); e.Erase(); }
            }
            var defId = Definition(key, append =>
            {
                var title = new MText { Contents = "LEGEND", TextHeight = th, Attachment = AttachmentPoint.BottomLeft, Location = new Point3d(0, 0, 0) };
                if (!_textStyle.IsNull) title.TextStyleId = _textStyle;
                append(title);
                var y = -th * 0.6;
                foreach (var entry in entries)
                {
                    var swatch = new Polyline();
                    swatch.AddVertexAt(0, new Point2d(0, y - th * 1.4), 0, 0, 0);
                    swatch.AddVertexAt(1, new Point2d(th * 3.5, y - th * 1.4), 0, 0, 0);
                    swatch.AddVertexAt(2, new Point2d(th * 3.5, y), 0, 0, 0);
                    swatch.AddVertexAt(3, new Point2d(0, y), 0, 0, 0);
                    swatch.Closed = true;
                    swatch.LayerId = ProductionLayers.Get(_db, _tr, entry.Item3, _settings);
                    append(swatch);
                    if (!string.IsNullOrWhiteSpace(entry.Item2))
                    {
                        try
                        {
                            var hatch = new Hatch();
                            hatch.SetDatabaseDefaults(_db);
                            append(hatch);
                            hatch.LayerId = swatch.LayerId;
                            hatch.PatternScale = es.HatchScale * 0.1;
                            hatch.SetHatchPattern(HatchPatternType.PreDefined, entry.Item2);
                            hatch.AppendLoop(HatchLoopTypes.External, new ObjectIdCollection { swatch.ObjectId });
                            hatch.EvaluateHatch(true);
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception) { }
                    }
                    var label = new MText { Contents = EasementCommands.Escape(entry.Item1), TextHeight = th * 0.9, Attachment = AttachmentPoint.MiddleLeft, Location = new Point3d(th * 4.2, y - th * 0.7, 0) };
                    if (!_textStyle.IsNull) label.TextStyleId = _textStyle;
                    append(label);
                    y -= th * 2.1;
                }
            }, true);
            var at = Position("LEGEND", new Point3d(_xs.LegendX, _xs.LegendY, 0));
            Add(new BlockReference(at, defId), "LEGEND", "SYMBOL", FtfEntityKind.ExhibitSymbol, _xs.SymbolLayer, string.Join("|", entries.Select(e => e.Item1).ToArray()), true, at);
        }

        /// <summary>A block definition made once (or refilled when <paramref name="refill"/>).</summary>
        private ObjectId Definition(string name, Action<Action<AcEntity>> fill, bool refill = false)
        {
            var blocks = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
            if (blocks.Has(name) && !refill) return blocks[name];
            BlockTableRecord def;
            if (blocks.Has(name)) def = (BlockTableRecord)_tr.GetObject(blocks[name], OpenMode.ForWrite);
            else
            {
                def = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
                blocks.UpgradeOpen();
                blocks.Add(def);
                _tr.AddNewlyCreatedDBObject(def, true);
            }
            fill(entity =>
            {
                def.AppendEntity(entity);
                _tr.AddNewlyCreatedDBObject(entity, true);
            });
            return def.ObjectId;
        }

        // ============================================================ easements

        private readonly List<CourseData> _tableRows = new List<CourseData>();
        private readonly List<Tuple<P2, P2>> _paperLines = new List<Tuple<P2, P2>>();

        private void Easements()
        {
            var es = _settings.Easements;
            var modelTextHeight = _xs.TextHeightIn * _scale * _upf;
            var lines = 0;
            var curves = 0;
            foreach (var r in _records)
                foreach (var loop in OutlinesOf(r))
                    foreach (var d in loop) PaperSegments(d.Course);

            foreach (var r in _records)
            {
                if (_xs.DrawCourseLabels && es.LabelMode != EasementLabelMode.None)
                {
                    List<CourseLabel> plan;
                    if (r.IsPortion) plan = new List<CourseLabel>();
                    else if (r.IsArea) plan = EasementAnnotation.PlanLabels(r.TieCourses(), (r.RouteCourses ?? new List<CourseData>()).Select(d => d.Course), null, es, modelTextHeight, "%%d");
                    else if (r.IsTemporary) plan = new List<CourseLabel>();        // its centerline is labelled with the easement it belongs to
                    else plan = EasementAnnotation.PlanLabels(r.TieCourses(), (r.RouteCourses ?? new List<CourseData>()).Select(d => d.Course), r.TerminusTie, es, modelTextHeight, "%%d");

                    var n = 0;
                    var outerLeft = r.Width != null ? Math.Max(r.Width.Left, _records.Where(x => x.GroupId != null && x.GroupId == r.GroupId && x.Width != null).Select(x => x.Width.Left).DefaultIfEmpty(0).Max()) : 0;
                    var outward = r.IsArea && r.RouteCourses != null && r.RouteCourses.Count > 0 && Loops.SignedArea(r.RouteCourses.Select(x => x.Course).ToList()) > 0 ? -1.0 : 1.0;
                    foreach (var label in plan)
                    {
                        n++;
                        if (label.InTable)
                        {
                            if (!_xs.DrawLineCurveTable)
                                _notPlaced.Add("Course \"" + string.Join(" ", label.Lines.ToArray()) + "\" of " + r.Title + " does not fit on its line at 1\" = " + _scale + "' and the line table is turned off.");
                            label.Data.Id = label.Data.Course.Kind == CourseKind.Line ? es.LinePrefix + (++lines) : es.CurvePrefix + (++curves);
                            _tableRows.Add(label.Data);
                        }
                        CourseLabel(r, label, n, outerLeft, outward);
                    }
                    PointLeaders(r, es);
                }
                if (_xs.DrawWidthDimensions) Dimensions(r);
            }
            // Title/area labels last, so they can go where no line, course label or leader is.
            if (_xs.DrawEasementLabels)
                foreach (var r in _records) AreaLabel(r, es);
            if (_xs.DrawParcelLabel && (!string.IsNullOrWhiteSpace(_exhibit.Info.Apn) || !string.IsNullOrWhiteSpace(_exhibit.Info.Parcel)))
                ParcelLabel();
        }

        private static IEnumerable<IList<CourseData>> OutlinesOf(EasementRecord r)
        {
            return new[] { r.BoundaryCourses }.Concat(r.Holes ?? new List<List<CourseData>>())
                .Concat((r.Components ?? new List<EasementComponent>()).Select(c => c.BoundaryCourses))
                .Where(loop => loop != null).Cast<IList<CourseData>>();
        }

        private static P2? Centroid(IList<P2> polygon)
        {
            double a = 0, cx = 0, cy = 0;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var cross = P2.Cross(polygon[j], polygon[i]);
                a += cross;
                cx += (polygon[j].X + polygon[i].X) * cross;
                cy += (polygon[j].Y + polygon[i].Y) * cross;
            }
            if (Math.Abs(a) < 1e-12) return null;
            return new P2(cx / (3 * a), cy / (3 * a));
        }

        private List<P2> PaperPolygon(IEnumerable<CourseData> loop)
        {
            var points = new List<P2>();
            foreach (var d in loop)
            {
                var c = d.Course;
                var steps = c.Kind == CourseKind.Arc ? Math.Max(2, (int)Math.Ceiling(c.Sweep / (Math.PI / 18))) : 1;
                for (var i = 0; i < steps; i++) points.Add(Paper(c.PointAt(c.Length * i / steps)));
            }
            return points;
        }

        /// <summary>Sheet boxes of the annotation placed in the viewport so far.</summary>
        private List<SheetRect> TakenInViewport(bool withSheetItems = false, string exceptKey = null, bool reserveTable = true)
        {
            var taken = new List<SheetRect>();
            foreach (var item in _exhibit.Items.Where(i => i.Key != exceptKey && (i.Kind == "LABEL" || i.Kind == "LEADER" || i.Kind == "DIMENSION" || i.Kind == "AREALABEL" ||
                                                           (withSheetItems && i.Kind != "VIEWPORT" && i.Kind != "BORDER" && i.Kind != "TITLEBLOCK"))))
            {
                var entity = EasementCommands.Resolve(_db, _tr, item.Handle);
                if (entity == null) continue;
                try
                {
                    var e = entity.GeometricExtents;
                    taken.Add(new SheetRect(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y));
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { }
            }
            if (withSheetItems && reserveTable && _xs.DrawLineCurveTable && _tableRows.Count > 0)
            {
                // Where the line/curve table will go once the rows are known.
                var rows = _tableRows.Count + 4;
                taken.Add(new SheetRect(_xs.LineTableX, _xs.LineTableY - rows * _xs.TextHeightIn * 2.4, _xs.LineTableX + 40 * _xs.TextHeightIn, _xs.LineTableY));
            }
            taken.AddRange(_modelText);
            return taken;
        }

        private void PaperSegments(Course c)
        {
            var steps = c.Kind == CourseKind.Arc ? Math.Max(2, (int)Math.Ceiling(c.Sweep / (Math.PI / 18))) : 1;
            for (var i = 0; i < steps; i++)
                _paperLines.Add(Tuple.Create(Paper(c.PointAt(c.Length * i / steps)), Paper(c.PointAt(c.Length * (i + 1) / steps))));
        }

        private P2 Paper(P2 model)
        {
            return ExhibitPlanner.ToPaper(model, _viewCenter, _scale, _twist, _vpCenter, _upf);
        }

        private double PaperAngle(P2 modelDirection)
        {
            var d = ExhibitPlanner.Rotate(modelDirection, _twist);
            return EasementCommands.Readable(Math.Atan2(d.Y, d.X));
        }

        private void AreaLabel(EasementRecord r, EasementSettings es)
        {
            if (r.BoundaryCourses == null || r.BoundaryCourses.Count == 0) return;
            var region = new RegionShape(r.BoundaryCourses.Select(d => d.Course));
            foreach (var h in r.Holes ?? new List<List<CourseData>>()) region.Holes.Add(h.Select(d => d.Course).ToList());
            var inside = StripTrim.PointInside(region.Outer, es.ToleranceFt * _upf);
            var lines = new List<string>();
            if (_xs.AreaLabelTitle) lines.Add(r.Title);
            // The profile words easement areas; a construction or acquisition area keeps its own area wording (it is not always an easement).
            var worded = r.IsArea ? null : _xs.AreaLine(r.IsTemporary ? es.TemporaryPurpose : r.Purpose, r.Title, r.DisplayAreaSquareFeet);
            if (worded != null) lines.Add(worded);
            else lines.AddRange(r.IsArea ? new List<string> { EasementAnnotation.AreaOfAreaLine(r.DisplayAreaSquareFeet, es, r.Purpose) } : EasementAnnotation.AreaLines(r.DisplayAreaSquareFeet, es, r.Purpose));
            lines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (lines.Count == 0) return;
            P2? leaderTo = null;
            var rotation = 0.0;
            var route = r.RouteCourses ?? new List<CourseData>();
            if (!r.IsArea && route.Count > 0)
            {
                var longest = route.OrderByDescending(d => d.Length).First().Course;
                rotation = PaperAngle(longest.DirectionAt(longest.Length / 2));
            }
            var at = Paper(inside);
            var th = _xs.TextHeightIn;
            var width = lines.Max(l => l.Length) * th * 0.95 + th;
            var height = lines.Count * th * 1.7 + th * 0.5;
            if (r.IsArea || route.Count == 0)
            {
                // Somewhere inside the area, off its lines (and any hole) and clear of the other labels,
                // as near the middle of the area as that allows.
                var outline = PaperPolygon(r.BoundaryCourses);
                var spot = ExhibitReview.ClearSpot(outline, (r.Holes ?? new List<List<CourseData>>()).Select(h => (IList<P2>)PaperPolygon(h)).ToList(),
                                                   _viewport, _paperLines, TakenInViewport(), width, height, Centroid(outline) ?? at, 40, 1.5);
                if (spot.HasValue) at = spot.Value;
            }
            else if (r.Width != null && !_xs.StripLabelAlong)
            {
                // Office practice on strips: the area reads horizontally beside the strip, off its lines.
                var toPaper = 1.0 / (_scale * _upf);
                var group = _records.Where(x => x.Width != null && (x.Id == r.Id || (x.GroupId != null && x.GroupId == r.GroupId))).ToList();
                var half = Math.Max(group.Max(x => x.Width.Right), group.Max(x => x.Width.Left)) * toPaper;
                var candidates = new List<Tuple<P2, double>>();
                var anchors = new List<P2>();
                foreach (var d in route.OrderByDescending(x => x.Length).Take(3))
                    foreach (var t in new[] { 0.5, 0.3, 0.7, 0.15, 0.85 })
                        foreach (var right in new[] { true, false })
                        {
                            var c = d.Course;
                            var dir = ExhibitPlanner.Rotate(c.DirectionAt(c.Length * t), _twist);
                            var normal = right ? new P2(dir.Y, -dir.X) : new P2(-dir.Y, dir.X);
                            var distance = half + Math.Abs(normal.X) * width / 2 + Math.Abs(normal.Y) * height / 2 + th * 1.2;
                            var mid = Paper(c.PointAt(c.Length * t));
                            candidates.Add(Tuple.Create(new P2(mid.X + normal.X * distance, mid.Y + normal.Y * distance), 0.0));
                            anchors.Add(mid);
                        }
                int conflicts;
                var pick = ExhibitReview.FewestConflicts(candidates, width, height, _viewport, _paperLines, TakenInViewport(true), out conflicts);
                at = candidates[pick].Item1;
                rotation = 0;
                if (string.Equals(_xs.NarrowStripLabel, "Leader", StringComparison.OrdinalIgnoreCase) || (string.Equals(_xs.NarrowStripLabel, "Ask", StringComparison.OrdinalIgnoreCase) && StripLabelLeader(r)))
                    leaderTo = anchors[pick];
            }
            else if (r.Width != null)
            {
                // A strip too narrow on paper for its label gets it alongside: the right side first (the
                // course labels are on the left), clear of the lines and the other labels.
                var toPaper = 1.0 / (_scale * _upf);
                if (height > (r.Width.Left + r.Width.Right) * toPaper * 0.9)
                {
                    var group = _records.Where(x => x.Width != null && (x.Id == r.Id || (x.GroupId != null && x.GroupId == r.GroupId))).ToList();
                    var rightIn = group.Max(x => x.Width.Right) * toPaper;
                    var leftIn = group.Max(x => x.Width.Left) * toPaper + th * (0.9 * 2 + 0.4) + th * 1.5;
                    var longest = route.OrderByDescending(d => d.Length).First().Course;
                    var candidates = new List<Tuple<P2, double>>();
                    foreach (var t in new[] { 0.5, 0.35, 0.65, 0.2, 0.8 })
                        foreach (var right in new[] { true, false })
                        {
                            var dir = longest.DirectionAt(longest.Length * t);
                            var normal = ExhibitPlanner.Rotate(right ? new P2(dir.Y, -dir.X) : dir.LeftNormal(), _twist);
                            var distance = (right ? rightIn : leftIn) + height / 2 + th * 0.6;
                            var mid = Paper(longest.PointAt(longest.Length * t));
                            candidates.Add(Tuple.Create(new P2(mid.X + normal.X * distance, mid.Y + normal.Y * distance), rotation));
                        }
                    if (StripLabelLeader(r))
                    {
                        // With a leader the label can stand further off, where it is clearer, and points back at the strip.
                        foreach (var t in new[] { 0.5, 0.35, 0.65 })
                            foreach (var right in new[] { true, false })
                            {
                                var dir = longest.DirectionAt(longest.Length * t);
                                var normal = ExhibitPlanner.Rotate(right ? new P2(dir.Y, -dir.X) : dir.LeftNormal(), _twist);
                                var distance = (right ? rightIn : leftIn) + height / 2 + th * 6;
                                var mid = Paper(longest.PointAt(longest.Length * t));
                                candidates.Add(Tuple.Create(new P2(mid.X + normal.X * distance, mid.Y + normal.Y * distance), 0.0));
                            }
                        var leaderCandidates = candidates.Skip(10).ToList();
                        int leaderConflicts;
                        at = leaderCandidates[ExhibitReview.FewestConflicts(leaderCandidates, width, height, _viewport, _paperLines, TakenInViewport(), out leaderConflicts)].Item1;
                        rotation = 0;
                        leaderTo = Paper(longest.PointAt(longest.Length / 2));
                    }
                    else
                    {
                        int conflicts;
                        at = candidates[ExhibitReview.FewestConflicts(candidates, width, height, _viewport, _paperLines, TakenInViewport(), out conflicts)].Item1;
                    }
                }
            }
            var contents = string.Join("\\P", lines.Select(EasementCommands.Escape).ToArray());
            if (leaderTo.HasValue)
                LabelLeader("AREA:" + r.Id, contents, new Point3d(at.X, at.Y, 0), leaderTo.Value);
            else
                Text("AREA:" + r.Id, "AREALABEL", contents, new Point3d(at.X, at.Y, 0), _xs.TextHeightIn,
                     AttachmentPoint.MiddleCenter, rotation, _xs.AnnotationLayer, false, "Title/area label for " + r.Title, 0, true);
        }

        /// <summary>Whether a narrow strip's label gets a leader: the profile's choice, or the drafter's when it says Ask (remembered for rebuilds).</summary>
        private bool StripLabelLeader(EasementRecord r)
        {
            var mode = _xs.NarrowStripLabel ?? "NoLeader";
            if (string.Equals(mode, "Leader", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.Equals(mode, "Ask", StringComparison.OrdinalIgnoreCase)) return false;
            if (_exhibit.LabelLeaders == null) _exhibit.LabelLeaders = new Dictionary<string, bool>();
            bool known;
            if (_exhibit.LabelLeaders.TryGetValue(r.Id, out known)) return known;
            var question = r.Title + " is too narrow at 1\" = " + _scale.ToString("0.##", CultureInfo.InvariantCulture) + "' for its label inside it. Put the label beside it with a leader?";
            bool answer;
            if (EasementInspectCommands.Headless())
            {
                var k = EasementCommands.Keyword(_ed, "\n" + question + " [Leader/NoLeader] <NoLeader>: ", "NoLeader", "Leader", "NoLeader");
                answer = k == "Leader";
            }
            else answer = AskLeader(question);
            _exhibit.LabelLeaders[r.Id] = answer;
            return answer;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool AskLeader(string question)
        {
            return System.Windows.Forms.MessageBox.Show(question, "FTF exhibit", System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question) == System.Windows.Forms.DialogResult.Yes;
        }

        /// <summary>A title/area label with a leader to the easement.</summary>
        private void LabelLeader(string key, string contents, Point3d textAt, P2 arrowAt)
        {
            var position = Position(key, textAt);
            var entity = new MLeader();
            entity.SetDatabaseDefaults(_db);
            var style = LeaderStyleId();
            if (!style.IsNull) entity.MLeaderStyle = style;
            entity.ContentType = ContentType.MTextContent;
            using (var mtext = new MText())
            {
                mtext.SetDatabaseDefaults(_db);
                if (!_textStyle.IsNull) mtext.TextStyleId = _textStyle;
                mtext.Contents = contents;
                mtext.TextHeight = _xs.TextHeightIn;
                mtext.Location = position;
                entity.MText = mtext;
            }
            entity.TextLocation = position;
            entity.AddLeaderLine(new Point3d(arrowAt.X, arrowAt.Y, 0));
            Add(entity, key, "AREALABEL", FtfEntityKind.ExhibitLabel, _xs.AnnotationLayer, entity.MText.Contents, false, position);
        }

        /// <summary>The profile's multileader style, or the drawing's current one.</summary>
        private ObjectId LeaderStyleId()
        {
            if (!string.IsNullOrWhiteSpace(_xs.LeaderStyle))
            {
                var styles = (DBDictionary)_tr.GetObject(_db.MLeaderStyleDictionaryId, OpenMode.ForRead);
                if (styles.Contains(_xs.LeaderStyle)) return styles.GetAt(_xs.LeaderStyle);
                Note("Warning", null, "Multileader style \"" + _xs.LeaderStyle + "\" is not in this drawing; the current style is used.");
            }
            return _db.MLeaderstyle;
        }

        private void CourseLabel(EasementRecord r, CourseLabel label, int n, double outerLeft, double outward)
        {
            var c = label.Data.Course;
            var mid = c.PointAt(c.Length / 2);
            var dir = c.DirectionAt(c.Length / 2);
            var text = label.InTable ? label.Data.Id : string.Join("\\P", label.Lines.Select(EasementCommands.Escape).ToArray());
            var lineCount = label.InTable ? 1 : label.Lines.Count;
            // On paper: beside the line, clear of the easement.
            double offsetModel;
            if (r.IsArea) offsetModel = outward * 0;
            else offsetModel = label.IsTie ? 0 : outerLeft;
            var basePoint = mid + dir.LeftNormal() * (r.IsArea && !label.IsTie ? 0 : offsetModel);
            var paper = Paper(basePoint);
            var normal = ExhibitPlanner.Rotate(dir.LeftNormal() * (r.IsArea && !label.IsTie ? outward : 1), _twist);
            var offsetIn = _xs.TextHeightIn * (0.9 * lineCount + 0.4);
            var at = new P2(paper.X + normal.X * offsetIn, paper.Y + normal.Y * offsetIn);
            if (!label.InTable)
            {
                // The other side of its line when that side is plainly clearer (a parcel label, a leader or another label
                // in the way); otherwise the usual side. Strips go beyond their other sideline.
                var otherBase = r.IsArea || label.IsTie ? mid : mid - dir.LeftNormal() * OuterRight(r);
                var otherPaper = Paper(otherBase);
                var other = new P2(otherPaper.X - normal.X * offsetIn, otherPaper.Y - normal.Y * offsetIn);
                var th = _xs.TextHeightIn;
                var longest = label.Lines.Max(l => l.Length) * th * 0.8 + th;
                var tall = lineCount * th * 1.7;
                var angle = PaperAngle(dir);
                var w = Math.Abs(longest * Math.Cos(angle)) + Math.Abs(tall * Math.Sin(angle));
                var h = Math.Abs(longest * Math.Sin(angle)) + Math.Abs(tall * Math.Cos(angle));
                var candidates = new List<Tuple<P2, double>> { Tuple.Create(at, 0.0), Tuple.Create(other, 0.0) };
                int conflicts;
                var taken = TakenInViewport(true);
                var pick = ExhibitReview.FewestConflicts(candidates, w, h, _viewport, _modelLines, taken, out conflicts);
                if (pick == 1) at = other;
            }
            Text("LABEL:" + r.Id + ":" + n, "LABEL", text, new Point3d(at.X, at.Y, 0), _xs.TextHeightIn, AttachmentPoint.MiddleCenter, PaperAngle(dir), _xs.AnnotationLayer, false,
                 "Course label " + text.Replace("\\P", " ").Replace("%%d", "°"));
        }

        private double OuterRight(EasementRecord r)
        {
            if (r.Width == null) return 0;
            return Math.Max(r.Width.Right, _records.Where(x => x.GroupId != null && x.GroupId == r.GroupId && x.Width != null).Select(x => x.Width.Right).DefaultIfEmpty(0).Max());
        }

        private void PointLeaders(EasementRecord r, EasementSettings es)
        {
            if (!_xs.DrawPointLabels) return;
            var route = r.RouteCourses ?? new List<CourseData>();
            if (route.Count == 0) return;
            var first = route[0].Course;
            var last = route[route.Count - 1].Course;
            var reach = 0.9;     // inches
            Action<string, string, P2, P2> leader = (key, text, model, awayModel) =>
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                var anchor = Paper(model);
                var preferred = ExhibitPlanner.Rotate(awayModel.Normalized(), _twist);
                // The preferred direction first, then the others around it: the one that crosses the fewest
                // lines, labels and sheet furniture and stays in the viewport wins.
                var th = _xs.TextHeightIn;
                var textLines = text.Split('\n');
                var w = textLines.Max(l => l.Length) * th * 0.8 + th;
                var h = textLines.Length * th * 1.7;
                var taken = TakenInViewport(true);
                var candidates = new List<Tuple<P2, double>>();
                var landings = new List<P2>();
                var lefts = new List<bool>();
                foreach (var r2 in new[] { reach, reach * 1.5, reach * 2.2 })
                    foreach (var turn in new[] { 0, 45, -45, 90, -90, 135, -135, 180 })
                    {
                        var dir = ExhibitPlanner.Rotate(preferred, turn);
                        var at = new P2(anchor.X + dir.X * r2, anchor.Y + dir.Y * r2);
                        landings.Add(at);
                        lefts.Add(dir.X < 0);
                        var center = new P2(at.X + (dir.X >= 0 ? w / 2 : -w / 2), at.Y - h / 2 + th * 0.3);
                        candidates.Add(Tuple.Create(center, 0.0));
                    }
                // Leader text never sits on an easement's own hatched area when a spot outside one exists.
                var areas = _records.Where(x => x.BoundaryCourses != null && x.BoundaryCourses.Count > 2).Select(x => (IList<P2>)PaperPolygon(x.BoundaryCourses)).ToList();
                Func<Tuple<P2, double>, bool> onArea = cand => areas.Any(poly => new[]
                    {
                        cand.Item1, new P2(cand.Item1.X - w / 2, cand.Item1.Y - h / 2), new P2(cand.Item1.X + w / 2, cand.Item1.Y - h / 2),
                        new P2(cand.Item1.X + w / 2, cand.Item1.Y + h / 2), new P2(cand.Item1.X - w / 2, cand.Item1.Y + h / 2)
                    }.Any(p => ExhibitReview.InPolygon(poly, p)));
                var outside = Enumerable.Range(0, candidates.Count).Where(i => !onArea(candidates[i])).ToList();
                var pool = outside.Count > 0 ? outside : Enumerable.Range(0, candidates.Count).ToList();
                int conflicts;
                var pick = ExhibitReview.FewestConflicts(pool.Select(i => candidates[i]).ToList(), w, h, _viewport, _paperLines.Concat(_modelLines).ToList(), taken, out conflicts);
                var best = pool[pick];
                // The leader's text runs to the right of its location: text on the left of the point ends at the landing.
                var textAt = new Point3d(landings[best].X - (lefts[best] ? w : 0), landings[best].Y, 0);
                var position = Position(key, textAt);
                var entity = new MLeader();
                entity.SetDatabaseDefaults(_db);
                var leaderStyle = LeaderStyleId();
                if (!leaderStyle.IsNull) entity.MLeaderStyle = leaderStyle;
                entity.ContentType = ContentType.MTextContent;
                using (var mtext = new MText())
                {
                    mtext.SetDatabaseDefaults(_db);
                    if (!_textStyle.IsNull) mtext.TextStyleId = _textStyle;
                    mtext.Contents = EasementCommands.Escape(text);
                    mtext.TextHeight = _xs.TextHeightIn;
                    mtext.Location = position;
                    entity.MText = mtext;
                }
                entity.TextLocation = position;
                entity.AddLeaderLine(new Point3d(anchor.X, anchor.Y, 0));
                Add(entity, key, "LEADER", FtfEntityKind.ExhibitLabel, _xs.AnnotationLayer, entity.MText.Contents, false, position);
            };
            var startRight = new P2(first.StartDirection.Y, -first.StartDirection.X);
            if (r.IsArea)
            {
                // Out of the area, away from its middle.
                var corners = (r.BoundaryCourses ?? route).Select(d => d.Course.Start).ToList();
                var middle = new P2(corners.Average(p => p.X), corners.Average(p => p.Y));
                var away = first.Start - middle;
                leader("POINT:" + r.Id + ":POB", es.BeginningLabel, first.Start,
                       away.Length > 1e-9 ? new P2(Math.Sign(away.X), Math.Sign(away.Y) * 0.6) : startRight - first.StartDirection);
            }
            else if (!r.IsTemporary)
            {
                // Back from the start, on the side away from the commencement tie so the two leaders do not cross.
                var startLeft = first.StartDirection.LeftNormal();
                var pob = startRight - first.StartDirection * 0.5;
                if (r.PointOfCommencement != null && P2.Dot(r.PointOfCommencement.Point - first.Start, startRight) > 0)
                    pob = startLeft - first.StartDirection * 0.5;
                leader("POINT:" + r.Id + ":POB", es.BeginningLabel, first.Start, pob);
                var endRight = new P2(last.EndDirection.Y, -last.EndDirection.X);
                leader("POINT:" + r.Id + ":TERMINUS", es.TerminusLabel, last.End, endRight + last.EndDirection * 0.5);
            }
            if (r.PointOfCommencement != null && !r.IsTemporary)
            {
                var poc = r.PointOfCommencement.Point;
                var tie = r.TieCourses();
                var away = Ties.Follows(tie) ? tie[0].Course.StartDirection * -1 : poc.DistanceTo(first.Start) > 1e-6 ? (poc - first.Start) : startRight;
                leader("POINT:" + r.Id + ":POC", es.CommencementLabel, poc, away.Normalized() + new P2(away.Normalized().Y, -away.Normalized().X) * 0.5);
            }
        }

        private void Dimensions(EasementRecord r)
        {
            var es = _settings.Easements;
            var tolerance = es.ToleranceFt * _upf;
            if (r.IsPortion && r.PortionSteps != null && r.BoundaryCourses != null)
            {
                var inside = StripTrim.PointInside(r.BoundaryCourses.Select(d => d.Course).ToList(), tolerance);
                var n = 0;
                foreach (var s in r.PortionSteps)
                {
                    var along = s.Line.StartDirection;
                    var foot = s.Line.Start + along * P2.Dot(inside - s.Line.Start, along);
                    var inward = P2.Dot(inside - foot, along.LeftNormal()) >= 0 ? along.LeftNormal() : along.LeftNormal() * -1;
                    Dimension("DIM:" + r.Id + ":" + (++n), foot, foot + inward * s.Distance, along);
                }
                return;
            }
            if (r.IsArea || r.Width == null || r.RouteCourses == null || r.RouteCourses.Count == 0 || r.BoundaryCourses == null) return;
            var centerline = r.RouteCourses.Select(d => d.Course).ToList();
            var boundary = r.BoundaryCourses.Select(d => d.Course).ToList();
            // Only where the dimension truly shows the width: at right angles between parallel sidelines, clear of
            // trimmed ends, tapers and bends. Of those, the one clearest of labels, leaders and drawing text.
            var fractions = new[] { 0.25, 0.5, 0.75, 0.125, 0.375, 0.625, 0.875 };
            var spots = WidthDimensions.Spots(centerline, boundary, r.Width.Left, r.Width.Right, tolerance, fractions);
            if (spots.Count == 0)
            {
                _notPlaced.Add("No place along " + r.Title + " shows its full width at right angles between parallel sidelines (it is trimmed, tapered or bent where one could go), " +
                               "so no width dimension is drawn -- add one by hand where the surveyor wants it.");
                return;
            }
            var th = _xs.TextHeightIn;
            var taken = TakenInViewport(true);
            var candidates = new List<Tuple<P2, double>>();
            var options = new List<Tuple<WidthSpot, double>>();
            foreach (var s in spots)
                foreach (var side in new[] { 1.0, -1.0 })
                {
                    var a = Paper(s.Right);
                    var b = Paper(s.Left);
                    var along = ExhibitPlanner.Rotate(s.Direction.Normalized(), _twist);
                    var offset = th * 3 * side;
                    var mid = new P2((a.X + b.X) / 2 + along.X * offset, (a.Y + b.Y) / 2 + along.Y * offset);
                    candidates.Add(Tuple.Create(mid, 0.0));
                    options.Add(Tuple.Create(s, side));
                }
            var sizeW = Math.Max(th * 5, spots.Max(s => Math.Abs(Paper(s.Left).X - Paper(s.Right).X)) + th * 2);
            var sizeH = Math.Max(th * 2, spots.Max(s => Math.Abs(Paper(s.Left).Y - Paper(s.Right).Y)) + th * 2);
            int conflicts;
            var best = ExhibitReview.FewestConflicts(candidates, sizeW, sizeH, _viewport, new List<Tuple<P2, P2>>(), taken, out conflicts);
            Dimension("DIM:" + r.Id + ":WIDTH", options[best].Item1.Right, options[best].Item1.Left, options[best].Item1.Direction, options[best].Item2);
        }

        private void Dimension(string key, P2 fromModel, P2 toModel, P2 alongModel, double side = 1)
        {
            var from = Paper(fromModel);
            var to = Paper(toModel);
            var along = ExhibitPlanner.Rotate(alongModel.Normalized(), _twist);
            var offset = _xs.TextHeightIn * 3 * side;
            var line = new P2(from.X + along.X * offset, from.Y + along.Y * offset);
            var style = _db.Dimstyle;
            if (!string.IsNullOrWhiteSpace(_xs.DimensionStyle))
            {
                var table = (DimStyleTable)_tr.GetObject(_db.DimStyleTableId, OpenMode.ForRead);
                if (table.Has(_xs.DimensionStyle)) style = table[_xs.DimensionStyle];
                else Note("Warning", key, "Dimension style \"" + _xs.DimensionStyle + "\" is not in this drawing; the current style is used.");
            }
            var dim = new AlignedDimension(new Point3d(from.X, from.Y, 0), new Point3d(to.X, to.Y, 0), new Point3d(line.X, line.Y, 0), string.Empty, style);
            dim.SetDatabaseDefaults(_db);
            dim.DimensionStyle = style;
            dim.Dimlfac = _scale;          // paper inches measure as model feet
            if (string.IsNullOrWhiteSpace(_xs.DimensionStyle))
            {
                // No office style named: survey feet to the profile's precision, sized for the sheet.
                var th = _xs.TextHeightIn;
                dim.Dimtxt = th;
                dim.Dimasz = th;
                dim.Dimgap = th * 0.4;
                dim.Dimexo = th * 0.6;
                dim.Dimexe = th * 0.6;
                dim.Dimdec = _settings.Easements.DistanceDecimals;
                dim.Dimzin = 0;
                dim.Dimpost = "<>'";
                dim.Dimtad = 1;
            }
            Add(dim, key, "DIMENSION", FtfEntityKind.ExhibitDimension, _xs.DimensionLayer, null, false, new Point3d(line.X, line.Y, 0));
        }

        private void ParcelLabel()
        {
            string lotProblem;
            var lotRecord = _records.FirstOrDefault(r => r.IsPortion && r.Parcel != null);
            var courses = lotRecord == null ? null : EasementAreaCommands.LotCourses(_db, _tr, lotRecord, _settings.Easements.ToleranceFt * _upf, out lotProblem);
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(_exhibit.Info.Apn)) lines.Add("APN " + _exhibit.Info.Apn.ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(_exhibit.Info.Owner)) lines.Add(_exhibit.Info.Owner.ToUpperInvariant());
            if (lines.Count == 0) return;
            Point3d at;
            if (courses != null)
            {
                // Inside the lot where nothing else is, as near its middle as that allows.
                var inside = StripTrim.PointInside(courses, 0.01);
                var paper = Paper(inside);
                var th = _xs.TextHeightIn;
                var w = lines.Max(l => l.Length) * th * 0.95 + th;
                var h = lines.Count * th * 1.7 + th * 0.5;
                var outline = PaperPolygon(courses.Select(EasementAnnotation.Describe));
                var holes = _records.Where(r => r.BoundaryCourses != null).Select(r => (IList<P2>)PaperPolygon(r.BoundaryCourses)).ToList();
                var spot = ExhibitReview.ClearSpot(outline, holes, _viewport, _paperLines, TakenInViewport(true), w, h, Centroid(outline) ?? paper, 40, 0);
                at = spot.HasValue ? new Point3d(spot.Value.X, spot.Value.Y, 0) : new Point3d(paper.X, paper.Y - th * 3, 0);
            }
            else at = new Point3d(_viewport.MaxX - 1.5, _viewport.MaxY - 0.6, 0);
            Text("PARCEL", "LABEL", string.Join("\\P", lines.Select(EasementCommands.Escape).ToArray()), at, _xs.TextHeightIn, AttachmentPoint.MiddleCenter, 0, _xs.AnnotationLayer, false, "Parcel label");
        }

        // ================================================================ tables

        private void Tables()
        {
            var es = _settings.Easements;
            var th = _xs.TextHeightIn;
            if (ExhibitPlanner.WantsAreaTable(_records, _xs))
            {
                var rows = ExhibitPlanner.AreaRows(_records, _xs);
                var cells = rows.Select(r => new[]
                {
                    r.Label,
                    r.SquareFeet.ToString("N0", CultureInfo.InvariantCulture) + " SF" + (_xs.AreaTableAcres ? " / " + (r.SquareFeet / EasementAnnotation.SquareFeetPerAcre).ToString("0.000", CultureInfo.InvariantCulture) + " AC" : string.Empty)
                }).ToList();
                Table("AREATABLE", "AREA SUMMARY", new[] { "EASEMENT", "AREA" }, new[] { 26.0, 21.0 }, cells, new Point3d(_xs.AreaTableX, _xs.AreaTableY, 0));
                if (rows.Any(r => r.Combined))
                    Note("Info", "AREATABLE", "The area table's \"" + _xs.CombinedAreaLabel + "\" row is the sum of the easements shown; it is not a legal total unless a surveyor says so.");
            }
            if (_xs.DrawLineCurveTable && _tableRows.Count > 0)
            {
                // Columns and their order are the profile's: "LINE NO.={id}|LENGTH={distance}|DIRECTION={bearing}".
                Func<CourseData, string, string> cell = (d, token) =>
                {
                    switch (token)
                    {
                        case "{id}": return d.Id;
                        case "{distance}":
                        case "{length}": return EasementAnnotation.Distance(d.Length, es);
                        case "{bearing}": return EasementAnnotation.Bearing(d.AzimuthDegrees, es, "°");
                        case "{radius}": return EasementAnnotation.Distance(d.Radius ?? 0, es);
                        case "{delta}": return EasementAnnotation.Angle(d.DeltaDegrees ?? 0, es, "°");
                        default: return token;
                    }
                };
                Func<string, double> widthOf = token => token == "{id}" ? 8.0 : token == "{bearing}" ? 17.0 : token == "{delta}" ? 14.0 : 11.0;
                var lineColumns = _xs.LineColumns();
                var curveColumns = ExhibitSettings.Columns(_xs.CurveTableColumns);
                if (curveColumns.Count == 0) curveColumns = ExhibitSettings.Columns(new ExhibitSettings().CurveTableColumns);
                var lineRows = _tableRows.Where(d => d.Course.Kind == CourseKind.Line).Select(d => lineColumns.Select(c => cell(d, c.Value)).ToArray()).ToList();
                var curveRows = _tableRows.Where(d => d.Course.Kind == CourseKind.Arc).Select(d => curveColumns.Select(c => cell(d, c.Value)).ToArray()).ToList();
                var at = new Point3d(_xs.LineTableX, _xs.LineTableY, 0);
                if (lineRows.Count > 0)
                    at = Table("LINETABLE", _xs.LineTableTitle, lineColumns.Select(c => c.Key).ToArray(), lineColumns.Select(c => widthOf(c.Value)).ToArray(), lineRows, at);
                if (_xs.CurveTableX > 0 || _xs.CurveTableY > 0) at = new Point3d(_xs.CurveTableX, _xs.CurveTableY, 0);
                if (curveRows.Count > 0)
                    Table("CURVETABLE", _xs.CurveTableTitle, curveColumns.Select(c => c.Key).ToArray(), curveColumns.Select(c => widthOf(c.Value)).ToArray(), curveRows, at);
            }
            foreach (var key in new[] { "LINETABLE", "CURVETABLE" }) TableSpot(key);
            foreach (var key in new[] { "AREATABLE", "LINETABLE", "CURVETABLE" }) ClearBelow(key);
        }

        /// <summary>
        /// A line/curve table that runs into the logo, stamp or other sheet furniture, or off the printable area, moves
        /// to the first of the profile's other table places that is plainly clear. When none is, it stays and the
        /// review says so. A table the drafter moved by hand is never moved.
        /// </summary>
        private void TableSpot(string key)
        {
            if (_userPositions.ContainsKey(key)) return;
            var item = _exhibit.Items.FirstOrDefault(i => i.Key == key);
            var entity = item == null ? null : EasementCommands.Resolve(_db, _tr, item.Handle);
            var box = Box(entity);
            if (!box.HasValue) return;
            var printable = new SheetRect(_xs.MarginIn, _xs.MarginIn, _xs.SheetWidthIn - _xs.MarginIn, _xs.SheetHeightIn - _xs.MarginIn);
            var furniture = _exhibit.Items.Where(i => i.Key != key && i.Kind != "VIEWPORT" && i.Kind != "BORDER" && i.Kind != "TITLEBLOCK" && (i.Kind == "SYMBOL" || i.Kind == "STAMP" || i.Kind == "TABLE" || i.Kind == "TITLE" || i.Kind == "INFO" || i.Kind == "NOTES"))
                                       .Select(i => Box(EasementCommands.Resolve(_db, _tr, i.Handle))).Where(b => b.HasValue).Select(b => b.Value).ToList();
            Func<SheetRect, bool> clear = r => r.Within(printable, 0.01) && !furniture.Any(f => f.MaxX > r.MinX + 0.01 && f.MinX < r.MaxX - 0.01 && f.MaxY > r.MinY + 0.01 && f.MinY < r.MaxY - 0.01) &&
                                               !(r.MaxX > _viewport.MinX + 0.01 && r.MinX < _viewport.MaxX - 0.01 && r.MaxY > _viewport.MinY + 0.01 && r.MinY < _viewport.MaxY - 0.01);
            var here = box.Value;
            var table = entity as Table;
            if (table == null) return;
            // Inside the viewport a table is on the plan: it is clear only where no linework, easement line, label,
            // leader or drawing text runs under it.
            var taken = TakenInViewport(true, key, false);
            Func<SheetRect, int> onPlan = r =>
                _paperLines.Concat(_modelLines).Count(l => ExhibitReview.SegmentHitsRect(l.Item1, l.Item2, r)) +
                taken.Count(t => t.MaxX > r.MinX && t.MinX < r.MaxX && t.MaxY > r.MinY && t.MinY < r.MaxY);
            Func<SheetRect, bool> overlapsViewport = r => r.MaxX > _viewport.MinX + 0.01 && r.MinX < _viewport.MaxX - 0.01 && r.MaxY > _viewport.MinY + 0.01 && r.MinY < _viewport.MaxY - 0.01;
            Func<SheetRect, bool> fine = r => overlapsViewport(r)
                ? r.Within(printable, 0.01) && r.Within(_viewport, 0.01) && onPlan(r) == 0 && !furniture.Any(f => f.MaxX > r.MinX + 0.01 && f.MinX < r.MaxX - 0.01 && f.MaxY > r.MinY + 0.01 && f.MinY < r.MaxY - 0.01)
                : clear(r);
            if (fine(here)) return;
            var name = key == "LINETABLE" ? "The line table" : "The curve table";
            Action<double, double, string> move = (dx, dy, why) =>
            {
                table.UpgradeOpen();
                table.TransformBy(Matrix3d.Displacement(new Vector3d(dx, dy, 0)));
                item.X = table.Position.X;
                item.Y = table.Position.Y;
                Note("Info", key, name + " " + why);
            };
            foreach (var spot in _xs.TableSpotList())
            {
                var dx = spot.Key - table.Position.X;
                var dy = spot.Value - table.Position.Y;
                if (!fine(new SheetRect(here.MinX + dx, here.MinY + dy, here.MaxX + dx, here.MaxY + dy))) continue;
                move(dx, dy, "would run into sheet furniture or the plan at its usual place; it was moved to the profile's other table place at " +
                     spot.Key.ToString("0.##", CultureInfo.InvariantCulture) + "," + spot.Value.ToString("0.##", CultureInfo.InvariantCulture) + ".");
                return;
            }
            // Plainly empty plan inside the viewport: the nearest spot with nothing at all under the table. No such spot, no move.
            var w = here.MaxX - here.MinX;
            var h = here.MaxY - here.MinY;
            Tuple<double, double> best = null;
            var bestDistance = double.MaxValue;
            for (var x = _viewport.MinX + 0.05; x + w <= _viewport.MaxX - 0.05; x += 0.2)
                for (var y = _viewport.MinY + 0.05; y + h <= _viewport.MaxY - 0.05; y += 0.2)
                {
                    var r = new SheetRect(x, y, x + w, y + h);
                    var distance = Math.Abs(x - here.MinX) + Math.Abs(y - here.MinY);
                    if (distance >= bestDistance || !fine(r)) continue;
                    best = Tuple.Create(x - here.MinX, y - here.MinY);
                    bestDistance = distance;
                }
            if (best != null) move(best.Item1, best.Item2, overlapsViewport(here)
                ? "was on linework or labels at its usual place; it was moved to empty plan space nearby."
                : "ran into sheet furniture at its usual place and no other table place was clear; it was moved to empty plan space in the viewport.");
        }

        /// <summary>
        /// A table grows with its rows, so the sheet furniture hanging under it in the same column
        /// (scale bar, north arrow, legend, information, notes) moves down once to clear it. Anything
        /// the drafter placed by hand stays put; whatever then leaves the sheet is in the review.
        /// </summary>
        private void ClearBelow(string tableKey)
        {
            var tableItem = _exhibit.Items.FirstOrDefault(i => i.Key == tableKey);
            var table = tableItem == null ? null : Box(EasementCommands.Resolve(_db, _tr, tableItem.Handle));
            if (!table.HasValue) return;
            var t = table.Value;
            var gap = _xs.TextHeightIn * 1.5;
            var hanging = new List<Tuple<ExhibitItem, AcEntity, SheetRect>>();
            foreach (var key in new[] { "SCALEBAR", "NORTH", "LEGEND", "INFO", "NOTES" })
            {
                if (_userPositions.ContainsKey(key) || _keptEdited.Contains(key)) continue;
                var item = _exhibit.Items.FirstOrDefault(i => i.Key == key);
                var entity = item == null ? null : EasementCommands.Resolve(_db, _tr, item.Handle);
                var box = Box(entity);
                if (!box.HasValue) continue;
                var b = box.Value;
                if (b.MaxX > t.MinX && b.MinX < t.MaxX && b.MaxY <= t.MaxY) hanging.Add(Tuple.Create(item, entity, b));
            }
            var shift = hanging.Select(h => t.MinY - gap - h.Item3.MaxY).DefaultIfEmpty(0).Min();
            if (shift >= 0) return;
            foreach (var h in hanging)
            {
                h.Item2.UpgradeOpen();
                h.Item2.TransformBy(Matrix3d.Displacement(new Vector3d(0, shift, 0)));
                var p = ExhibitCommands.PositionOf(h.Item2);
                if (p.HasValue) { h.Item1.X = p.Value.X; h.Item1.Y = p.Value.Y; }
            }
        }

        private static SheetRect? Box(AcEntity entity)
        {
            if (entity == null) return null;
            try
            {
                var e = entity.GeometricExtents;
                return new SheetRect(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
        }

        /// <summary>A table at its profile position (or where the drafter moved it); returns the point below it.</summary>
        private Point3d Table(string key, string title, string[] headers, double[] widths, IList<string[]> rows, Point3d at)
        {
            var th = _xs.TextHeightIn;
            var position = Position(key, at);
            var table = new Table();
            table.SetDatabaseDefaults(_db);
            if (!string.IsNullOrWhiteSpace(_xs.TableStyle))
            {
                var styles = (DBDictionary)_tr.GetObject(_db.TableStyleDictionaryId, OpenMode.ForRead);
                if (styles.Contains(_xs.TableStyle)) table.TableStyle = styles.GetAt(_xs.TableStyle);
                else Note("Warning", key, "Table style \"" + _xs.TableStyle + "\" is not in this drawing; the current style is used.");
            }
            table.Position = position;
            table.SetSize(rows.Count + 2, headers.Length);
            table.MergeCells(CellRange.Create(table, 0, 0, 0, headers.Length - 1));
            Cell(table, 0, 0, title, th, _textStyle);
            var columnScale = _xs.TableColumnScale > 0 ? _xs.TableColumnScale : 1;
            for (var c = 0; c < headers.Length; c++) { table.Columns[c].Width = widths[c] * th * columnScale; Cell(table, 1, c, headers[c], th, _textStyle); }
            for (var r = 0; r < rows.Count; r++)
                for (var c = 0; c < headers.Length; c++) Cell(table, r + 2, c, rows[r][c], th, _textStyle);
            for (var r = 0; r < rows.Count + 2; r++) table.Rows[r].Height = th * 2.0;
            table.GenerateLayout();
            Add(table, key, "TABLE", FtfEntityKind.ExhibitTable, _xs.TableLayer, null, true, position);
            // The text FTF wrote is read back from the table itself, so a later hand edit is detectable.
            var added = _exhibit.Items.FirstOrDefault(i => i.Key == key);
            if (added != null && !table.IsDisposed && !table.ObjectId.IsNull && added.Handle == table.Handle.ToString()) added.Text = ExhibitCommands.TextOf(table);
            return new Point3d(at.X, at.Y - table.Height - th * 2, 0);
        }


        private static void Cell(Table table, int row, int column, string text, double height, ObjectId style)
        {
            var cell = table.Cells[row, column];
            if (!style.IsNull) cell.TextStyleId = style;
            cell.TextHeight = height;
            cell.TextString = (text ?? string.Empty).Replace("Δ", "\\U+0394").Replace("%%d", "°");
            cell.Alignment = CellAlignment.MiddleCenter;
        }

        // ================================================================ review

        private void Review()
        {
            foreach (var item in _exhibit.Items)
            {
                if (item.Kind == "VIEWPORT") continue;
                var entity = EasementCommands.Resolve(_db, _tr, item.Handle);
                if (entity == null) continue;
                Extents3d extents;
                try { extents = entity.GeometricExtents; }
                catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                var box = new SheetBox
                {
                    Key = item.Key, Kind = item.Kind, Description = BoxName(item),
                    Rect = new SheetRect(extents.MinPoint.X, extents.MinPoint.Y, extents.MaxPoint.X, extents.MaxPoint.Y)
                };
                var turned = entity as MText;
                if (turned != null && turned.Attachment == AttachmentPoint.MiddleCenter && Math.Abs(Math.Sin(2 * turned.Rotation)) > 1e-6)
                    box.Corners = ExhibitReview.TurnedBox(new P2(turned.Location.X, turned.Location.Y), turned.Rotation, turned.ActualWidth, turned.ActualHeight);
                _boxes.Add(box);
            }
            var printable = new SheetRect(_xs.MarginIn, _xs.MarginIn, _xs.SheetWidthIn - _xs.MarginIn, _xs.SheetHeightIn - _xs.MarginIn);
            var review = ExhibitReview.Check(_boxes, _viewport, printable, _paperLines, _notPlaced, _viewportFramePlots);
            _exhibit.Review.Clear();
            _exhibit.Review.AddRange(_notes);
            _exhibit.Review.AddRange(review);
        }

        private static string BoxName(ExhibitItem item)
        {
            switch (item.Key)
            {
                case "TITLE": return "The title";
                case "INFO": return "The exhibit information";
                case "NOTES": return "The notes";
                case "NORTH": return "The north arrow";
                case "SCALEBAR": return "The scale bar";
                case "LEGEND": return "The legend";
                case "AREATABLE": return "The area table";
                case "LINETABLE": return "The line table";
                case "CURVETABLE": return "The curve table";
                case "BORDER": return "The border";
                case "PARCEL": return "The parcel label";
                case "STAMP": return "The stamp place";
            }
            if (item.Key != null && item.Key.StartsWith("BLOCK:", StringComparison.Ordinal)) return "The " + item.Key.Substring(6) + " block";
            var text = (item.Text ?? item.Key).Replace("\\P", " ").Replace("%%d", "°");
            if (item.Kind == "LEADER") return "The \"" + text + "\" leader";
            if (item.Kind == "DIMENSION") return "A width dimension";
            if (item.Kind == "AREALABEL") return "The title/area label \"" + text + "\"";
            return "Label \"" + text + "\"";
        }

        // =============================================================== helpers

        private void Note(string severity, string key, string message)
        {
            if (_notes.All(n => n.Message != message)) _notes.Add(new ExhibitReviewItem { Severity = severity, ItemKey = key, Message = message });
        }

        /// <summary>Where an item goes: where the drafter moved it (for items that keep their place), else its default.</summary>
        private Point3d Position(string key, Point3d fallback)
        {
            Point3d user;
            return _userPositions.TryGetValue(key, out user) ? user : fallback;
        }

        private void Text(string key, string kind, string contents, Point3d at, double height, AttachmentPoint attachment, double rotation,
                          string layer, bool keepPosition, string description, double width = 0, bool mask = false, ObjectId style = default(ObjectId))
        {
            var position = Position(key, at);
            var text = new MText();
            text.SetDatabaseDefaults(_db);
            if (!style.IsNull) text.TextStyleId = style;
            else if (!_textStyle.IsNull) text.TextStyleId = _textStyle;
            text.TextHeight = height;
            text.Attachment = attachment;
            text.Location = position;
            text.Rotation = rotation;
            text.Contents = contents;
            if (width > 0) text.Width = width;           // wraps rather than running off the sheet
            if (mask)
            {
                // Reads over the hatch.
                text.BackgroundFill = true;
                text.UseBackgroundColor = true;
                text.BackgroundScaleFactor = 1.15;
            }
            Add(text, key, kind, kind == "LABEL" || kind == "AREALABEL" ? FtfEntityKind.ExhibitLabel : FtfEntityKind.ExhibitText, layer, contents, keepPosition, position);
        }

        /// <summary>
        /// Adds a generated item -- unless the drafter edited the previous one by hand, in which case
        /// the edit stays and the difference is listed for review.
        /// </summary>
        private void Add(AcEntity entity, string key, string kind, FtfEntityKind ftfKind, string layer, string text, bool keepPosition, Point3d position)
        {
            ExhibitItem old;
            if (_keptEdited.Contains(key) && _previous.TryGetValue(key, out old))
            {
                var current = EasementCommands.Resolve(_db, _tr, old.Handle);
                var now = current == null ? null : ExhibitCommands.TextOf(current);
                var fresh = text ?? ExhibitCommands.TextOf(entity);
                entity.Dispose();
                _exhibit.Items.Add(old);
                if (fresh != null && fresh != old.Text)
                    Note("Warning", key, "CONFLICT: " + Describe(old) + " was edited by hand to \"" + Plain(now) + "\"; the rebuild would now write \"" + Plain(fresh) + "\". The hand edit was kept -- update it or delete it and rebuild.");
                else
                    Note("Info", key, Describe(old) + " was edited by hand to \"" + Plain(now) + "\"; the hand edit was kept.");
                return;
            }
            if (_userErased.Contains(key))
            {
                // Erased by hand: respected.
                entity.Dispose();
                return;
            }
            entity.LayerId = ProductionLayers.Get(_db, _tr, layer, _settings);
            _space.AppendEntity(entity);
            _tr.AddNewlyCreatedDBObject(entity, true);
            Stamp(entity, key, kind, ftfKind, text, keepPosition, position);
        }

        private static string Plain(string s)
        {
            return (s ?? string.Empty).Replace("\\P", " / ").Replace("%%d", "°");
        }

        private void Stamp(AcEntity entity, string key, string kind, FtfEntityKind ftfKind, string text, bool keepPosition, Point3d position)
        {
            Ownership.Stamp(entity, _exhibit.Id, _version, ftfKind, null, key);
            var item = new ExhibitItem { Key = key, Kind = kind, Handle = entity.Handle.ToString(), Text = text, X = position.X, Y = position.Y, KeepPosition = keepPosition };
            var actual = ExhibitCommands.PositionOf(entity);
            if (actual.HasValue) { item.X = actual.Value.X; item.Y = actual.Value.Y; }
            _exhibit.Items.RemoveAll(i => i.Key == key);
            _exhibit.Items.Add(item);
        }
    }
}
