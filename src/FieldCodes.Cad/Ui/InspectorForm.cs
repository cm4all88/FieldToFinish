using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using FlowDirection = System.Windows.Forms.FlowDirection;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// The inspection report: sections of items, each with its value and where it came from.
    /// Problems stand out; double-clicking a row with a source object zooms to it and selects it
    /// (switching to its layout when it lives on a sheet). Modeless, so the drawing stays usable.
    /// </summary>
    internal sealed class InspectorForm : Form
    {
        private readonly List<InspectRow> _rows;
        private readonly string _title;
        private readonly ListView _list;
        private readonly Label _status;

        public InspectorForm(string title, List<InspectRow> rows)
        {
            _rows = rows;
            _title = title;
            DipBuilderForm.UseTheme(ThemePreference.LoadDark());
            Text = "FTF Inspector -- " + title;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            BackColor = DipBuilderForm.Ground;
            ForeColor = DipBuilderForm.Ink;
            Font = DipBuilderForm.F(10f, false);
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(1280, area.Width - 60), Math.Min(820, area.Height - 60));

            var header = new Panel { Dock = DockStyle.Top, BackColor = DipBuilderForm.Surface, Padding = new Padding(18, 10, 18, 8) };
            var heading = new Label { AutoSize = true, Text = title, Font = DipBuilderForm.F(14f, true), ForeColor = DipBuilderForm.Ink };
            var errors = rows.Count(r => r.Severity == "Error");
            var warnings = rows.Count(r => r.Severity == "Warning");
            var summary = new Label
            {
                AutoSize = true, Top = heading.PreferredHeight + 14, Left = 20,
                ForeColor = errors > 0 ? DipBuilderForm.Bad : warnings > 0 ? DipBuilderForm.Warn : DipBuilderForm.Good,
                Text = errors > 0 ? errors + " problem(s) and " + warnings + " warning(s)." : warnings > 0 ? warnings + " warning(s)." : "No problems found.",
            };
            heading.Location = new Point(18, 8);
            header.Controls.Add(heading);
            header.Controls.Add(summary);
            header.Height = heading.PreferredHeight + summary.PreferredHeight + 26;

            _list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false, BorderStyle = BorderStyle.None,
                BackColor = DipBuilderForm.Surface, ForeColor = DipBuilderForm.Ink, Font = DipBuilderForm.F(9.5f, false), ShowItemToolTips = true
            };
            _list.Columns.Add("Item", 260);
            _list.Columns.Add("Value", 560);
            _list.Columns.Add("Source", 400);
            var sectionFont = DipBuilderForm.F(9.5f, true);
            foreach (var section in rows.Select(r => r.Section).Distinct())
            {
                // A section heading row: ListView group headers ignore the theme colours.
                _list.Items.Add(new ListViewItem(new[] { section.ToUpperInvariant(), string.Empty, string.Empty })
                {
                    Font = sectionFont, ForeColor = DipBuilderForm.Accent, BackColor = DipBuilderForm.Ground, UseItemStyleForSubItems = true
                });
                foreach (var r in rows.Where(x => x.Section == section))
                {
                    var item = new ListViewItem(new[] { "   " + (r.Severity.Length > 0 ? "! " : string.Empty) + r.Item + (string.IsNullOrEmpty(r.Handle) ? string.Empty : "  >"), r.Value, r.Source ?? string.Empty })
                    {
                        Tag = r, ToolTipText = r.Value + (string.IsNullOrEmpty(r.Source) ? string.Empty : "\nSource: " + r.Source),
                        ForeColor = r.Severity == "Error" ? DipBuilderForm.Bad : r.Severity == "Warning" ? DipBuilderForm.Warn : DipBuilderForm.Ink
                    };
                    _list.Items.Add(item);
                }
            }
            _list.DoubleClick += (s, e) => GoToSelected();

            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 8, 12, 8), BackColor = DipBuilderForm.Surface };
            var close = Button("Close", false);
            close.Click += (s, e) => Close();
            var save = Button("Save report...", false);
            save.Click += (s, e) => SaveReport();
            var copy = Button("Copy report", false);
            copy.Click += (s, e) => { try { Clipboard.SetText(EasementInspectCommands.AsText(_title, _rows)); _status.Text = "Report copied."; } catch (System.Runtime.InteropServices.ExternalException) { } };
            var go = Button("Zoom to source", true);
            go.Click += (s, e) => GoToSelected();
            bar.Controls.Add(close);
            bar.Controls.Add(save);
            bar.Controls.Add(copy);
            bar.Controls.Add(go);
            _status = new Label { AutoSize = true, ForeColor = DipBuilderForm.Muted, Margin = new Padding(0, 8, 20, 0), Text = "Double-click a row marked > to zoom to its source object." };
            bar.Controls.Add(_status);

            Controls.Add(_list);
            Controls.Add(bar);
            Controls.Add(header);
        }

        private static Button Button(string text, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(8, 0, 0, 0), Padding = new Padding(8, 2, 8, 2),
                BackColor = primary ? DipBuilderForm.Accent : DipBuilderForm.Surface, ForeColor = primary ? Color.White : DipBuilderForm.Ink
            };
            b.FlatAppearance.BorderColor = primary ? DipBuilderForm.Accent : DipBuilderForm.ButtonBorder;
            return b;
        }

        private void SaveReport()
        {
            using (var dialog = new SaveFileDialog { Filter = "Text (*.txt)|*.txt", FileName = "FTF inspection.txt" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dialog.FileName, EasementInspectCommands.AsText(_title, _rows), new System.Text.UTF8Encoding(true));
                _status.Text = "Saved " + dialog.FileName;
            }
        }

        private void GoToSelected()
        {
            if (_list.SelectedItems.Count == 0) return;
            var row = _list.SelectedItems[0].Tag as InspectRow;
            if (row == null) return;
            if (string.IsNullOrEmpty(row.Handle)) { _status.Text = "That item has no CAD object to go to."; return; }
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                using (doc.LockDocument())
                {
                    long value;
                    if (!long.TryParse(row.Handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) return;
                    ObjectId id;
                    if (!doc.Database.TryGetObjectId(new Handle(value), out id) || id.IsErased) { _status.Text = "Object " + row.Handle + " is no longer in the drawing."; return; }
                    var target = string.IsNullOrEmpty(row.Layout) ? "Model" : row.Layout;
                    if (!string.Equals(LayoutManager.Current.CurrentLayout, target, StringComparison.OrdinalIgnoreCase))
                        LayoutManager.Current.CurrentLayout = target;
                    using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction())
                    {
                        var entity = tr.GetObject(id, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Entity;
                        if (entity != null)
                        {
                            UtilityCadService.ZoomTo(doc.Editor, entity.GeometricExtents);
                            doc.Editor.SetImpliedSelection(new[] { id });
                        }
                    }
                    _status.Text = "Showing " + row.Item + " (" + row.Handle + ").";
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                _status.Text = "Could not go to " + row.Handle + ": " + ex.Message;
            }
        }
    }
}
