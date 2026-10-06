using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace CrewUpload.Reports
{
    /// <summary>One person on the day's crew.</summary>
    public sealed class ReportCrewMember
    {
        [JsonProperty("Initials")] public string Initials { get; set; }
        [JsonProperty("Name")] public string Name { get; set; }
        [JsonProperty("ScheduleEmployeeID", NullValueHandling = NullValueHandling.Ignore)] public string ScheduleEmployeeId { get; set; }
        public override string ToString() => string.IsNullOrEmpty(Name) ? Initials : Name + " (" + Initials + ")";
    }

    /// <summary>A calendar day as yyyy-MM-dd, the same on every PC whatever its time zone.</summary>
    internal sealed class DayConverter : Newtonsoft.Json.Converters.IsoDateTimeConverter
    {
        public DayConverter() { DateTimeFormat = "yyyy-MM-dd"; }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Date) return ((DateTime)reader.Value).Date;
            var s = reader.Value as string;
            DateTime d;
            if (s != null && s.Length >= 10 && DateTime.TryParseExact(s.Substring(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return d;
            return base.ReadJson(reader, objectType, existingValue, serializer);
        }
    }

    public sealed class ReportMileage
    {
        [JsonProperty("Start")] public int? Start { get; set; }
        [JsonProperty("Finish")] public int? Finish { get; set; }
        [JsonProperty("Total")] public int? Total => Start.HasValue && Finish.HasValue ? Finish - Start : (int?)null;
    }

    /// <summary>
    /// A crew's daily field report: what actually happened that day. Anything prefilled from the
    /// schedule is only a starting point; this records what the crew submitted. The property names
    /// are the structured record's field names.
    /// </summary>
    public sealed class DailyReport
    {
        [JsonProperty("ReportID")] public string ReportId { get; set; }
        /// <summary>The day worked, written as 2026-05-07 (no time, no time zone).</summary>
        [JsonProperty("Date")] [JsonConverter(typeof(DayConverter))] public DateTime Date { get; set; }

        /// <summary>The full 3-4-3 project number when known (554-1800-119), else what the crew typed.</summary>
        [JsonProperty("ProjectNumber")] public string ProjectNumber { get; set; }
        [JsonProperty("ProjectName")] public string ProjectName { get; set; }

        /// <summary>Task # on the form (3.5, 141).</summary>
        [JsonProperty("TaskNumber")] public string TaskNumber { get; set; }
        [JsonProperty("Subtask")] public string Subtask { get; set; }

        /// <summary>The schedule project this day was linked to, or null when entered by hand.</summary>
        [JsonProperty("ScheduleProjectID")] public string ScheduleProjectId { get; set; }

        /// <summary>The schedule entries this report covers (empty when not from the schedule).</summary>
        [JsonProperty("ScheduleAssignmentIDs")] public List<string> ScheduleAssignmentIds { get; set; } = new List<string>();

        /// <summary>The crew, the person filling it in first ("Crew Name" on the form).</summary>
        [JsonProperty("Crew")] public List<ReportCrewMember> Crew { get; set; } = new List<ReportCrewMember>();

        [JsonProperty("WorkType")] public string WorkType { get; set; }
        [JsonProperty("Hours")] public decimal? Hours { get; set; }

        /// <summary>A vehicle id (AUT 103), the personal auto label, or null for none.</summary>
        [JsonProperty("Vehicle")] public string Vehicle { get; set; }
        [JsonProperty("PersonalAutoOwner", NullValueHandling = NullValueHandling.Ignore)] public string PersonalAutoOwner { get; set; }

        [JsonProperty("Equipment")] public List<string> Equipment { get; set; } = new List<string>();
        [JsonProperty("Mileage")] public ReportMileage Mileage { get; set; } = new ReportMileage();
        [JsonProperty("WorkOrder")] public string WorkOrder { get; set; }
        [JsonProperty("Weather")] public string Weather { get; set; }

        /// <summary>The form's Comments.</summary>
        [JsonProperty("Notes")] public string Notes { get; set; }

        /// <summary>The download folder name ("Data File Name" on the form).</summary>
        [JsonProperty("DataFileName")] public string DataFileName { get; set; }
        [JsonProperty("ControlFile")] public string ControlFile { get; set; }

        [JsonProperty("SafetyObservations")] public List<string> SafetyObservations { get; set; } = new List<string>();
        [JsonProperty("SafetyPrecautions")] public List<string> SafetyPrecautions { get; set; } = new List<string>();
        [JsonProperty("Extras")] public string Extras { get; set; }

        /// <summary>Where the report's PDF was filed: the project copy, or the admin copy when the project's could not be written.</summary>
        [JsonProperty("PDFPath")] public string PdfPath { get; set; }
        [JsonProperty("AdminPDFPath")] public string AdminPdfPath { get; set; }

        [JsonProperty("SubmittedBy")] public string SubmittedBy { get; set; }
        [JsonProperty("SubmittedTime")] public DateTimeOffset? SubmittedTime { get; set; }

        /// <summary>How the report started: "schedule" (prefilled) or "manual".</summary>
        [JsonProperty("Source")] public string Source { get; set; } = "manual";

        [JsonIgnore] public ReportCrewMember Lead => Crew.FirstOrDefault();
        [JsonIgnore] public string CrewInitials => string.Join("/", Crew.Select(c => c.Initials).Where(i => !string.IsNullOrEmpty(i)));

        /// <summary>A new report id: DR-20260507-JBB-3f9a1c2e. Unique without asking anyone.</summary>
        public static string NewId(DateTime date, string initials) =>
            "DR-" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" + (string.IsNullOrWhiteSpace(initials) ? "X" : initials.Trim().ToUpperInvariant())
            + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        /// <summary>What must be fixed before the report can be submitted; empty when it is ready.</summary>
        public List<string> Problems()
        {
            var p = new List<string>();
            if (Date == default(DateTime)) p.Add("Enter the date.");
            else if (Date.Date > DateTime.Today.AddDays(1)) p.Add("The date is in the future.");
            if (string.IsNullOrWhiteSpace(ProjectNumber)) p.Add("Enter the project number.");
            if (Crew.Count == 0 || string.IsNullOrWhiteSpace(Crew[0].Initials)) p.Add("Enter the crew.");
            if (!Hours.HasValue) p.Add("Enter the hours.");
            else if (Hours.Value <= 0 || Hours.Value > 24) p.Add("Hours must be more than 0 and no more than 24.");
            if (Mileage.Start.HasValue != Mileage.Finish.HasValue) p.Add("Enter both the start and finish mileage, or neither.");
            else if (Mileage.Total < 0) p.Add("The finish mileage is less than the start.");
            if (Equipment.Count == 0) p.Add("Check the equipment used (or No Equipment).");
            return p;
        }
    }
}
