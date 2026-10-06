using System;
using System.IO;
using System.Reflection;

namespace CrewUpload.Integration
{
    /// <summary>
    /// Finds the schedule integration, if there is one. Crew Upload has no compile-time link to it:
    /// the integration assembly named in job-folders.json is loaded from beside the app only when
    /// features.scheduleIntegration is on. A missing, broken or switched-off integration gives null,
    /// and every caller treats null as "no schedule" -- the standalone Crew Upload.
    /// </summary>
    public static class ScheduleConnector
    {
        public static IScheduleSource Connect(JobFolderConfig config, out string message)
        {
            message = null;
            var f = config.Features ?? new FeatureSwitches();
            var s = config.Schedule ?? new ScheduleSettings();
            if (!f.ScheduleIntegration || string.IsNullOrWhiteSpace(s.IntegrationAssembly) || string.IsNullOrWhiteSpace(s.Folder)) return null;
            try
            {
                var path = Path.IsPathRooted(s.IntegrationAssembly)
                    ? s.IntegrationAssembly
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, s.IntegrationAssembly);
                if (!File.Exists(path)) return null; // integration removed: standalone, nothing to say
                var type = Assembly.LoadFrom(path).GetType(s.IntegrationType, false);
                if (type == null || !typeof(IScheduleSource).IsAssignableFrom(type)) return null;
                return (IScheduleSource)Activator.CreateInstance(type, s.Folder);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                message = "Schedule unavailable. Report can still be entered manually.";
                return null;
            }
        }
    }
}
