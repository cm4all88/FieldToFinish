using System;
using System.Linq;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using FieldCodes.Settings;

namespace FieldCodes.Cad
{
    /// <summary>
    /// Drafting profiles: named settings files (layers, styles, thresholds, label
    /// formats) a project or client uses. The drawing remembers which profile it
    /// uses, so everyone opening it drafts the same way. No profile name is
    /// required -- without one the normal settings apply.
    /// </summary>
    public sealed class ProfileCommands
    {
        /// <summary>Shows, selects, creates or clears the drawing's drafting profile.</summary>
        [CommandMethod("FTFPROFILE", CommandFlags.Modal)]
        public void Profile()
        {
            FtfSession.Run("FTFPROFILE", (db, tr, ed) =>
            {
                var rules = FtfSession.Rules(db);
                var resolution = FtfSession.ResolveSettings(db, rules);
                var current = DrawingStore.ReadProfileName(db);
                var profiles = FtfSettings.ListProfiles();

                ed.WriteMessage("\nDrafting profile for this drawing: {0}", string.IsNullOrWhiteSpace(current) ? "(none)" : current);
                if (!string.IsNullOrWhiteSpace(resolution.MissingProfile))
                    ed.WriteMessage("\n  ! Profile \"{0}\" is not on this computer ({1}); normal settings are in use.",
                                    resolution.MissingProfile, FtfSettings.ProfilesDirectory());
                if (resolution.Source == SettingsSource.DrawingFolder && !string.IsNullOrWhiteSpace(current))
                    ed.WriteMessage("\n  ! A settings file beside the drawing overrides the profile: {0}", resolution.LoadedFrom);
                ed.WriteMessage("\nSettings in use come from: {0}", resolution.LoadedFrom ?? "shipped defaults");
                ed.WriteMessage("\nProfiles on this computer: {0}", profiles.Count == 0 ? "(none yet)" : string.Join(", ", profiles.ToArray()));
                ed.WriteMessage("\n");

                var options = new PromptKeywordOptions("\n[Use/SaveAs/Clear/List] <exit>: ", "Use SaveAs Clear List");
                options.AllowNone = true;
                var answer = ed.GetKeywords(options);
                if (answer.Status != PromptStatus.OK) return;

                switch (answer.StringResult)
                {
                    case "Use":
                    {
                        var name = ed.GetString(new PromptStringOptions("\nProfile name to use: ") { AllowSpaces = true });
                        if (name.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(name.StringResult)) return;
                        var chosen = profiles.FirstOrDefault(p => string.Equals(p, name.StringResult.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (chosen == null)
                        {
                            ed.WriteMessage("\nNo profile \"{0}\" on this computer. Use SaveAs to create it from the current settings first.", name.StringResult.Trim());
                            return;
                        }
                        DrawingStore.WriteProfileName(db, tr, chosen);
                        FtfSession.InvalidateSettings();
                        ed.WriteMessage("\nThis drawing now drafts with profile \"{0}\". Existing drafting is not changed.", chosen);
                        break;
                    }
                    case "SaveAs":
                    {
                        var name = ed.GetString(new PromptStringOptions("\nNew profile name (project or client): ") { AllowSpaces = true });
                        if (name.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(name.StringResult)) return;
                        var trimmed = name.StringResult.Trim();
                        if (FtfSettings.ProfileExists(trimmed))
                        {
                            var overwrite = new PromptKeywordOptions("\nProfile \"" + trimmed + "\" exists. Overwrite it with the settings in use? [Yes/No] <No>: ", "Yes No");
                            overwrite.Keywords.Default = "No";
                            var ok = ed.GetKeywords(overwrite);
                            if (ok.Status != PromptStatus.OK || ok.StringResult != "Yes") return;
                        }
                        FtfSettings.SaveProfile(trimmed, resolution.Settings);
                        DrawingStore.WriteProfileName(db, tr, trimmed);
                        FtfSession.InvalidateSettings();
                        ed.WriteMessage("\nSaved profile \"{0}\" to {1} and selected it for this drawing. Edit it in FTF > Settings (save to the profile).",
                                        trimmed, FtfSettings.ProfilePath(trimmed));
                        break;
                    }
                    case "Clear":
                        DrawingStore.WriteProfileName(db, tr, string.Empty);
                        FtfSession.InvalidateSettings();
                        ed.WriteMessage("\nThis drawing no longer uses a drafting profile; normal settings apply.");
                        break;
                    default:
                        foreach (var p in profiles) ed.WriteMessage("\n  {0}  ({1})", p, FtfSettings.ProfilePath(p));
                        break;
                }
                ed.WriteMessage("\n");
            });
        }
    }
}
