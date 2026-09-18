using System;
using System.Collections.Generic;
using System.Windows.Forms;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>
    /// The Drafting Lines section: the catalog FTFDRAWLINE draws from. Deliberately
    /// its own page -- these standards describe lines FTF CREATES on request, which
    /// is a different world from Point Features (the pipeline's grammar) and Line
    /// Labels (annotation on geometry Civil 3D created), and mixing them would blur
    /// exactly the boundary the two worlds depend on.
    ///
    /// Settings are the authority: the command carries no layer knowledge of its
    /// own. Layers and linetypes are chosen HERE, once, from dropdowns of what the
    /// open drawing actually has -- nobody types a layer name, and the command
    /// refuses a configured layer the drawing no longer has rather than drawing
    /// somewhere else. A saved assignment is never second-guessed.
    ///
    /// One type is edited at a time; switching types commits the current controls
    /// first, so nothing typed is lost. Rows that do not apply to the chosen
    /// annotation standard collapse, the same way the point-feature pages do.
    ///
    /// UNTESTED: never shown in AutoCAD.
    /// </summary>
    internal sealed class DraftingLinesPage : SetupPage
    {
        private ComboBox _typePicker;
        private CheckBox _enabled;
        private ComboBox _layer;
        private ComboBox _linetype;
        private ComboBox _annotation;
        private TextBox _featureText;
        private ComboBox _annotationLayer;
        private ComboBox _textStyle;
        private TextBox _height;
        private TextBox _offset;
        private ComboBox _placement;
        private CheckBox _stacked;
        private CheckBox _mask;
        private ComboBox _bearingFormat;
        private ComboBox _bearingPrecision;
        private ComboBox _distancePrecision;
        private CheckBox _footSymbol;

        private List<DraftingLineType> _working = new List<DraftingLineType>();
        private int _current = -1;
        private bool _switching;

        // Annotation combo order. Index IS the mapping, so keep these together.
        private static readonly string[] AnnotationKinds =
        {
            DraftingLineType.AnnotationNone,
            DraftingLineType.AnnotationBearingDistance,
            DraftingLineType.AnnotationBearingOnly,
            DraftingLineType.AnnotationDistanceOnly,
            DraftingLineType.AnnotationFeatureText
        };
        private static readonly object[] AnnotationLabels =
        {
            "None", "Bearing + distance", "Bearing only", "Distance only",
            "Feature text"
        };

        public override string Title { get { return "Drafting Lines"; } }
        public override string AffectedCommands { get { return "FTFDRAWLINE, FTFDRAFTCLEAN"; } }

        protected override void BuildBody()
        {
            Heading("Survey drafting lines");
            Note("FTFDRAWLINE drafts the cadastral / record lines an office draws " +
                 "deliberately -- boundary, right of way, section lines, easements. " +
                 "Configure each type's standard here, once; the command then applies " +
                 "it automatically and never asks for a layer while drafting. The " +
                 "drawing's own tables are the authority: a configured layer or " +
                 "linetype the drawing does not have is refused at draw time, never " +
                 "invented or created.");

            _typePicker = ComboRow("Line type", new object[0],
                "Each type carries its own standard; all share one construction engine.");
            _typePicker.SelectedIndexChanged += OnTypePicked;

            _enabled = CheckRow("Enabled",
                "Disabled types are not offered by FTFDRAWLINE.");

            Heading("Linework");
            var layers = new List<object>();
            foreach (var name in Context.Drawing.Layers) layers.Add(name);

            _layer = ComboRow("Layer", Prepend("(not configured)", layers),
                "The office layer this type draws on -- pick it from this drawing's " +
                "real layer list. Must exist at draw time.");

            var linetypes = new List<object>();
            foreach (var name in Context.Drawing.Linetypes) linetypes.Add(name);
            _linetype = ComboRow("Linetype", Prepend("(ByLayer)", linetypes),
                "Normally ByLayer -- the layer controls the linetype. An explicit " +
                "choice must already be loaded in the drawing; FTF never loads one.");

            Heading("Annotation");
            _annotation = ComboRow("Type", AnnotationLabels,
                "What each course gets. Bearing and distance are always computed " +
                "from the drawn geometry, never echoed from what was typed. " +
                "(Curve data becomes available with the curve milestone.)");
            _annotation.SelectedIndexChanged += (s, e) => UpdateAnnotationRows();

            _featureText = TextRow("Feature text",
                "The literal label drawn along the line, e.g. SECTION LINE. Never " +
                "derived from the type name.", 220);

            _annotationLayer = ComboRow("Layer",
                Prepend("(derive from the line layer)", new List<object>(layers)),
                "Empty derives the office text layer from the line layer's family, " +
                "the same way line labels do -- only layers that actually exist.");

            var styles = new List<object>();
            foreach (var name in Context.Drawing.TextStyles) styles.Add(name);
            _textStyle = ComboRow("Text style", Prepend("(drawing's current style)", styles),
                null);

            _height = TextRow("Text height (plotted)", null);

            _placement = ComboRow("Placement",
                new object[] { "Above", "Below", "On line" },
                "Above or below in the text's reading direction. The readability " +
                "flip never changes the reported bearing. Ignored when stacked.");

            _offset = TextRow("Offset from line (plotted)",
                "Gap between the line and the nearest edge of the text.");

            _stacked = CheckRow("Stacked (bearing over the line, distance under it)",
                "Bearing + distance only.");
            _mask = CheckRow("Mask under the annotation", null);

            Heading("Bearing / distance format");
            _bearingFormat = ComboRow("Bearing format",
                new object[] { "Quadrant bearing (N 42°18'36\" E)",
                               "Azimuth (215°30'00\")" },
                null);
            _bearingPrecision = ComboRow("Bearing precision",
                new object[] { "1 second", "0.1 second", "0.01 second", "0.001 second" },
                null);
            _distancePrecision = ComboRow("Distance precision",
                new object[] { "1 foot", "0.1", "0.01", "0.001", "0.0001" },
                null);
            _footSymbol = CheckRow("Append the foot symbol (')", null);
        }

        private static IList<object> Prepend(string first, IList<object> rest)
        {
            var items = new List<object> { first };
            foreach (var item in rest) items.Add(item);
            return items;
        }

        /// <summary>Collapses the rows the chosen annotation standard does not use,
        /// so the page reads as the standard it configures.</summary>
        private void UpdateAnnotationRows()
        {
            var index = _annotation.SelectedIndex;
            var isBearingDistance = index == 1;
            var usesBearing = index == 1 || index == 2;
            var usesDistance = index == 1 || index == 3;
            var any = index > 0;

            SetRowVisible(_featureText, index == 4);
            SetRowVisible(_annotationLayer, any);
            SetRowVisible(_textStyle, any);
            SetRowVisible(_height, any);
            SetRowVisible(_placement, any);
            SetRowVisible(_offset, any);
            SetRowVisible(_stacked, isBearingDistance);
            SetRowVisible(_mask, any);
            SetRowVisible(_bearingFormat, usesBearing);
            SetRowVisible(_bearingPrecision, usesBearing);
            SetRowVisible(_distancePrecision, usesDistance);
            SetRowVisible(_footSymbol, usesDistance);
        }

        public override void LoadFrom(FtfSettings settings)
        {
            _working = new List<DraftingLineType>();
            if (settings.Drafting != null && settings.Drafting.LineTypes != null)
            {
                foreach (var type in settings.Drafting.LineTypes)
                    if (type != null) _working.Add(type.Clone());
            }

            _switching = true;
            _typePicker.Items.Clear();
            foreach (var type in _working) _typePicker.Items.Add(type.Name ?? "(unnamed)");
            _switching = false;

            _current = -1;
            if (_typePicker.Items.Count > 0) _typePicker.SelectedIndex = 0;
        }

        private void OnTypePicked(object sender, EventArgs e)
        {
            if (_switching) return;

            var next = _typePicker.SelectedIndex;
            if (next == _current) return;

            // Commit what is on screen before showing the next type, so switching
            // never loses an edit. A value that does not parse blocks the switch --
            // silently keeping the old number would be a quiet data loss.
            if (_current >= 0)
            {
                var problems = new List<string>();
                CommitCurrent(problems);
                if (problems.Count > 0)
                {
                    MessageBox.Show(_typePicker.FindForm(),
                        string.Join(Environment.NewLine, problems.ToArray()),
                        "Fix this before switching types",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _switching = true;
                    _typePicker.SelectedIndex = _current;
                    _switching = false;
                    return;
                }
            }

            _current = next;
            ShowType(_working[_current]);
        }

        private void ShowType(DraftingLineType type)
        {
            _enabled.Checked = type.Enabled;
            ComboHelp.SelectOrAdd(_layer, type.Layer, true);
            ComboHelp.SelectOrAdd(_linetype, type.Linetype, true);

            var kind = type.AnnotationKind ?? DraftingLineType.AnnotationBearingDistance;
            _annotation.SelectedIndex = Math.Max(0, Array.IndexOf(AnnotationKinds, kind));

            _featureText.Text = type.FeatureText ?? string.Empty;
            ComboHelp.SelectOrAdd(_annotationLayer, type.AnnotationLayer, true);
            ComboHelp.SelectOrAdd(_textStyle, type.TextStyle, true);

            _height.Text = Fmt(type.TextHeightPlotted);
            _offset.Text = Fmt(type.OffsetPlotted);

            var placement = (type.Placement ?? "Above").Trim().Replace(" ", string.Empty);
            _placement.SelectedIndex =
                string.Equals(placement, "Below", StringComparison.OrdinalIgnoreCase) ? 1
                : string.Equals(placement, "OnLine", StringComparison.OrdinalIgnoreCase) ? 2
                : 0;

            _stacked.Checked = type.Stacked;
            _mask.Checked = type.Mask;

            _bearingFormat.SelectedIndex = type.WantsAzimuthFormat ? 1 : 0;
            _bearingPrecision.SelectedIndex = Clamp(type.BearingSecondsDecimals, 0,
                _bearingPrecision.Items.Count - 1);
            _distancePrecision.SelectedIndex = Clamp(type.DistanceDecimals, 0,
                _distancePrecision.Items.Count - 1);
            _footSymbol.Checked = type.FootSymbol;

            UpdateAnnotationRows();
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }

        /// <summary>The on-screen values into the current working type.</summary>
        private void CommitCurrent(ICollection<string> problems)
        {
            if (_current < 0 || _current >= _working.Count) return;
            var type = _working[_current];
            var label = "Drafting lines: " + (type.Name ?? "(unnamed)") + ": ";

            type.Enabled = _enabled.Checked;

            type.Layer = _layer.SelectedIndex <= 0
                ? string.Empty : Convert.ToString(_layer.SelectedItem);
            type.Linetype = _linetype.SelectedIndex <= 0
                ? string.Empty : Convert.ToString(_linetype.SelectedItem);

            var kindIndex = Clamp(_annotation.SelectedIndex, 0, AnnotationKinds.Length - 1);
            type.Annotation = AnnotationKinds[kindIndex];

            // Missing feature text is a save-time problem (the section's Validate
            // reports it), not a reason to block switching between types here.
            type.FeatureText = (_featureText.Text ?? string.Empty).Trim();

            type.AnnotationLayer = _annotationLayer.SelectedIndex <= 0
                ? string.Empty : Convert.ToString(_annotationLayer.SelectedItem);
            type.TextStyle = _textStyle.SelectedIndex <= 0
                ? string.Empty : Convert.ToString(_textStyle.SelectedItem);

            var height = type.TextHeightPlotted;
            ReadDouble(_height, label + "text height", v => v > 0,
                       "must be greater than zero", problems, ref height);
            type.TextHeightPlotted = height;

            var offset = type.OffsetPlotted;
            ReadDouble(_offset, label + "offset from line", v => v >= 0,
                       "cannot be negative", problems, ref offset);
            type.OffsetPlotted = offset;

            type.Placement = _placement.SelectedIndex == 1 ? "Below"
                : _placement.SelectedIndex == 2 ? "OnLine"
                : "Above";

            type.Stacked = _stacked.Checked;
            type.Mask = _mask.Checked;

            type.BearingFormat = _bearingFormat.SelectedIndex == 1
                ? DraftingLineType.BearingFormatAzimuth
                : DraftingLineType.BearingFormatQuadrant;

            // The precision dropdowns' index IS the decimal count.
            type.BearingSecondsDecimals = Clamp(_bearingPrecision.SelectedIndex, 0, 3);
            type.DistanceDecimals = Clamp(_distancePrecision.SelectedIndex, 0, 4);

            type.FootSymbol = _footSymbol.Checked;
        }

        public override void SaveTo(FtfSettings settings, ICollection<string> problems)
        {
            CommitCurrent(problems);

            var saved = new List<DraftingLineType>();
            foreach (var type in _working) saved.Add(type.Clone());

            if (settings.Drafting == null) settings.Drafting = new DraftingSettings();
            settings.Drafting.LineTypes = saved;
        }

        public override void RestoreDefaults(FtfSettings settings)
        {
            if (settings.Drafting == null) settings.Drafting = new DraftingSettings();
            settings.Drafting.RestoreDefaults();
            LoadFrom(settings);
        }
    }
}
