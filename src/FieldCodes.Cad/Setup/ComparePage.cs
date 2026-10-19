using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using FieldCodes.Compare;
using FieldCodes.Settings;

namespace FieldCodes.Cad.Setup
{
    /// <summary>Settings for FTFCOMPARE on this machine. Mappings and tolerances live in
    /// the comparison profile (JSON, one per consultant), reachable from here.</summary>
    internal sealed class ComparePage : SetupPage
    {
        private ComboBox _profile;
        private TextBox _deliveryFolder;
        private TextBox _suffix;
        private CheckBox _writeCsv;
        private CheckBox _applyAuto;
        private ComboBox _source;
        private TextBox _file;
        private TextBox _surveyColor;
        private TextBox _consultantColor;
        private CheckBox _decisionsBeside;

        public override string Title { get { return "Survey Comparison"; } }
        public override string AffectedCommands { get { return "FTFCOMPARE, FTFCOMPARERUN, FTFCOMPAREAPPLY, FTFCOMPARESAVE, FTFCOMPAREBATCH"; } }

        protected override void BuildBody()
        {
            Heading("Profile");
            Note("A comparison profile maps one consultant's layers, blocks and wording to our " +
                 "features and holds the tolerances and nonplot layers. The built-in Default is a " +
                 "starting point; save a copy per consultant (FTFCOMPAREPROFILE SaveAs) and edit the JSON.");
            var items = new List<object>();
            foreach (var name in CompareProfile.ListProfiles(PluginDirectory())) items.Add(name);
            _profile = ComboRow("Profile when the drawing has not chosen one", items, "FTFCOMPAREPROFILE Use sets a drawing's own choice.");
            var open = new Button { Text = "Open profiles folder" };
            open.Click += (s, e) =>
            {
                try
                {
                    Directory.CreateDirectory(CompareProfile.ProfilesDirectory());
                    System.Diagnostics.Process.Start(CompareProfile.ProfilesDirectory());
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (IOException) { }
            };
            AddFullWidth(SideButtons(open));

            Heading("Consultant data");
            _source = ComboRow("Where the consultant's drawing is", new object[] { "Xrefs", "File" },
                "Xrefs: the external references attached to the survey drawing. File: a drawing read without attaching it.");
            _file = TextRow("Consultant file (File mode)", "Full path. Each drawing can override this with FTFCOMPARESOURCE.", 360);

            Heading("Applying");
            _applyAuto = CheckRow("Apply confident automatic matches without a decision",
                "Off means every nonplot change needs an explicit Accept in the review window.");
            _decisionsBeside = CheckRow("Also keep review decisions in a JSON file beside the drawing",
                "Decisions are always stored in the drawing; the file lets a copy or a re-run on a revised base reuse them.");

            Heading("Deliverable");
            _deliveryFolder = TextRow("Delivery folder", "Where FTFCOMPARESAVE writes the approved drawing. The profile's own deliveryFolder wins when set. Empty asks.", 360);
            _suffix = TextRow("File name suffix", "survey.dwg becomes survey-ACC.dwg", 100);
            _writeCsv = CheckRow("Write the CSV report beside the drawing after every run", null);

            Heading("Review highlight");
            _surveyColor = TextRow("Our feature colour (ACI 1-255)", "Temporary review graphics only; nothing in the drawing changes.", 60);
            _consultantColor = TextRow("Consultant feature colour (ACI 1-255)", null, 60);
        }

        private static string PluginDirectory()
        {
            try
            {
                var location = typeof(ComparePage).Assembly.Location;
                return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
            }
            catch (NotSupportedException) { return null; }
        }

        public override void LoadFrom(FtfSettings settings)
        {
            var c = settings.Compare;
            if (_profile.Items.IndexOf(c.ActiveProfile) < 0 && !string.IsNullOrEmpty(c.ActiveProfile)) _profile.Items.Add(c.ActiveProfile);
            _profile.SelectedItem = c.ActiveProfile;
            _source.SelectedItem = c.ConsultantSource == "File" ? "File" : "Xrefs";
            _file.Text = c.ConsultantFile ?? string.Empty;
            _applyAuto.Checked = c.ApplyAutomaticResults;
            _decisionsBeside.Checked = c.DecisionsBesideDrawing;
            _deliveryFolder.Text = c.DeliveryFolder ?? string.Empty;
            _suffix.Text = c.DeliverySuffix ?? string.Empty;
            _writeCsv.Checked = c.WriteCsvReport;
            _surveyColor.Text = c.HighlightSurveyColor.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _consultantColor.Text = c.HighlightConsultantColor.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public override void SaveTo(FtfSettings settings, ICollection<string> problems)
        {
            var c = settings.Compare;
            c.ActiveProfile = _profile.SelectedItem as string ?? "Default";
            c.ConsultantSource = _source.SelectedItem as string ?? "Xrefs";
            c.ConsultantFile = _file.Text.Trim();
            c.ApplyAutomaticResults = _applyAuto.Checked;
            c.DecisionsBesideDrawing = _decisionsBeside.Checked;
            c.DeliveryFolder = _deliveryFolder.Text.Trim();
            c.DeliverySuffix = _suffix.Text.Trim();
            c.WriteCsvReport = _writeCsv.Checked;
            short color;
            if (short.TryParse(_surveyColor.Text.Trim(), out color) && color >= 1 && color <= 255) c.HighlightSurveyColor = color;
            else problems.Add("Survey highlight colour must be 1-255.");
            if (short.TryParse(_consultantColor.Text.Trim(), out color) && color >= 1 && color <= 255) c.HighlightConsultantColor = color;
            else problems.Add("Consultant highlight colour must be 1-255.");
        }

        public override void RestoreDefaults(FtfSettings settings)
        {
            settings.Compare.RestoreDefaults();
            LoadFrom(settings);
        }
    }
}
