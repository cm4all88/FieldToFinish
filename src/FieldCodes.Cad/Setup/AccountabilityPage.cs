using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// Settings > Point Accountability: when the checks run, the tolerances, which symbol blocks are
    /// a code's own marker, and the linework continuity limits per feature family.
    /// </summary>
    [SettingsPage(Group = "FieldToFinish", Label = "Point Accountability", Order = 142, Sections = "accountability",
                  Commands = "FTFRUN,FTFLABELALL,FTFREVIEWCHECK,FTFQC,FTFREVIEW")]
    internal sealed class AccountabilityPage : SetupPage
    {
        private CheckBox _run;
        private TextBox _duplicate;
        private TextBox _surface;
        private CheckBox _verifySurface;
        private TextBox _controlCheck;
        private CheckBox _reasons;
        private CheckBox _report;
        private TextBox _symbols;
        private CheckBox _continuity;
        private TextBox _proposalLayer;
        private TextBox _onLine;
        private DataGridView _features;

        public override string Title { get { return "Point Accountability"; } }
        public override string AffectedCommands { get { return "FTFRUN, FTFLABELALL, FTFREVIEWCHECK, FTFQC, FTFREVIEW"; } }

        protected override void BuildBody()
        {
            Heading("Every point accounted for");
            Note("After processing, FTF accounts for every survey point against its feature's drafting standard " +
                 "(Settings > Point Features > Drafting Standard): an FTF label, tag, block or dripline; a drawn line of its " +
                 "feature through it; its symbol or its point style; its own Civil 3D label; surface data; a control or control " +
                 "check shot; the point alone where the standard draws nothing; or an exclusion with a documented reason. " +
                 "A missing or wrong deliverable is a required action, and Drafting QC Complete waits until each is drawn or " +
                 "closed with a reason; a possible problem is a warning, listed but never holding a point. The ledger keeps every " +
                 "point ever recorded, so a deleted point is reported. Nothing here writes a point or a survey line.");
            _run = CheckRow("Run after processing",
                "FTFRUN, the window's Process page and FTFLABELALL run the checks when they finish. FTFREVIEWCHECK and FTFQC always run them.");
            _duplicate = TextRow("Duplicate points within (ft)", "Two points closer than this in plan are reported as duplicates.");
            _verifySurface = CheckRow("Verify surface membership",
                "Off (the default): a ground shot is accounted for as surface data whether or not a surface uses it -- surface data is not a drafting deliverable. " +
                "On: each one is also checked against the drawing's TIN surfaces: verified (a vertex, or in a point group the surface is built from), " +
                "excluded by the surface's own definition (a warning), or not verifiable (listed once per code, as information).");
            _surface = TextRow("Surface vertex within (ft)", "A TIN vertex this close to a point, in plan and elevation, is the point.");
            _controlCheck = TextRow("Control check: control point within (ft)",
                "A control check shot (ZK) is paired with the nearest control point this close in plan, in <drawing>.ftf-control-checks.csv.");
            _reasons = CheckRow("Ignored codes need a reason",
                "A code on an ignore list without a reason (rules.json ignoreCodes, Settings > Diagnostics) is listed as a warning, so the reason gets written down.");
            _report = CheckRow("Write the point ledger beside the drawing",
                "<drawing>.ftf-points.csv (every point, its class, standard and how it is accounted for), <drawing>.ftf-continuity.csv " +
                "and, when there are control check shots, <drawing>.ftf-control-checks.csv.");

            Heading("Symbols");
            Note("A bare code is accounted for by its point style unless its drafting standard requires its own symbol (Symbol: Specific, " +
                 "as for a fire hydrant). Then the marker must be the code's own symbol block: one named in the standard's symbol style, " +
                 "one named here, the one the orientation standard gives the code, or one whose name holds the code as a word (PMX-CB for CB); " +
                 "a dot, an X or another code's symbol does not count. One line per code: CODE = BLOCK, BLOCK* ...");
            _symbols = AddFullWidth(new TextBox { Multiline = true, Height = 80, Width = 520, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true });
            _symbols.TextChanged += (o, e) => MarkChanged();

            Heading("Linework continuity");
            Note("The shots are read in collection order as the strings the crew coded (begin, continue, end, close, curves), " +
                 "and compared with the lines drawn through them. A gap in a coded string, a figure begun again after an interruption, " +
                 "a line that skips a coded shot, or coded shots with no line are listed with their evidence; probable and possible " +
                 "gaps both need review. Gate posts at both ends, or another feature crossing the gap, make a break intentional. " +
                 "Proposals are previewed dashed on the non-plotted layer below; approving one in FTFREVIEW draws a separate line " +
                 "on the feature's layer -- the survey linework is never changed.");
            _continuity = CheckRow("Check linework continuity", "Off: no continuity items; the ledger still checks that linework shots are drawn.");
            _proposalLayer = TextRow("Proposal preview layer", "Made non-plotting by FTF.", 200);
            _onLine = TextRow("On the line within (ft)", "A shot this close to a line counts as on it (a curve shot need not be a vertex).");
            Note("The limits below are INITIAL TESTING VALUES, not proven standards. Tune them per feature against real drawings. " +
                 "A gap is proposed only when it is shorter than both the spacing factor times the strings' median shot spacing and " +
                 "the longest gap; the sequence window is how many shots may come between the two parts.");
            _features = new DataGridView
            {
                Width = 760,
                Height = 240,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                RowHeadersVisible = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _features.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", FillWeight = 22 });
            _features.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Codes", FillWeight = 34, ToolTipText = "Comma separated; empty on the defaults row." });
            _features.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Check", FillWeight = 8 });
            _features.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "x spacing", FillWeight = 9 });
            _features.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Max gap ft", FillWeight = 9 });
            _features.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Shots between", FillWeight = 9 });
            _features.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Max turn deg", FillWeight = 9 });
            _features.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Crossing = intentional", FillWeight = 11 });
            _features.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Gate posts = intentional", FillWeight = 11 });
            _features.CellValueChanged += (o, e) => MarkChanged();
            _features.UserDeletedRow += (o, e) => MarkChanged();
            AddFullWidth(_features);
            Note("The first row is the defaults for any feature without its own row; leave its codes empty.");
        }

        public override void LoadFrom(FtfSettings s)
        {
            var a = s.Accountability ?? new AccountabilitySettings();
            _run.Checked = a.RunAfterProcessing;
            _duplicate.Text = Fmt(a.DuplicateToleranceFeet);
            _surface.Text = Fmt(a.SurfaceToleranceFeet);
            _verifySurface.Checked = a.VerifySurfaceMembership;
            _controlCheck.Text = Fmt(a.ControlCheckSearchFeet);
            _reasons.Checked = a.RequireIgnoreReasons;
            _report.Checked = a.WriteReport;
            _symbols.Text = string.Join(Environment.NewLine, (a.SymbolBlocks ?? new Dictionary<string, List<string>>())
                .Select(p => p.Key + " = " + string.Join(", ", (p.Value ?? new List<string>()).ToArray())).ToArray());
            var c = a.Continuity ?? new ContinuitySettings();
            _continuity.Checked = c.Enabled;
            _proposalLayer.Text = c.ProposalLayer;
            _onLine.Text = Fmt(c.OnLineToleranceFeet);
            _features.Rows.Clear();
            AddRow(c.Defaults ?? new FeatureContinuity { Name = "Any other linework" });
            foreach (var f in c.Features ?? new List<FeatureContinuity>()) AddRow(f);
        }

        private void AddRow(FeatureContinuity f)
        {
            _features.Rows.Add(f.Name ?? string.Empty, string.Join(", ", (f.Codes ?? new List<string>()).ToArray()), f.Enabled,
                               Fmt(f.SpacingFactor), Fmt(f.MaxGapFeet), f.SequenceWindow.ToString(CultureInfo.InvariantCulture),
                               Fmt(f.MaxDeflectionDegrees), f.CrossingMeansIntentional, f.GatePostsMeanIntentional);
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            _features.EndEdit();
            if (s.Accountability == null) s.Accountability = new AccountabilitySettings();
            var a = s.Accountability;
            a.RunAfterProcessing = _run.Checked;
            var v = a.DuplicateToleranceFeet;
            ReadDouble(_duplicate, "Point Accountability: duplicate points within", x => x >= 0, "cannot be negative", problems, ref v);
            a.DuplicateToleranceFeet = v;
            v = a.SurfaceToleranceFeet;
            ReadDouble(_surface, "Point Accountability: surface vertex within", x => x >= 0, "cannot be negative", problems, ref v);
            a.SurfaceToleranceFeet = v;
            a.VerifySurfaceMembership = _verifySurface.Checked;
            v = a.ControlCheckSearchFeet;
            ReadDouble(_controlCheck, "Point Accountability: control check, control point within", x => x > 0, "must be more than 0", problems, ref v);
            a.ControlCheckSearchFeet = v;
            a.RequireIgnoreReasons = _reasons.Checked;
            a.WriteReport = _report.Checked;

            var symbols = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in (_symbols.Text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = line.IndexOf('=');
                if (eq <= 0) { if (line.Trim().Length > 0) problems.Add("Point Accountability: symbol line \"" + line.Trim() + "\" needs CODE = BLOCK."); continue; }
                var code = line.Substring(0, eq).Trim().ToUpperInvariant();
                var blocks = SettingsRegistry.Split(line.Substring(eq + 1)).ToList();
                if (code.Length == 0 || blocks.Count == 0) continue;
                List<string> list;
                if (!symbols.TryGetValue(code, out list)) { list = new List<string>(); symbols[code] = list; }
                list.AddRange(blocks);
            }
            a.SymbolBlocks = symbols;

            var c = a.Continuity ?? new ContinuitySettings();
            c.Enabled = _continuity.Checked;
            c.ProposalLayer = string.IsNullOrWhiteSpace(_proposalLayer.Text) ? "FTF-QC-CONTINUITY" : _proposalLayer.Text.Trim();
            v = c.OnLineToleranceFeet;
            ReadDouble(_onLine, "Linework continuity: on the line within", x => x >= 0, "cannot be negative", problems, ref v);
            c.OnLineToleranceFeet = v;
            var rows = new List<FeatureContinuity>();
            foreach (DataGridViewRow r in _features.Rows)
            {
                if (r.IsNewRow) continue;
                var f = new FeatureContinuity
                {
                    Name = Convert.ToString(r.Cells[0].Value),
                    Codes = SettingsRegistry.Split(Convert.ToString(r.Cells[1].Value)).Select(x => x.ToUpperInvariant()).Distinct().ToList(),
                    Enabled = Convert.ToBoolean(r.Cells[2].Value)
                };
                f.SpacingFactor = Number(r.Cells[3].Value, f.SpacingFactor, "spacing factor", f.Name, problems);
                f.MaxGapFeet = Number(r.Cells[4].Value, f.MaxGapFeet, "longest gap", f.Name, problems);
                f.SequenceWindow = (int)Math.Round(Number(r.Cells[5].Value, f.SequenceWindow, "shots between", f.Name, problems));
                f.MaxDeflectionDegrees = Number(r.Cells[6].Value, f.MaxDeflectionDegrees, "sharpest turn", f.Name, problems);
                f.CrossingMeansIntentional = Convert.ToBoolean(r.Cells[7].Value);
                f.GatePostsMeanIntentional = Convert.ToBoolean(r.Cells[8].Value);
                rows.Add(f);
            }
            if (rows.Count > 0)
            {
                c.Defaults = rows[0];
                c.Defaults.Codes = new List<string>();
                c.Features = rows.Skip(1).ToList();
            }
            a.Continuity = c;
            var more = new List<string>();
            a.Validate(more);
            foreach (var p in more) if (!problems.Contains(p)) problems.Add(p);
        }

        private static double Number(object value, double fallback, string what, string row, ICollection<string> problems)
        {
            double d;
            if (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            problems.Add("Linework continuity (" + (string.IsNullOrWhiteSpace(row) ? "a row" : row) + "): the " + what + " is not a number.");
            return fallback;
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.Accountability = new AccountabilitySettings();
            LoadFrom(s);
        }
    }
}
