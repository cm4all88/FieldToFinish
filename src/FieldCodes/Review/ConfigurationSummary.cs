using FieldCodes.Editing;
using FieldCodes.Settings;

namespace FieldCodes.Review
{
    /// <summary>
    /// Says which settings and rules files are in force, in the words the window
    /// uses. When a value on the Standards page looks wrong, the first question is
    /// always "which file is it reading?".
    /// </summary>
    public static class ConfigurationSummary
    {
        public static string SettingsSourceText(SettingsSource source, string profileName)
        {
            switch (source)
            {
                case SettingsSource.Profile:
                    return "Drafting profile \"" + (profileName ?? "?") + "\"";
                case SettingsSource.DrawingFolder:
                    return "This drawing's folder";
                case SettingsSource.UserProfile:
                    return "Your settings (all drawings)";
                case SettingsSource.PluginFolder:
                    return "Shipped with the plugin";
                default:
                    return "Built-in defaults (no settings file found)";
            }
        }

        /// <summary>"Drafting profile "WSDOT" - C:\...\WSDOT.json", plus a warning when
        /// the drawing selected a profile that is not on this computer.</summary>
        public static string DescribeSettings(SettingsResolution resolution)
        {
            if (resolution == null) return "Settings not resolved";

            var text = SettingsSourceText(resolution.Source, resolution.ProfileName);
            if (!string.IsNullOrWhiteSpace(resolution.LoadedFrom))
                text += " - " + resolution.LoadedFrom;
            if (resolution.Source == SettingsSource.Defaults && resolution.MigratedFromRules)
                text += " (seeded from the rules file)";
            if (!string.IsNullOrWhiteSpace(resolution.MissingProfile))
                text += ". The drawing selects profile \"" + resolution.MissingProfile +
                        "\", which is not on this computer.";
            return text;
        }

        public static string RulesSourceText(RulesSource source)
        {
            switch (source)
            {
                case RulesSource.Drawing: return "Drawing override";
                case RulesSource.Office: return "Office configuration";
                default: return "Factory defaults (shipped)";
            }
        }

        public static string DescribeRules(RulesResolution resolution)
        {
            if (resolution == null || string.IsNullOrWhiteSpace(resolution.ActivePath))
                return "No rules file found";
            return RulesSourceText(resolution.Source) + " - " + resolution.ActivePath;
        }
    }
}
