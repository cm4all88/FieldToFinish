using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace FieldCodes.Cad.Ui
{
    /// <summary>
    /// Window preferences that belong to the person, not the standard: where the FTF window
    /// was and how big, and the Process preset last chosen. Kept in ui-state.json beside the
    /// user's settings; never part of a profile or the office file. A missing or unreadable
    /// file just means defaults.
    /// </summary>
    internal sealed class UiState
    {
        [JsonProperty("window")] public int[] Window { get; set; }
        [JsonProperty("maximized")] public bool Maximized { get; set; }
        [JsonProperty("processPreset")] public string ProcessPreset { get; set; }
        [JsonProperty("customStages")] public List<string> CustomStages { get; set; }
        [JsonProperty("customFamilies")] public List<string> CustomFamilies { get; set; }

        /// <summary>Commands this person put on the ribbon's My Commands panel (Settings > Commands).</summary>
        [JsonProperty("quickCommands")] public List<string> QuickCommands { get; set; }

        /// <summary>True on the instance that set <see cref="QuickCommands"/>. Any other instance
        /// (the FTF window keeps one open for hours) writes back what is on disk instead of the
        /// list it read at the start, so saving its window position cannot undo a tick.</summary>
        private bool _ownsQuickCommands;

        /// <summary>Saves the My Commands list, leaving everything else in the file as it is.</summary>
        public static void SaveQuickCommands(IEnumerable<string> commands)
        {
            var state = Load();
            state.QuickCommands = new List<string>(commands ?? new string[0]);
            state._ownsQuickCommands = true;
            state.Save();
        }

        private static string PathName()
        {
            var dir = Path.GetDirectoryName(FieldCodes.Settings.FtfSettings.UserProfilePath());
            return Path.Combine(dir ?? ".", "ui-state.json");
        }

        public static UiState Load()
        {
            try
            {
                var path = PathName();
                if (File.Exists(path))
                    return JsonConvert.DeserializeObject<UiState>(File.ReadAllText(path)) ?? new UiState();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
            return new UiState();
        }

        public void Save()
        {
            if (!_ownsQuickCommands) QuickCommands = Load().QuickCommands;
            try
            {
                var path = PathName();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Puts a form back where it was, if that place is still on a screen.</summary>
        public void Restore(Form form)
        {
            if (Window == null || Window.Length != 4) return;
            var bounds = new Rectangle(Window[0], Window[1], Math.Max(Window[2], form.MinimumSize.Width), Math.Max(Window[3], form.MinimumSize.Height));
            var visible = false;
            foreach (var screen in Screen.AllScreens)
                if (screen.WorkingArea.IntersectsWith(bounds)) { visible = true; break; }
            if (!visible) return;
            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = bounds;
            if (Maximized) form.WindowState = FormWindowState.Maximized;
        }

        public void Remember(Form form)
        {
            var b = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
            Window = new[] { b.X, b.Y, b.Width, b.Height };
            Maximized = form.WindowState == FormWindowState.Maximized;
        }
    }
}
