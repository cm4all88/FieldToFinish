using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace FieldCodes.Settings
{
    /// <summary>
    /// How FTF reports the codes it has nothing configured for. Today one decision:
    /// codes the office has looked at and deliberately chosen not to configure.
    ///
    /// <c>"diagnostics": { "ignoredCodes": [ "XYZ", "TEMP" ] }</c>
    ///
    /// An ignored code is skipped the way a linework code is: only when no rule
    /// matches the description. A rule added for the code later still wins, so an
    /// ignore can never silently swallow a configured feature. Ignored points are
    /// reported as Informational ("marked ignored by you"), never as unknown codes,
    /// and are left out of the unknown-codes CSV.
    ///
    /// A file written before this section existed loads with an empty list.
    /// </summary>
    public sealed class DiagnosticsSettings : ISettingsSection
    {
        public string Title { get { return "Diagnostics"; } }
        public string AffectedCommands { get { return "FTFPOINTS, FTF window (Overview, Unknown Codes, Reports)"; } }

        /// <summary>Bare field codes, as shot. A trailing sequence number is matched
        /// too, so XYZ covers XYZ2 -- the same rule as the rules file's code lists.</summary>
        [JsonProperty("ignoredCodes", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> IgnoredCodes { get; set; }

        public DiagnosticsSettings() { RestoreDefaults(); }

        public void RestoreDefaults()
        {
            IgnoredCodes = new List<string>();
        }

        public void Validate(ICollection<string> problems)
        {
            if (IgnoredCodes == null) IgnoredCodes = new List<string>();

            foreach (var code in IgnoredCodes)
            {
                if (string.IsNullOrWhiteSpace(code)) continue;   // dropped on use
                if (!IsPlainCode(code.Trim()))
                    problems.Add(string.Format(
                        "Diagnostics: ignored code '{0}' must be a plain field code (letters, digits, - or _).",
                        code.Trim()));
            }
        }

        // ------------------------------------------------------------------ queries

        /// <summary>The usable entries: trimmed, upper case, plain codes only, no
        /// duplicates, in the order they were added.</summary>
        public IList<string> Codes()
        {
            var result = new List<string>();
            foreach (var raw in IgnoredCodes ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var code = raw.Trim().ToUpperInvariant();
                if (!IsPlainCode(code)) continue;
                if (!result.Contains(code)) result.Add(code);
            }
            return result;
        }

        /// <summary>True when <paramref name="code"/> is ignored: equal to an entry,
        /// or an entry followed only by digits (XYZ2 under XYZ). Case-insensitive.</summary>
        public bool IsIgnored(string code)
        {
            return Matches(Codes(), code);
        }

        /// <summary>The same test against any list of entries.</summary>
        public static bool Matches(IEnumerable<string> entries, string code)
        {
            if (entries == null || string.IsNullOrWhiteSpace(code)) return false;
            var c = code.Trim();

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                var e = entry.Trim();
                if (c.Length < e.Length) continue;
                if (!c.StartsWith(e, StringComparison.OrdinalIgnoreCase)) continue;

                var rest = c.Substring(e.Length);
                if (rest.All(char.IsDigit)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ editing

        /// <summary>Adds a code. False when it is blank, not a plain code, or
        /// already ignored.</summary>
        public bool Ignore(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            var c = code.Trim().ToUpperInvariant();
            if (!IsPlainCode(c)) return false;
            if (IgnoredCodes == null) IgnoredCodes = new List<string>();
            if (IgnoredCodes.Any(x => string.Equals((x ?? string.Empty).Trim(), c,
                                                    StringComparison.OrdinalIgnoreCase)))
                return false;

            IgnoredCodes.Add(c);
            return true;
        }

        /// <summary>Removes a code (exact entry, case-insensitive). False when it was
        /// not in the list.</summary>
        public bool Unignore(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || IgnoredCodes == null) return false;
            var c = code.Trim();
            return IgnoredCodes.RemoveAll(x => string.Equals((x ?? string.Empty).Trim(), c,
                                                             StringComparison.OrdinalIgnoreCase)) > 0;
        }

        /// <summary>The entry that makes <paramref name="code"/> ignored (XYZ for
        /// XYZ2), or null.</summary>
        public string EntryFor(string code)
        {
            foreach (var entry in Codes())
                if (Matches(new[] { entry }, code)) return entry;
            return null;
        }

        // --------------------------------------------------------------- the parser

        private sealed class Applied
        {
            public IList<Regex> List;
            public List<Regex> Original;
            public string Signature;
        }

        private static readonly ConditionalWeakTable<RulesConfig, Applied> AppliedTo =
            new ConditionalWeakTable<RulesConfig, Applied>();

        /// <summary>
        /// Makes the parser skip ignored codes. They join the list the parser
        /// consults only after no rule matched (the linework list), so a code with a
        /// rule is never affected and the point simply reads as Ignored -- the
        /// unknown-codes report and the review stop listing it.
        ///
        /// Idempotent: the rules object is cached for the session and this runs on
        /// every command, so the rules' own linework patterns are remembered the
        /// first time and the user's entries are re-applied over them, never stacked.
        /// </summary>
        public void ApplyTo(RulesConfig rules)
        {
            if (rules == null || rules.LineworkPatterns == null) return;

            // A bare code the office labels with its code (symbolLabels) is checked by
            // the parser after this list, so an entry for it would silently stop those
            // labels. Such an entry is reported as ignored but never reaches the parser.
            var codes = Codes().Where(c => !IsSymbolLabelCode(rules, c)).ToList();
            var signature = string.Join("|", codes.ToArray());

            lock (AppliedTo)
            {
                Applied state;
                if (!AppliedTo.TryGetValue(rules, out state) ||
                    !ReferenceEquals(state.List, rules.LineworkPatterns))
                {
                    // First sight of this list (or Compile replaced it): what it holds
                    // now is the rules file's own linework.
                    AppliedTo.Remove(rules);
                    state = new Applied
                    {
                        List = rules.LineworkPatterns,
                        Original = new List<Regex>(rules.LineworkPatterns),
                        Signature = string.Empty
                    };
                    AppliedTo.Add(rules, state);
                }

                if (state.Signature == signature) return;

                var list = rules.LineworkPatterns;
                list.Clear();
                foreach (var pattern in state.Original) list.Add(pattern);
                foreach (var code in codes) list.Add(PatternFor(code));
                state.Signature = signature;
            }
        }

        /// <summary>True when <paramref name="entry"/> (sequence digits ignored) is a
        /// code listed in the rules' symbolLabels.</summary>
        internal static bool IsSymbolLabelCode(RulesConfig rules, string entry)
        {
            if (rules == null || rules.SymbolLabels == null || string.IsNullOrWhiteSpace(entry)) return false;
            var e = entry.Trim();
            var end = e.Length;
            while (end > 0 && char.IsDigit(e[end - 1])) end--;
            var baseCode = end > 0 ? e.Substring(0, end) : e;

            return rules.SymbolLabels.Any(sl => sl != null && sl.Enabled && !string.IsNullOrWhiteSpace(sl.Code) &&
                (string.Equals(sl.Code.Trim(), baseCode, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(sl.Code.Trim(), e, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>The same anchored shape as the rules file's code lists:
        /// the code, optional sequence digits, then a space or the end.</summary>
        internal static Regex PatternFor(string code)
        {
            return new Regex("^" + Regex.Escape(code) + "[0-9]*(?:\\s|$)",
                             RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool IsPlainCode(string code)
        {
            return !string.IsNullOrEmpty(code) && Regex.IsMatch(code, @"^[A-Za-z0-9_-]+$");
        }

        // ------------------------------------------------------------------ saving

        /// <summary>
        /// Where a change made outside the Settings page (marking a code ignored on
        /// Unknown Codes) is saved: back to the file that is in force when that is a
        /// drawing-folder or profile file, otherwise the user's own settings -- the
        /// shipped plugin copy is replaced by every deploy and is never written.
        /// Mirrors the Settings page's default save target.
        /// </summary>
        public static string SavePathFor(SettingsResolution resolution)
        {
            if (resolution != null && !string.IsNullOrWhiteSpace(resolution.LoadedFrom) &&
                (resolution.Source == SettingsSource.DrawingFolder ||
                 resolution.Source == SettingsSource.Profile ||
                 resolution.Source == SettingsSource.UserProfile))
                return resolution.LoadedFrom;

            return FtfSettings.UserProfilePath();
        }
    }
}
