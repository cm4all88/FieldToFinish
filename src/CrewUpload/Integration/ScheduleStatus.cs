using System;
using System.Linq;
using CrewUpload.Reports;

namespace CrewUpload.Integration
{
    /// <summary>
    /// Optional: after a report is filed, tell the schedule its entries were reported. Only when
    /// features.scheduleReportStatus is on, only for reports started from the schedule, and only
    /// through the integration's own writer. A failure here never affects the report itself.
    /// </summary>
    public static class ScheduleStatus
    {
        public const string Kind = "schedule";

        /// <returns>What happened, for the submit summary; null when there was nothing to do.</returns>
        public static FiledCopy Report(JobFolderConfig config, IScheduleSource schedule, DailyReport report, CrewSettings crew)
        {
            if (!(config.Features?.ScheduleReportStatus ?? false) || !(config.Features?.ScheduleIntegration ?? false)) return null;
            if (report == null || report.ScheduleAssignmentIds.Count == 0) return null;
            var writer = schedule as IScheduleStatusWriter;
            if (writer == null) return null;
            var result = new FiledCopy { Kind = Kind };
            try
            {
                string message;
                if (writer.MarkReported(report.ScheduleAssignmentIds, report.ReportId, ActivityFor(report.WorkType, crew, schedule), out message))
                    result.Path = "marked as reported";
                else
                    result.Error = message ?? "not marked";
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                result.Error = e.Message;
            }
            return result;
        }

        /// <summary>The schedule's activity name for a work type, from the PM's mapping, when the schedule has that activity.</summary>
        internal static string ActivityFor(string workType, CrewSettings crew, IScheduleSource schedule)
        {
            if (string.IsNullOrEmpty(workType) || crew == null || schedule == null) return null;
            var known = schedule.Activities();
            return crew.ActivityMap.Where(m => string.Equals(m.WorkType, workType, StringComparison.OrdinalIgnoreCase))
                .SelectMany(m => m.Activities)
                .FirstOrDefault(a => known.Any(k => string.Equals(k, a, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
