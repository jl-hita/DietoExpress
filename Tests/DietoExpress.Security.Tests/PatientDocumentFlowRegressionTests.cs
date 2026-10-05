using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class PatientDocumentFlowRegressionTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    private static string ReadServerSource(string relativePath)
        => File.ReadAllText(Path.Combine(RepoRoot, relativePath));

    [Fact]
    public void AutomationWorkerMustClaimJobsOneAtATimeBeforeExecutingThem()
    {
        var source = ReadServerSource("Anguloso.Server/Logica/AutomationWorker.cs");

        Assert.Contains("ClaimNextJobAsync(cancellationToken)", source);
        Assert.Contains("LIMIT 1", source);
        Assert.Contains("await ExecuteJobAsync(job, cancellationToken)", source);
        Assert.Contains("for (var i = 0; i < 20; i++)", source);
        Assert.DoesNotContain("LIMIT 20", source);
    }

    [Fact]
    public void AutomationWorkerMustRecoverStaleProcessingJobs()
    {
        var source = ReadServerSource("Anguloso.Server/Logica/AutomationWorker.cs");

        Assert.Contains("status='processing'", source);
        Assert.Contains("locked_at < NOW() - INTERVAL '10 minutes'", source);
        Assert.Contains("SET status = CASE WHEN attempts >= max_attempts THEN 'failed' ELSE 'pending' END", source);
        Assert.Contains("locked_at = NULL", source);
    }
}
