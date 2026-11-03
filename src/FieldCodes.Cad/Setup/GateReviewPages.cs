using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using FieldCodes.Review;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    // ------------------------------------------------------------------- gates

    [SettingsPage(Group = "FieldToFinish", Label = "Gates", Order = 75, Sections = "gates", Commands = "FTFGATES,FTFGATE,FTFLINELABELS,FTFLABELALL")]
    internal sealed class GatesPage : SetupPage
    {
        private CheckBox _enabled;
        private TextBox _codes;
        private TextBox _reach;
        private TextBox _search;
        private CheckBox _withoutFence;
        private TextBox _ambiguity;
        private TextBox _narrow;
        private TextBox _plain;
        private TextBox _max;
        private TextBox _plainText;
        private TextBox _widthFormat;
        private TextBox _narrowText;
        private TextBox _round;
        private TextBox _decimals;
        private TextBox _tolerance;
        private ComboBox _layer;

        public override string Title { get { return "Gates"; } }
        public override string AffectedCommands { get { return "FTFGATES, FTFGATE, FTFLINELABELS, FTFLABELALL"; } }

        protected override void BuildBody()
        {
            Heading("Gate posts");
            Note("Two shots coded as gate posts are paired into one gate and labelled between them " +
                 "with the width: \"6' GATE\". A pair needs a fence to say so -- both posts on one " +
                 "fence next to each other, or the fence stopping at both -- never distance alone. " +
                 "A post with no partner, or with two possible partners, goes to the review list; " +
                 "nothing is guessed and no post is ever moved. FTFGATE pairs two posts you pick.");

            _enabled = CheckRow("Find and label gates",
                "FTFGATES, the automatic line label pass and Label All pair the posts and label the gates.");
            _codes = TextRow("Gate post codes",
                "The code sheet's gate post code and the crew's own aliases, comma separated (FGP, GP). " +
                "Matched as a whole word anywhere in the description: \"FGP FNC\" is a gate post on the fence.", 220);
            _reach = TextRow("On the fence within (ft)",
                "A post this close to a fence line is on that fence.");
            _search = TextRow("Look for a partner within (ft)",
                "Posts farther apart are never one gate. At least the widest gate.");
            _withoutFence = CheckRow("Pair posts that are not on any fence",
                "Off: two posts with no fence near them are proposed in the review list instead of labelled.");
            _ambiguity = TextRow("Ambiguous within (%)",
                "A post with two possible partners whose distances are within this percent of each other " +
                "is ambiguous: neither is labelled, both go to review.");

            Heading("Width and label");
            Note("The width is the distance between the two post shots, centre to centre -- not a " +
                 "measured clear opening, and the review list says so. A width the crew wrote on a post " +
                 "(\"FGP 12\") is used when the posts agree with it. No width is ever replaced by a " +
                 "standard size.");
            _narrow = TextRow("Review gates narrower than (ft)",
                "Narrower gates go to review, unless a narrow-gate label is set below.");
            _plain = TextRow("Plain label up to (ft)",
                "From the review limit up to this width the label is the plain one (4.8 ft: GATE).");
            _max = TextRow("Review gates wider than (ft)",
                "Wider gates go to review: two gates, or posts from different gates?");
            _plainText = TextRow("Plain label", "The label of a gate between the two limits above.", 160);
            _widthFormat = TextRow("Label with the width",
                "{width} is the rounded width: \"{width}' GATE\" labels 6' GATE.", 160);
            _narrowText = TextRow("Narrow-gate label",
                "Empty sends narrow gates to review; a label (WALK GATE) labels them instead.", 160);
            _round = TextRow("Round the width to (ft)", "1 for whole feet, 0.5 for half feet.");
            _decimals = TextRow("Width decimals", "Most decimal places written; trailing zeros are dropped.");
            _tolerance = TextRow("Recorded width tolerance (ft)",
                "A width written on a post is used when it is within this of the post spacing; further off, review.");

            Heading("Where the label goes");
            Note("Between the posts, along the opening, in the line label text style, height and mask. " +
                 "Move it wherever you like: a rerun keeps a gate label you moved or reworded.");
            var layers = new List<object> { "(the fence's label layer)" };
            foreach (var name in Context.Drawing.Layers) layers.Add(name);
            _layer = ComboRow("Gate label layer", layers, null);
        }

        public override void LoadFrom(FtfSettings s)
        {
            var g = s.Gates;
            _enabled.Checked = g.Enabled;
            _codes.Text = string.Join(", ", (g.PostCodes ?? new List<string>()).ToArray());
            _reach.Text = Fmt(g.FenceReachFeet);
            _search.Text = Fmt(g.SearchFeet);
            _withoutFence.Checked = g.PairWithoutFence;
            _ambiguity.Text = Fmt(g.AmbiguityPercent);
            _narrow.Text = Fmt(g.NarrowBelowFeet);
            _plain.Text = Fmt(g.PlainBelowFeet);
            _max.Text = Fmt(g.MaxWidthFeet);
            _plainText.Text = g.PlainText;
            _widthFormat.Text = g.WidthFormat;
            _narrowText.Text = g.NarrowText;
            _round.Text = Fmt(g.RoundToFeet);
            _decimals.Text = g.Decimals.ToString(CultureInfo.InvariantCulture);
            _tolerance.Text = Fmt(g.RecordedToleranceFeet);
            ComboHelp.SelectOrAdd(_layer, g.LabelLayer, true);
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            var g = s.Gates;
            g.Enabled = _enabled.Checked;
            var codes = SettingsRegistry.Split(_codes.Text).Select(c => c.ToUpperInvariant()).Distinct().ToList();
            if (codes.Count == 0) problems.Add("Gates: name at least one gate post code (FGP).");
            else g.PostCodes = codes;

            var v = g.FenceReachFeet;
            ReadDouble(_reach, "Gates: on the fence within", x => x > 0, "must be greater than zero", problems, ref v);
            g.FenceReachFeet = v;
            v = g.SearchFeet;
            ReadDouble(_search, "Gates: look for a partner within", x => x > 0, "must be greater than zero", problems, ref v);
            g.SearchFeet = v;
            g.PairWithoutFence = _withoutFence.Checked;
            v = g.AmbiguityPercent;
            ReadDouble(_ambiguity, "Gates: ambiguous within", x => x >= 0 && x <= 100, "must be 0 to 100", problems, ref v);
            g.AmbiguityPercent = v;
            v = g.NarrowBelowFeet;
            ReadDouble(_narrow, "Gates: review gates narrower than", x => x >= 0, "cannot be negative", problems, ref v);
            g.NarrowBelowFeet = v;
            v = g.PlainBelowFeet;
            ReadDouble(_plain, "Gates: plain label up to", x => x >= 0, "cannot be negative", problems, ref v);
            g.PlainBelowFeet = v;
            v = g.MaxWidthFeet;
            ReadDouble(_max, "Gates: review gates wider than", x => x > 0, "must be greater than zero", problems, ref v);
            g.MaxWidthFeet = v;
            g.PlainText = (_plainText.Text ?? string.Empty).Trim();
            g.WidthFormat = (_widthFormat.Text ?? string.Empty).Trim();
            g.NarrowText = (_narrowText.Text ?? string.Empty).Trim();
            v = g.RoundToFeet;
            ReadDouble(_round, "Gates: round the width to", x => x > 0, "must be greater than zero", problems, ref v);
            g.RoundToFeet = v;
            var d = g.Decimals;
            ReadInt(_decimals, "Gates: width decimals", x => x >= 0 && x <= 3, "must be 0 to 3", problems, ref d);
            g.Decimals = d;
            v = g.RecordedToleranceFeet;
            ReadDouble(_tolerance, "Gates: recorded width tolerance", x => x >= 0, "cannot be negative", problems, ref v);
            g.RecordedToleranceFeet = v;
            g.LabelLayer = _layer.SelectedIndex <= 0 ? string.Empty : Convert.ToString(_layer.SelectedItem);

            var more = new List<string>();
            g.Validate(more);
            foreach (var p in more) if (!problems.Contains(p)) problems.Add(p);
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.Gates.RestoreDefaults();
            LoadFrom(s);
        }
    }

    // ------------------------------------------------------------ review and QC

    [SettingsPage(Group = "FieldToFinish", Label = "Review and QC", Order = 140, Sections = "review", Commands = "FTFREVIEW,FTFQC")]
    internal sealed class ReviewQcPage : SetupPage
    {
        private CheckBox _block;
        private TextBox _reviewer;
        private DataGridView _kinds;

        public override string Title { get { return "Review and QC"; } }
        public override string AffectedCommands { get { return "FTFREVIEW, FTFQC"; } }

        protected override void BuildBody()
        {
            Heading("Review list");
            Note("Everything FTF could not decide -- unknown codes, unpaired gate posts, fence heights " +
                 "it could not read, arrows whose direction is unclear, labels it could not place -- " +
                 "is kept in one list in the drawing (FTFREVIEW). Codes you told FTF to ignore and " +
                 "ordinary Civil 3D linework are not on it. Nothing here stops you saving or drafting.");

            _block = CheckRow("Refuse Drafting QC Complete while required items are open",
                "FTFQC marks the drawing Drafting QC Complete only when every required item is " +
                "resolved, accepted, or marked not applicable with a reason.");
            _reviewer = TextRow("Reviewer name",
                "Recorded when you accept an item or mark it not applicable. Empty uses your Windows sign-in.", 200);

            Heading("Required before QC Complete");
            Note("Tick the kinds that must be closed before Drafting QC Complete.");
            _kinds = new DataGridView
            {
                Width = 640,
                Height = 360,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            _kinds.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Required", FillWeight = 18 });
            _kinds.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Check", ReadOnly = true, FillWeight = 22 });
            _kinds.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Item", ReadOnly = true, FillWeight = 60 });
            _kinds.CurrentCellDirtyStateChanged += (o, e) =>
            {
                if (_kinds.IsCurrentCellDirty) _kinds.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _kinds.CellValueChanged += (o, e) => MarkChanged();
            AddFullWidth(_kinds);
        }

        public override void LoadFrom(FtfSettings s)
        {
            _block.Checked = s.Review.BlockQcComplete;
            _reviewer.Text = s.Review.ReviewerName;
            _kinds.Rows.Clear();
            foreach (var k in ReviewKinds.All)
            {
                var i = _kinds.Rows.Add(s.Review.IsRequired(k.Id), k.Source, k.Title);
                _kinds.Rows[i].Tag = k.Id;
                _kinds.Rows[i].Cells[2].ToolTipText = k.Action;
            }
        }

        public override void SaveTo(FtfSettings s, ICollection<string> problems)
        {
            _kinds.EndEdit();
            s.Review.BlockQcComplete = _block.Checked;
            s.Review.ReviewerName = (_reviewer.Text ?? string.Empty).Trim();
            foreach (DataGridViewRow r in _kinds.Rows)
            {
                var id = r.Tag as string;
                var kind = ReviewKinds.Get(id);
                if (kind == null) continue;
                var required = Convert.ToBoolean(r.Cells[0].Value);
                // Only differences from the built-in default are written.
                if (required == kind.Required) s.Review.Required.Remove(id);
                else s.Review.Required[id] = required;
            }
        }

        public override void RestoreDefaults(FtfSettings s)
        {
            s.Review.RestoreDefaults();
            LoadFrom(s);
        }
    }
}
