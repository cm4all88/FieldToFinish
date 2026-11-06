using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace FieldCodes.TraceUi
{
    /// <summary>
    /// What the TRACE palette remembers between sessions, per user: which Run TRACE steps
    /// are ticked and whether the palette was open. Stored beside the other TRACE settings.
    /// A missing or unreadable file means "first run": defaults apply and nothing fails.
    /// </summary>
    public sealed class TraceUiState
    {
        public const string FileName = "trace-ui.json";

        /// <summary>
        /// Steps left unticked until the technician turns them on. The bulk line-label pass is
        /// optional by design: click-to-place line labels are the normal way.
        /// </summary>
        private static readonly string[] OffByDefault = { "FTFLINELABELS" };

        /// <summary>Ticked pipeline step names. Null means nothing has been chosen yet.</summary>
        [JsonProperty("runSteps")]
        public List<string> RunSteps { get; set; }

        /// <summary>Whether the palette was open when it was last used. Null means never used.</summary>
        [JsonProperty("paletteVisible")]
        public bool? PaletteVisible { get; set; }

        public static string DefaultPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Branding.ConfigFolderName,
                FileName);
        }

        public static TraceUiState Load(string path)
        {
            try
            {
                if (path != null && File.Exists(path))
                {
                    var state = JsonConvert.DeserializeObject<TraceUiState>(File.ReadAllText(path));
                    if (state != null) return state;
                }
            }
            catch (Exception)
            {
                // A damaged file must never stop TRACE starting: fall back to defaults.
            }
            return new TraceUiState();
        }

        /// <summary>Saves the state. Failure to save is not fatal; returns false then.</summary>
        public bool Save(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonConvert.SerializeObject(this, Formatting.Indented));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// The steps to run, in the order given by allSteps (the pipeline decides order, never
        /// the saved list). With nothing saved, every step except the optional ones.
        /// Names the pipeline no longer has are ignored; new steps follow the default.
        /// </summary>
        public IList<string> SelectedSteps(IEnumerable<string> allSteps)
        {
            var all = allSteps.ToList();
            if (RunSteps == null)
                return all.Where(s => !OffByDefault.Contains(s, StringComparer.OrdinalIgnoreCase)).ToList();

            var saved = new HashSet<string>(RunSteps, StringComparer.OrdinalIgnoreCase);
            return all.Where(saved.Contains).ToList();
        }

        /// <summary>Records which of allSteps are ticked.</summary>
        public void SetSelectedSteps(IEnumerable<string> ticked)
        {
            RunSteps = ticked.ToList();
        }
    }
}
