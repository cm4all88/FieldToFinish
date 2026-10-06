using CrewUpload;

namespace CrewUpload.Tests;

/// <summary>A throwaway "share" for the schedule-integration tests: Clients, Config and a config.</summary>
public abstract class TestShare : IDisposable
{
    protected readonly string Root;
    protected readonly string Clients;
    protected readonly JobFolderConfig Config;

    protected TestShare()
    {
        Root = Path.Combine(Path.GetTempPath(), "crewi-" + Path.GetRandomFileName());
        Clients = Path.Combine(Root, "Clients");
        Directory.CreateDirectory(Clients);
        Config = JobFolderConfig.CreateDefault();
        Config.JobsRoot = Clients;
        Config.BaseDirectory = Root;
        Config.RequireUncPaths = false;
        Config.RegistryFile = Path.Combine(Root, "Config", "project-registry.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch (IOException) { }
    }

    protected string SurveyDir(string client = "1800-HDR", string project = "554-1800-119 TDLE Phase 3")
    {
        var path = Path.Combine(Clients, client, project, "99Svcs", "Survey");
        Directory.CreateDirectory(path);
        return path;
    }

    protected ProjectRegistry Registry() => new(Config.RegistryFile, requireUnc: false, backups: 5, retries: 5, retryDelayMs: 50)
    {
        Log = new RegistryLog(Path.Combine(Root, "logs", "registry-errors.log")),
        RetryDelaysMs = new[] { 1, 1, 1, 1 },
        LockWait = TimeSpan.FromMilliseconds(300),
    };
}
