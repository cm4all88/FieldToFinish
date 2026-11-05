using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace FieldCodes.Editing
{
    /// <summary>Which table of the rules file a CSV supplies.</summary>
    public enum StandardsTable
    {
        /// <summary>code, name, layer, label, labelLayer, placement, aliases</summary>
        LineFeatures = 0,
        /// <summary>code, layer, label</summary>
        SymbolLabels = 1,
        /// <summary>code</summary>
        LineworkCodes = 2,
        /// <summary>code</summary>
        ControlCodes = 3,
        /// <summary>code</summary>
        IgnoreCodes = 4
    }

    /// <summary>What happened to one imported row. Every row gets one.</summary>
    public sealed class ImportRowResult
    {
        public int Line { get; set; }
        public string Code { get; set; }
        /// <summary>Added, Updated, Unchanged or Rejected.</summary>
        public string Disposition { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>The outcome of an import: the new rules text and a line-by-line account.</summary>
    public sealed class ImportReport
    {
        public IList<ImportRowResult> Rows { get; private set; }
        public string NewJson { get; set; }

        public ImportReport() { Rows = new List<ImportRowResult>(); }

        public int Count(string disposition)
        {
            return Rows.Count(r => r.Disposition == disposition);
        }

        public string Summary()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} row(s): {1} added, {2} updated, {3} unchanged, {4} rejected.",
                Rows.Count, Count("Added"), Count("Updated"), Count("Unchanged"), Count("Rejected"));
        }
    }

    /// <summary>
    /// Lets a firm bring its own standards in as a spreadsheet (CSV) instead of editing
    /// rules.json by hand. Pure text in, text out: no file or CAD access, so it is
    /// fully testable.
    ///
    /// Every data row is accounted for -- added, updated, unchanged or rejected with a
    /// reason. Nothing is dropped silently. Rows already in the rules are matched by
    /// code (case-insensitive) and only the columns the CSV supplies are changed;
    /// everything else on that entry, and every other part of the file, is kept.
    /// The result is validated by compiling it as the parser would; if it does not
    /// compile the import fails and no text is returned.
    /// </summary>
    public static class StandardsImport
    {
        private static readonly string[] Placements = { "Left", "Right", "OnLine" };

        public static ImportReport Apply(string rulesJson, StandardsTable table, string csv)
        {
            if (string.IsNullOrWhiteSpace(rulesJson)) throw new ArgumentException("rulesJson is empty.");
            if (csv == null) throw new ArgumentNullException("csv");

            var root = JObject.Parse(rulesJson);
            var report = new ImportReport();
            var rows = ParseCsv(csv);

            if (rows.Count == 0)
                throw new ConfigException("The CSV is empty. Expected a header row with a 'code' column.");

            var header = rows[0].Fields.Select(h => h.Trim().ToLowerInvariant()).ToList();
            var codeCol = header.IndexOf("code");
            if (codeCol < 0)
                throw new ConfigException("The CSV has no 'code' column in its header row.");

            var allowed = AllowedColumns(table);
            var unknown = header.Where(h => h.Length > 0 && !allowed.Contains(h)).ToList();
            if (unknown.Count > 0)
                throw new ConfigException("Unknown column(s) for " + table + ": " +
                    string.Join(", ", unknown.ToArray()) + ". Expected: " +
                    string.Join(", ", allowed.ToArray()) + ".");

            var key = KeyFor(table);
            var array = root[key] as JArray;
            if (array == null)
            {
                array = new JArray();
                root[key] = array;
            }

            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                var code = codeCol < row.Fields.Count ? row.Fields[codeCol].Trim() : "";
                var result = new ImportRowResult { Line = row.Line, Code = code };
                report.Rows.Add(result);

                if (code.Length == 0)
                {
                    Reject(result, "The code is blank.");
                    continue;
                }

                int firstLine;
                if (seen.TryGetValue(code, out firstLine))
                {
                    Reject(result, "Duplicate of line " + firstLine + " in this file; the first one was used.");
                    continue;
                }
                seen[code] = row.Line;

                if (table == StandardsTable.LineworkCodes ||
                    table == StandardsTable.ControlCodes ||
                    table == StandardsTable.IgnoreCodes)
                {
                    ApplyListRow(array, code, result);
                    continue;
                }

                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var c = 0; c < header.Count && c < row.Fields.Count; c++)
                {
                    if (header[c].Length == 0 || c == codeCol) continue;
                    values[header[c]] = row.Fields[c].Trim();
                }

                string problem;
                if (!ValidateValues(table, values, out problem))
                {
                    Reject(result, problem);
                    continue;
                }

                ApplyObjectRow(array, table, code, values, result);
            }

            report.NewJson = root.ToString(Newtonsoft.Json.Formatting.Indented);

            // The result must compile as the parser sees it, or nothing is returned.
            try
            {
                RuleDocument.FromText(report.NewJson).CompileCandidate();
            }
            catch (Exception ex)
            {
                throw new ConfigException("The imported standards do not form a valid configuration: " + ex.Message);
            }

            return report;
        }

        private static List<string> AllowedColumns(StandardsTable table)
        {
            switch (table)
            {
                case StandardsTable.LineFeatures:
                    return new List<string> { "code", "name", "layer", "label", "labellayer", "placement", "aliases" };
                case StandardsTable.SymbolLabels:
                    return new List<string> { "code", "layer", "label" };
                default:
                    return new List<string> { "code" };
            }
        }

        private static string KeyFor(StandardsTable table)
        {
            switch (table)
            {
                case StandardsTable.LineFeatures: return "lineFeatures";
                case StandardsTable.SymbolLabels: return "symbolLabels";
                case StandardsTable.LineworkCodes: return "lineworkCodes";
                case StandardsTable.ControlCodes: return "controlCodes";
                default: return "ignoreCodes";
            }
        }

        private static bool ValidateValues(StandardsTable table, Dictionary<string, string> values, out string problem)
        {
            problem = null;
            string placement;
            if (table == StandardsTable.LineFeatures && values.TryGetValue("placement", out placement) &&
                placement.Length > 0 &&
                !Placements.Any(p => string.Equals(p, placement, StringComparison.OrdinalIgnoreCase)))
            {
                problem = "Placement '" + placement + "' is not one of Left, Right, OnLine.";
                return false;
            }
            return true;
        }

        private static void ApplyListRow(JArray array, string code, ImportRowResult result)
        {
            var exists = array.Any(t => t.Type == JTokenType.String &&
                string.Equals((string)t, code, StringComparison.OrdinalIgnoreCase));
            if (exists)
            {
                result.Disposition = "Unchanged";
                result.Reason = "Already in the list.";
                return;
            }
            array.Add(code);
            result.Disposition = "Added";
        }

        private static void ApplyObjectRow(JArray array, StandardsTable table, string code,
            Dictionary<string, string> values, ImportRowResult result)
        {
            var entry = array.OfType<JObject>().FirstOrDefault(o =>
                string.Equals((string)o["code"], code, StringComparison.OrdinalIgnoreCase));
            var isNew = entry == null;
            if (isNew) entry = new JObject { ["code"] = code };

            var changed = isNew;
            foreach (var kv in values)
            {
                var value = kv.Value;
                if (value.Length == 0) continue; // blank cell = leave as it is

                var prop = PropertyName(kv.Key);
                if (kv.Key == "aliases")
                {
                    var wanted = value.Split(new[] { '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
                    var current = (entry[prop] as JArray)?.Select(t => (string)t).ToList() ?? new List<string>();
                    var merged = current.Concat(wanted.Where(w =>
                        !current.Contains(w, StringComparer.OrdinalIgnoreCase))).ToList();
                    if (merged.Count != current.Count)
                    {
                        entry[prop] = new JArray(merged);
                        changed = true;
                    }
                    continue;
                }

                if (kv.Key == "placement")
                    value = Placements.First(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase));

                if ((string)entry[prop] != value)
                {
                    entry[prop] = value;
                    changed = true;
                }
            }

            if (isNew) array.Add(entry);

            result.Disposition = isNew ? "Added" : (changed ? "Updated" : "Unchanged");
        }

        private static string PropertyName(string column)
        {
            return column == "labellayer" ? "labelLayer" : column;
        }

        private static void Reject(ImportRowResult result, string reason)
        {
            result.Disposition = "Rejected";
            result.Reason = reason;
        }

        // ---- CSV ---------------------------------------------------------------

        private sealed class CsvRow
        {
            public int Line;
            public List<string> Fields = new List<string>();
        }

        /// <summary>RFC 4180 style: commas, double-quoted fields, "" for a quote, and
        /// line breaks inside quotes. Blank lines are skipped; they are not data rows.</summary>
        private static List<CsvRow> ParseCsv(string text)
        {
            var rows = new List<CsvRow>();
            var field = new StringBuilder();
            var row = new CsvRow { Line = 1 };
            var inQuotes = false;
            var line = 1;
            var rowHasContent = false;

            Action endField = () =>
            {
                row.Fields.Add(field.ToString());
                field.Length = 0;
            };
            Action endRow = () =>
            {
                endField();
                if (rowHasContent) rows.Add(row);
                row = new CsvRow { Line = line + 1 };
                rowHasContent = false;
            };

            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);

            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else
                    {
                        if (ch == '\n') line++;
                        field.Append(ch);
                    }
                    continue;
                }

                if (ch == '"') { inQuotes = true; rowHasContent = true; }
                else if (ch == ',') { endField(); rowHasContent = true; }
                else if (ch == '\r') { /* handled with \n */ }
                else if (ch == '\n') { endRow(); line++; row.Line = line; }
                else { field.Append(ch); if (!char.IsWhiteSpace(ch)) rowHasContent = true; }
            }
            if (field.Length > 0 || row.Fields.Count > 0) endRow();

            return rows;
        }
    }
}
