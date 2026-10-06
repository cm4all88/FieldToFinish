using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CrewUpload.Reports
{
    /// <summary>
    /// Draws a daily report the way Form 03-SV-125-GW lays it out: the job block, equipment and
    /// vehicles with their check boxes, mileage, comments, the safety lists, extras and the signature
    /// line. Long comments carry on to a second page.
    /// </summary>
    internal static class DailyReportPdf
    {
        private const float Left = 36f, Right = 576f, Bottom = 742f;

        public static byte[] Render(DailyReport r, DailyReportSettings form)
        {
            var pdf = new PdfDocument { Title = (form.Title ?? "Daily report") + " " + r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " " + r.CrewInitials };
            var page = 0;
            float y = 0;

            Action newPage = () =>
            {
                pdf.NewPage();
                page++;
                Footer(pdf, r, page);
                pdf.TextRight(Right, 28, form.FormNumber, 7);
                y = 40;
                if (page > 1)
                {
                    pdf.Text(Left, 50, (form.Title ?? string.Empty) + " (continued)  " + r.Date.ToString("M-d-yy", CultureInfo.InvariantCulture) + "  " + r.ProjectNumber, 9, true);
                    y = 70;
                }
            };
            Action<float> ensure = h => { if (y + h > Bottom) newPage(); };

            newPage();
            pdf.TextCentered(306, 58, form.Title, 15, true);
            pdf.TextCentered(306, 73, form.Team, 10);
            y = 96;

            // ---- job block
            var members = r.Crew.Skip(1).Select(c => c.Name ?? c.Initials).ToList();
            var leftRows = new List<KeyValuePair<string, string>>
            {
                Row("Job Name:", r.ProjectName),
                Row("Crew Name:", r.Lead == null ? string.Empty : r.Lead.Name ?? r.Lead.Initials),
                Row(null, null), // solo / crew line
                Row("Hours:", r.Hours.HasValue ? r.Hours.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty),
                Row("Data File Name:", r.DataFileName),
                Row("Control File:", r.ControlFile),
            };
            var rightRows = new List<KeyValuePair<string, string>>
            {
                Row("Date:", r.Date.ToString("M-d-yy", CultureInfo.InvariantCulture)),
                Row("Project #:", r.ProjectNumber),
                Row("Task #:", r.TaskNumber),
                Row("Subtask #:", r.Subtask),
                Row("Work Type:", r.WorkType),
                Row("Work Order:", r.WorkOrder),
            };
            if (!string.IsNullOrWhiteSpace(r.Weather)) rightRows.Add(Row("Weather:", r.Weather));
            for (var i = 0; i < Math.Max(leftRows.Count, rightRows.Count); i++)
            {
                var rowY = y + i * 18;
                if (i < leftRows.Count)
                {
                    if (leftRows[i].Key == null)
                    {
                        pdf.CheckBox(Left, rowY - 7, members.Count == 0);
                        pdf.Text(Left + 12, rowY, "Solo Crew", 9);
                        pdf.CheckBox(Left + 70, rowY - 7, members.Count > 0);
                        pdf.Text(Left + 82, rowY, members.Count > 1 ? "Crew:" : "Two-Person Crew:", 9);
                        Field(pdf, Left + (members.Count > 1 ? 112 : 160), rowY, 300 - (members.Count > 1 ? 112 : 160), string.Join(" / ", members));
                    }
                    else Labeled(pdf, Left, rowY, 88, 300, leftRows[i].Key, leftRows[i].Value);
                }
                if (i < rightRows.Count) Labeled(pdf, 350, rowY, 64, Right - 350, rightRows[i].Key, rightRows[i].Value);
            }
            y += Math.Max(leftRows.Count, rightRows.Count) * 18 + 8;

            // ---- equipment (left) and vehicles (right)
            pdf.Line(Left, y, Right, y, 0.8f);
            y += 14;
            pdf.Text(Left, y, "Survey Equipment", 9, true);
            pdf.Text(Left, y + 9, "(check equipment)", 7);
            pdf.Text(170, y, "Vehicle and Mileage (check vehicle)", 9, true);
            var top = y + 18;

            var ey = top;
            var equipment = form.Equipment.Select(e => new { e.Name, e.Assets }).ToList();
            equipment.Add(new { Name = form.NoEquipment, Assets = "(N/A)" });
            foreach (var e in r.Equipment.Where(x => !equipment.Any(q => Same(q.Name, x))).ToList())
                equipment.Add(new { Name = e, Assets = string.Empty });
            foreach (var e in equipment)
            {
                pdf.CheckBox(Left, ey - 7, r.Equipment.Any(x => Same(x, e.Name)));
                pdf.Text(Left + 12, ey, e.Name, 8.5f);
                pdf.Text(Left + 12, ey + 9, e.Assets, 7, false, 0.4f);
                ey += 24;
            }

            // personal auto + mileage
            var personal = Same(r.Vehicle, form.PersonalAuto);
            pdf.CheckBox(170, top - 7, personal);
            pdf.Text(182, top, form.PersonalAuto + ":", 8.5f);
            Field(pdf, 245, top, 140, personal ? r.PersonalAutoOwner : string.Empty);
            pdf.Text(182, top + 9, "For personal auto, put vehicle owner name above", 6.5f, false, 0.4f);
            Labeled(pdf, 410, top - 10, 50, 110, "Start:", r.Mileage.Start?.ToString(CultureInfo.InvariantCulture));
            Labeled(pdf, 410, top + 4, 50, 110, "Finish:", r.Mileage.Finish?.ToString(CultureInfo.InvariantCulture));
            Labeled(pdf, 410, top + 18, 50, 110, "Total Miles:", r.Mileage.Total?.ToString(CultureInfo.InvariantCulture));

            var vy = top + 34;
            var vehicles = form.Vehicles.ToList();
            if (!string.IsNullOrEmpty(r.Vehicle) && !personal && !vehicles.Any(v => Same(v.Id, r.Vehicle)))
                vehicles.Add(new ReportVehicle { Id = r.Vehicle, Description = string.Empty, Assets = string.Empty });
            var perColumn = (int)Math.Ceiling(vehicles.Count / 3.0);
            for (var i = 0; i < vehicles.Count; i++)
            {
                var v = vehicles[i];
                var x = 170 + (i / perColumn) * 136;
                var cy = vy + (i % perColumn) * 30;
                pdf.CheckBox(x, cy - 7, Same(r.Vehicle, v.Id));
                pdf.Text(x + 12, cy, v.Id, 8.5f, true);
                pdf.Text(x + 12, cy + 8.5f, v.Description, 7);
                pdf.Text(x + 12, cy + 16.5f, v.Assets, 7, false, 0.4f);
            }
            y = Math.Max(ey, vy + perColumn * 30) + 4;

            // ---- comments
            pdf.Line(Left, y, Right, y, 0.8f);
            y += 14;
            y = Paragraph(pdf, "Comments:", r.Notes, y, 48, newPage, () => y, v => y = v);

            // ---- safety
            y = CheckList(pdf, "Site Safety Observations:", form.SafetyObservations, r.SafetyObservations, y, ensure, () => y);
            y = CheckList(pdf, "Site Safety Precautions:", form.SafetyPrecautions, r.SafetyPrecautions, y, ensure, () => y);

            // ---- extras, with the signature line beside them
            var extras = PdfDocument.Wrap(r.Extras, 230, 9.5f);
            ensure(Math.Max(40, 14 + extras.Count * 12));
            pdf.Text(Left, y, "Extras:", 9, true);
            var xy = y;
            foreach (var line in extras)
            {
                pdf.Text(Left + 66, xy, line, 9.5f, false, 0.05f);
                xy += 12;
            }
            pdf.Line(350, y + 22, 560, y + 22, 0.6f);
            pdf.Text(400, y + 32, "Extra Authorization Signature", 8);

            return pdf.ToBytes();
        }

        private static KeyValuePair<string, string> Row(string k, string v) => new KeyValuePair<string, string>(k, v);

        private static bool Same(string a, string b) => string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

        private static void Labeled(PdfDocument pdf, float x, float y, float labelWidth, float width, string label, string value)
        {
            pdf.Text(x, y, label, 9, true);
            Field(pdf, x + labelWidth, y, width - labelWidth, value);
        }

        /// <summary>A filled-in value on its line; shrunk to fit if it is long.</summary>
        private static void Field(PdfDocument pdf, float x, float y, float width, string value)
        {
            pdf.Line(x, y + 3, x + width, y + 3, 0.4f, 0.55f);
            if (string.IsNullOrEmpty(value)) return;
            var size = 10f;
            while (size > 6 && PdfDocument.Measure(value, size) > width - 4) size -= 0.5f;
            var text = value;
            while (text.Length > 1 && PdfDocument.Measure(text, size) > width - 4) text = text.Substring(0, text.Length - 1);
            pdf.Text(x + 2, y, text, size, false, 0.05f);
        }

        private static float Paragraph(PdfDocument pdf, string label, string text, float y, float minHeight,
            Action newPage, Func<float> current, Action<float> set)
        {
            var lines = PdfDocument.Wrap(text, Right - Left - 70, 9.5f);
            if (y + Math.Min(minHeight, 14 + lines.Count * 12) > Bottom) { newPage(); y = current(); }
            pdf.Text(Left, y, label, 9, true);
            var ly = y;
            foreach (var line in lines)
            {
                if (ly > Bottom) { newPage(); ly = current(); pdf.Text(Left, ly, label + " (continued)", 9, true); }
                pdf.Text(Left + 66, ly, line, 9.5f, false, 0.05f);
                ly += 12;
            }
            y = Math.Max(ly, y + minHeight) + 4;
            set(y);
            return y;
        }

        private static float CheckList(PdfDocument pdf, string label, List<string> options, List<string> chosen, float y,
            Action<float> ensure, Func<float> current)
        {
            var items = options.ToList();
            items.AddRange(chosen.Where(c => !options.Any(o => Same(o, c)))); // anything the crew added
            var rows = (int)Math.Ceiling(items.Count / 3.0);
            ensure(20 + rows * 12);
            y = current();
            pdf.Text(Left, y, label, 9, true);
            y += 14;
            for (var i = 0; i < items.Count; i++)
            {
                var x = Left + (i / rows) * 180;
                var cy = y + (i % rows) * 12;
                pdf.CheckBox(x, cy - 7, chosen.Any(c => Same(c, items[i])));
                pdf.Text(x + 12, cy, items[i], 8);
            }
            return y + rows * 12 + 6;
        }

        private static void Footer(PdfDocument pdf, DailyReport r, int page)
        {
            pdf.Line(Left, 756, Right, 756, 0.4f, 0.6f);
            var who = r.SubmittedBy + (r.SubmittedTime.HasValue ? " " + r.SubmittedTime.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : string.Empty);
            pdf.Text(Left, 766, "Report " + r.ReportId + "   Submitted by " + who
                + (r.Source == "schedule" ? "   Started from the Survey Schedule; as submitted by the crew." : string.Empty), 6.5f, false, 0.4f);
            pdf.TextRight(Right, 766, "Page " + page, 6.5f);
        }
    }
}
