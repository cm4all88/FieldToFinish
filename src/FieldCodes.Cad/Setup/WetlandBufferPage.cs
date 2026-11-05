using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Editing;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// Settings > Survey Drafting > Wetland Buffers: FTFWETLANDBUFFER's settings, the rules file's
    /// wetlandBuffer section. The buffer layer, the distance and smoothing radius the command
    /// offers first, whether its "cut back at hard surfaces" prompt defaults to Yes, and which
    /// layers are hard surfaces, with which of the open drawing's layers that list matches.
    /// Loading, staging and saving are RulesSectionPage's.
    ///
    /// UNTESTED as UI; the document editing and checks are unit tested
    /// (WetlandBufferSettingsEditingTests).
    /// </summary>
    [SettingsPage(Group = "Survey Drafting", Label = "Wetland Buffers", Order = 35, Commands = "FTFWETLANDBUFFER,FTFWLB")]
    internal sealed class WetlandBufferPage : RulesSectionPage
    {
        /// <summary>Words in a layer name that suggest a hard surface, to list those layers first
        /// when adding from the drawing.</summary>
        private static readonly string[] HardWords = { "ASPH", "CONC", "CURB", "GRVL", "GRAV", "SHLD", "PAVE", "BRCK", "BLDG", "WALK", "DRIV", "ROAD" };

        private ComboBox _layer;
        private TextBox _distance;
        private TextBox _smoothing;
        private CheckBox _trim;
        private TextBox _hardLayers;
        private Label _matches;

        public override string Title { get { return "Wetland Buffers"; } }
        public override string AffectedCommands { get { return "FTFWETLANDBUFFER (FTFWLB)"; } }
        protected override string SectionKey { get { return "wetlandBuffer"; } }
        protected override string WhatChanges { get { return "wetland buffer"; } }
        protected override string SaveText { get { return "Save Wetland Buffer Settings"; } }
        protected override string UsedBy { get { return "FTFWETLANDBUFFER"; } }

        protected override void BuildRows()
        {
            Heading("Buffer");
            _layer = LayerRow("Buffer layer",
                "The layer FTFWETLANDBUFFER draws the buffer line on. PMX: V-WATR-WTLD-BUFR-E. Type a name or pick one of this drawing's layers.",
                PmxLayers.WetlandBufferLayer);
            _distance = TextRow("Distance offered first",
                "The buffer distance the command offers, in drawing units (feet in a survey drawing). The drafter can type another each time; the command remembers it for the session.", 80);
            _smoothing = TextRow("Smoothing radius offered first",
                "Rounds the inside corners with this radius; 0 leaves them sharp. Either way the buffer is never closer to the wetland than the distance.", 80);
            Note("Outside corners are always round, with the buffer distance as their radius, so every point of the buffer is exactly that far from the wetland.");

            Heading("Hard surfaces");
            _trim = CheckRow("Cut the buffer back at hard surfaces (the command's default answer; the drafter can say No)",
                "Where a line on one of the layers below lies between the buffer and the wetland, the buffer stops at it.");
            Note("A hard-surface line that crosses the buffer cuts it there. A piece is not drawn when a hard surface lies between it and the wetland, " +
                 "so the buffer stops at the edge of the road instead of running across the road. A piece only partly behind one (a building inside " +
                 "the buffer) is kept and listed as CHECK. The road and wetland lines are never changed.");
            AddFullWidth(new Label { Text = "Hard-surface layers, one per line (* matches anything, as in V-SURF-ASPH*):", AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
            _hardLayers = new TextBox
            {
                Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, WordWrap = false,
                Width = 320, Height = 190, Margin = new Padding(0, 0, 0, 3)
            };
            Tip(_hardLayers, "Lines, polylines, arcs, circles, splines, 3D polylines, survey figures and feature lines on these layers are read as hard surfaces.");
            var add = new Button { Text = "Add Drawing Layers..." };
            var pmx = new Button { Text = "Use the PMX List" };
            Tip(add, "Pick layers of the open drawing to add to the list.");
            Tip(pmx, "Replaces the list with PMX's hard surfaces: asphalt (with overlays and patches), concrete, curbs, gravel, shoulders, brick and buildings.");
            add.Click += (s, e) => AddDrawingLayers();
            pmx.Click += (s, e) => { _hardLayers.Lines = WetlandBufferRule.DefaultHardSurfaceLayers().ToArray(); };
            AddWithSideButtons(_hardLayers, SideButtons(add, pmx));
            _matches = LiveNote();

            _distance.TextChanged += (s, e) => Stage();
            _smoothing.TextChanged += (s, e) => Stage();
            _trim.CheckedChanged += (s, e) => Stage();
            _hardLayers.TextChanged += (s, e) => Stage();
        }

        protected override bool ShowSection(RuleDocument document)
        {
            var values = document != null ? document.GetWetlandBuffer() : EditableWetlandBuffer.Defaults();
            Show(values);
            return values.InFile;
        }

        protected override void ShowBuiltIn()
        {
            Show(EditableWetlandBuffer.Defaults());
        }

        protected override void ApplyControls(RuleDocument document, ICollection<string> problems)
        {
            var edit = Read(problems);
            if (document != null && problems.Count == 0) document.ApplyWetlandBuffer(edit);
        }

        protected override void CopySection(RuleDocument from, RuleDocument to)
        {
            from.CopyWetlandBufferTo(to);
        }

        protected override void Edited()
        {
            ShowMatches(Read(new List<string>()));
        }

        private void Show(EditableWetlandBuffer values)
        {
            _layer.Text = values.Layer ?? string.Empty;
            _distance.Text = Fmt(values.Distance);
            _smoothing.Text = Fmt(values.Smoothing);
            _trim.Checked = values.TrimAtHardSurfaces;
            _hardLayers.Lines = (values.HardSurfaceLayers ?? new List<string>()).ToArray();
        }

        private EditableWetlandBuffer Read(ICollection<string> problems)
        {
            var current = Document != null ? Document.GetWetlandBuffer() : EditableWetlandBuffer.Defaults();
            var distance = current.Distance;
            var smoothing = current.Smoothing;
            ReadDouble(_distance, "Wetland Buffers: the distance", v => v > 0, "must be greater than zero", problems, ref distance);
            ReadDouble(_smoothing, "Wetland Buffers: the smoothing radius", v => v >= 0, "cannot be negative (0 = none)", problems, ref smoothing);
            return new EditableWetlandBuffer
            {
                Layer = (_layer.Text ?? string.Empty).Trim(),
                Distance = distance,
                Smoothing = smoothing,
                TrimAtHardSurfaces = _trim.Checked,
                HardSurfaceLayers = EditableWetlandBuffer.CleanLayers(_hardLayers.Lines)
            };
        }

        /// <summary>Which of the open drawing's layers the list makes hard surfaces, and which
        /// listed layers the drawing does not have.</summary>
        private void ShowMatches(EditableWetlandBuffer values)
        {
            var patterns = EditableWetlandBuffer.CleanLayers(values.HardSurfaceLayers);
            if (patterns.Count == 0)
            {
                _matches.ForeColor = Pending;
                _matches.Text = values.TrimAtHardSurfaces
                    ? "No hard-surface layers are listed, so nothing will be cut back."
                    : "No hard-surface layers are listed.";
                return;
            }
            _matches.ForeColor = SystemColors.GrayText;
            var layers = Context != null && Context.Drawing != null && Context.Drawing.Layers != null ? Context.Drawing.Layers : new List<string>();
            if (layers.Count == 0)
            {
                _matches.Text = "Open a drawing to see which of its layers these match.";
                return;
            }
            var rule = new WetlandBufferRule { HardSurfaceLayers = patterns };
            var hard = layers.Where(rule.IsHardSurface).OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
            var unused = patterns.Where(p => !layers.Any(l => new WetlandBufferRule { HardSurfaceLayers = new List<string> { p } }.IsHardSurface(l))).ToList();
            var text = hard.Count == 0
                ? "None of this drawing's layers is a hard surface by this list."
                : string.Format(CultureInfo.InvariantCulture, "In this drawing, {0} layer(s) are hard surfaces: {1}{2}.", hard.Count,
                                string.Join(", ", hard.Take(12).ToArray()), hard.Count > 12 ? ", ..." : string.Empty);
            if (unused.Count > 0)
                text += string.Format(CultureInfo.InvariantCulture, " Not in this drawing: {0}{1}.",
                                      string.Join(", ", unused.Take(12).ToArray()), unused.Count > 12 ? ", ..." : string.Empty);
            var buffer = (values.Layer ?? string.Empty).Trim();
            if (buffer.Length > 0 && rule.IsHardSurface(buffer))
            {
                _matches.ForeColor = Pending;
                text += " The buffer layer is in the list itself; FTF's own buffers are never read as hard surfaces, but other lines on it would be.";
            }
            _matches.Text = text;
        }

        private void AddDrawingLayers()
        {
            var layers = Context != null && Context.Drawing != null && Context.Drawing.Layers != null ? Context.Drawing.Layers : new List<string>();
            if (layers.Count == 0) { Info("Open a drawing to add its layers."); return; }
            var listed = new WetlandBufferRule { HardSurfaceLayers = EditableWetlandBuffer.CleanLayers(_hardLayers.Lines) };
            // Layers whose names look like hard surfaces first, then the rest; those already
            // covered by the list are left out.
            var offered = layers.Where(l => !listed.IsHardSurface(l))
                                .OrderBy(l => HardWords.Any(w => l.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) ? 0 : 1)
                                .ThenBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
            if (offered.Count == 0) { Info("Every layer of this drawing is already a hard surface by the list."); return; }

            using (var form = new Form
            {
                Text = "Add hard-surface layers", Width = 460, Height = 480, StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.Sizable, MinimizeBox = false, MaximizeBox = false
            })
            {
                var caption = new Label
                {
                    Text = "Tick the layers whose lines are hard surfaces. Layers whose names suggest one are listed first.",
                    Dock = DockStyle.Top, Height = 36
                };
                var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
                foreach (var l in offered) list.Items.Add(l);
                var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
                form.Controls.Add(list);
                form.Controls.Add(ok);
                form.Controls.Add(caption);
                form.AcceptButton = ok;
                if (form.ShowDialog(_hardLayers) != DialogResult.OK || list.CheckedItems.Count == 0) return;
                var lines = _hardLayers.Lines.ToList();
                lines.AddRange(list.CheckedItems.Cast<object>().Select(o => o.ToString()));
                _hardLayers.Lines = EditableWetlandBuffer.CleanLayers(lines).ToArray();
            }
        }
    }
}
