using System;
using System.IO;
using Xunit;

namespace DietoExpress.Security.Tests;

public class PublicFunnelAnalyticsRegressionTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;

            return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    [Fact]
    public void PublicFunnel_TracksOnlyNonSensitiveEvents()
    {
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "PublicFunnelAnalyticsController.cs"));
        var service = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "PublicFunnelAnalyticsService.cs"));

        Assert.Contains("directory_view", controller);
        Assert.Contains("profile_view", controller);
        Assert.Contains("booking_started", controller);
        Assert.DoesNotContain("email", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ip_address", service, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicBooking_RecordsTheRequestedFunnelEvent()
    {
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "DirectoryController.cs"));
        Assert.Contains("booking_requested", controller);
        Assert.Contains("TrackAsync", controller);
    }

    [Fact]
    public void AdminFunnel_IsRestrictedToSuperAdmin()
    {
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "PublicFunnelAnalyticsController.cs"));
        Assert.Contains("[Authorize(Roles = \"superadmin\")]", controller);
        Assert.Contains("CompletedConsultations", controller);
    }
}
