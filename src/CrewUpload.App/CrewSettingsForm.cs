using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CrewUpload.Integration;

namespace CrewUpload.App
{
    /// <summary>
    /// The PM's editor for crew-settings.json: each person's initials (and who they are in the
    /// schedule), and which Schedule activities mean which work type. Saved to the shared Config
    /// folder the same safe way as the project list; a clash with another PM's save refreshes.
    /// </summary>
    internal sealed class CrewSettingsForm : Form
    {
        private readonly JobFolderConfig _config;
        private readonly CrewSettingsStore _store;
        private readonly IScheduleSource _schedule;
        private readonly DataGridView _crew;
        private readonly DataGridView _types;
        private readonly Label _notice;
        private readonly Button _save;
        private readonly List<ScheduledEmployee> _people;
        private DataGridViewComboBoxColumn _who;
        private List<EmployeeChoice> _choices = new List<EmployeeChoice>();
        private CrewSettings _loaded;

        public CrewSettingsForm(JobFolderConfig config, CrewSettingsStore store, IScheduleSource schedule)
        {
            _config = config;
            _store = store;
            _schedule = schedule;
            _people = schedule != null && (schedule.Refresh() || schedule.Available)
                ? schedule.Employees().ToList()
                : new List<ScheduledEmployee>();

            Text = "Crew and work types (PM)";
            Font = Theme.Body(10f);
            ForeColor = Theme.Charcoal;
            BackColor = Color.White;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(900, 600);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var crewTab = new TabPage("Crew initials") { BackColor = Color.White, Padding = new Padding(10) };
            var typeTab = new TabPage("Work types") { BackColor = Color.White, Padding = new Padding(10) };
            tabs.TabPages.Add(crewTab);
            tabs.TabPages.Add(typeTab);

            // ---- crew ----
            _crew = Grid();
            _crew.Columns.Add(new DataGridViewTextBoxColumn { Name = "initials", HeaderText = "Initials", Width = 70, MaxInputLength = 4 });
            _crew.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Name", Width = 200 });
            if (schedule != null)
            {
                // The cells hold the person's name (each choice's text is unique); the id is looked up when
                // saving. Plain text items behave the same on every runtime, unlike DisplayMember binding.
                _who = new DataGridViewComboBoxColumn { Name = "schedule", HeaderText = "Schedule person", Width = 230, FlatStyle = FlatStyle.Flat };
                _crew.Columns.Add(_who);
            }
            _crew.Columns.Add(new DataGridViewTextBoxColumn { Name = "user", HeaderText = "Windows sign-in", Width = 140 });
            _crew.Columns.Add(new DataGridViewCheckBoxColumn { Name = "active", HeaderText = "Active", Width = 60 });
            _crew.DefaultValuesNeeded += (s, e) => e.Row.Cells["active"].Value = true;
            _crew.CellEndEdit += (s, e) =>
            {
                var c = _crew.Rows[e.RowIndex].Cells[e.ColumnIndex];
                if (_crew.Columns[e.ColumnIndex].Name == "initials" && c.Value is string v) c.Value = CrewSettings.NormalizeInitials(v);
            };
            _crew.DataError += (s, e) => e.ThrowException = false; // a schedule person no longer in the roster
            var crewHelp = new Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = 58, ForeColor = Theme.MediumGray, Padding = new Padding(0, 0, 0, 8),
                Text = "Initials are what crews type and what goes in download names and daily reports (JBB). Link a person to the schedule so their"
                    + " day can be prefilled. Windows sign-in is optional; with it the app knows who is at the keyboard. People are made inactive, not deleted.",
            };
            var crewButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
            if (schedule != null)
            {
                var add = Theme.Button("Add people from the schedule", false);
                add.Click += (s, e) => AddFromSchedule();
                crewButtons.Controls.Add(add);
            }
            crewTab.Controls.Add(_crew);
            crewTab.Controls.Add(crewButtons);
            crewTab.Controls.Add(crewHelp);

            // ---- work types ----
            _types = Grid();
            _types.AllowUserToAddRows = false;
            _types.Columns.Add(new DataGridViewTextBoxColumn { Name = "code", HeaderText = "Code", Width = 90, ReadOnly = true });
            _types.Columns.Add(new DataGridViewTextBoxColumn { Name = "tname", HeaderText = "Work type", Width = 180, ReadOnly = true });
            _types.Columns.Add(new DataGridViewTextBoxColumn { Name = "activities", HeaderText = "Schedule activities (separate with commas)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            var known = schedule != null && schedule.Available ? schedule.Activities() : (IReadOnlyList<string>)new string[0];
            var typeHelp = new Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = 58, ForeColor = Theme.MediumGray, Padding = new Padding(0, 0, 0, 8),
                Text = "Which Schedule activities mean each work type, for suggesting the work type on a crew's report. Nothing maps until it is listed here,"
                    + " and the crew can always change it. Work type codes come from job-folders.json."
                    + (known.Count > 0 ? "\r\nActivities in the schedule: " + string.Join(", ", known) : string.Empty),
            };
            typeTab.Controls.Add(_types);
            typeTab.Controls.Add(typeHelp);

            // ---- bottom ----
            _notice = new Label { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(0, 8, 0, 0) };
            _save = Theme.Button("Save", true);
            _save.Click += (s, e) => Save();
            var refresh = Theme.Button("Refresh", false);
            refresh.Click += (s, e) => Reload(null);
            var close = Theme.Button("Close", false);
            close.DialogResult = DialogResult.Cancel;
            CancelButton = close;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            buttons.Controls.Add(_save);
            buttons.Controls.Add(refresh);
            buttons.Controls.Add(close);
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(12, 6, 12, 6) };
            bottom.Controls.Add(_notice);
            bottom.Controls.Add(buttons);

            Controls.Add(tabs);
            Controls.Add(bottom);
            Reload(null);
        }

        private static DataGridView Grid() => new DataGridView
        {
            Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, RowHeadersVisible = false,
            AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.CellSelect, EditMode = DataGridViewEditMode.EditOnEnter,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
        };

        private void Reload(string message)
        {
            try
            {
                _loaded = _store.Load();
                _save.Enabled = true;
                Say(message ?? "Saved in " + _store.FilePath, message != null);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
            {
                _loaded = null;
                _save.Enabled = false; // never save over a file that could not be read
                Say("Cannot read " + _store.FilePath + ": " + e.Message, true);
            }
            var s = _loaded ?? new CrewSettings();

            _crew.Rows.Clear();
            if (_who != null)
            {
                _choices = EmployeeChoices.Build(_people, s.Members.Select(m => m.ScheduleEmployeeId));
                _who.Items.Clear();
                _who.Items.AddRange(_choices.Select(c => (object)c.Display).ToArray());
                _crew.Rows.Clear(); // changing the items can leave an empty row behind
            }
            foreach (var m in s.Members.OrderBy(m => m.Initials, StringComparer.Ordinal))
            {
                var i = _crew.Rows.Add();
                var row = _crew.Rows[i];
                row.Cells["initials"].Value = m.Initials;
                row.Cells["name"].Value = m.Name;
                if (_schedule != null) row.Cells["schedule"].Value = DisplayOf(m.ScheduleEmployeeId);
                row.Cells["user"].Value = m.User;
                row.Cells["active"].Value = m.Active;
                if (!m.Active) row.DefaultCellStyle.ForeColor = Theme.MediumGray;
            }

            _types.Rows.Clear();
            var codes = _config.WorkTypes.Select(w => w.Code).ToList();
            foreach (var w in _config.WorkTypes)
                _types.Rows.Add(w.Code, w.Name, string.Join(", ", Activities(s, w.Code)));
            foreach (var m in s.ActivityMap.Where(m => !codes.Contains(m.WorkType, StringComparer.OrdinalIgnoreCase)))
                _types.Rows.Add(m.WorkType, "(not in job-folders.json)", string.Join(", ", m.Activities));
        }

        private string DisplayOf(string id) =>
            (_choices.FirstOrDefault(c => c.Id == (id ?? string.Empty)) ?? _choices.FirstOrDefault())?.Display ?? string.Empty;

        private string IdOf(string display)
        {
            var c = _choices.FirstOrDefault(x => x.Display == display);
            return c == null || c.Id.Length == 0 ? null : c.Id;
        }

        private static IEnumerable<string> Activities(CrewSettings s, string code) =>
            s.ActivityMap.Where(m => string.Equals(m.WorkType, code, StringComparison.OrdinalIgnoreCase)).SelectMany(m => m.Activities);

        private void AddFromSchedule()
        {
            var listed = new HashSet<string>(_crew.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow)
                .Select(r => IdOf(r.Cells["schedule"].Value as string)).Where(v => !string.IsNullOrEmpty(v)));
            var added = 0;
            foreach (var p in _people.Where(p => p.Active && !listed.Contains(p.Id)))
            {
                var i = _crew.Rows.Add();
                var row = _crew.Rows[i];
                row.Cells["name"].Value = p.Name;
                row.Cells["schedule"].Value = DisplayOf(p.Id);
                row.Cells["active"].Value = true;
                added++;
            }
            Say(added == 0 ? "Everyone active in the schedule is already listed." : added + " people added. Type their initials, then Save.", false);
        }

        private CrewSettings Collect()
        {
            var s = new CrewSettings();
            foreach (DataGridViewRow r in _crew.Rows)
            {
                if (r.IsNewRow) continue;
                var initials = CrewSettings.NormalizeInitials(r.Cells["initials"].Value as string);
                var name = ((r.Cells["name"].Value as string) ?? string.Empty).Trim();
                var id = _schedule != null ? IdOf(r.Cells["schedule"].Value as string) : null;
                var user = ((r.Cells["user"].Value as string) ?? string.Empty).Trim();
                if (initials.Length == 0 && name.Length == 0 && string.IsNullOrEmpty(id)) continue;
                s.Members.Add(new CrewMember
                {
                    Initials = initials, Name = name,
                    ScheduleEmployeeId = string.IsNullOrEmpty(id) ? null : id,
                    User = user.Length == 0 ? null : user,
                    Active = r.Cells["active"].Value as bool? ?? true,
                });
            }
            if (_schedule == null && _loaded != null)
            {
                // the schedule column is not shown without the integration: keep the links that were there
                foreach (var m in s.Members)
                    m.ScheduleEmployeeId = _loaded.Members.FirstOrDefault(o => o.Initials == m.Initials)?.ScheduleEmployeeId;
            }
            foreach (DataGridViewRow r in _types.Rows)
            {
                var list = ((r.Cells["activities"].Value as string) ?? string.Empty).Split(',').Select(a => a.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (list.Count > 0) s.ActivityMap.Add(new ActivityMapping { WorkType = (string)r.Cells["code"].Value, Activities = list });
            }
            return s;
        }

        private void Save()
        {
            _crew.EndEdit();
            _types.EndEdit();
            var next = Collect();
            var problems = next.Problems();
            if (problems != null)
            {
                Say(problems, true);
                return;
            }
            try
            {
                _loaded = _store.Save(next, Environment.UserName, _loaded?.Revision ?? 0);
                Reload("Saved.");
                _notice.ForeColor = Theme.MediumGray;
            }
            catch (RegistryConflictException e)
            {
                MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Reload("Refreshed with the other PM's change. Make yours again.");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
            {
                Say("Not saved: " + e.Message + " Details are in " + string.Join(" and ", _store.Log.Paths.Where(p => !string.IsNullOrEmpty(p))), true);
            }
        }

        private void Say(string text, bool warn)
        {
            _notice.Text = text;
            _notice.ForeColor = warn ? Theme.Red : Theme.MediumGray;
        }
    }
}
