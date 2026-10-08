using System;
using System.IO;
using Xunit;

namespace DietoExpress.Security.Tests;

public class CookieTechnologyInventoryRegressionTests
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
    public void SessionCookies_AreHttpOnlySecureAndStrict()
    {
        var auth = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "AuthController.cs"));
        var patient = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "PatientPortalController.cs"));

        Assert.Contains("dietoexpress_professional_session", auth);
        Assert.Contains("HttpOnly = true", auth);
        Assert.Contains("Secure = true", auth);
        Assert.Contains("SameSite = SameSiteMode.Strict", auth);
        Assert.Contains("Expires = DateTimeOffset.UtcNow.AddHours(3)", auth);

        Assert.Contains("dietoexpress_patient_session", patient);
        Assert.Contains("HttpOnly = true", patient);
        Assert.Contains("Secure = true", patient);
        Assert.Contains("SameSite = SameSiteMode.Strict", patient);
        Assert.Contains("Expires = DateTimeOffset.UtcNow.AddHours(8)", patient);
    }

    [Fact]
    public void PublicFunnelAnalytics_DoesNotUseBrowserStorage()
    {
        var service = File.ReadAllText(Path.Combine(
            RepoRoot,
            "anguloso.client",
            "src",
            "app",
            "servicios",
            "public-funnel-analytics.service.ts"));

        Assert.DoesNotContain("localStorage", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("document.cookie", service, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClientInventory_DocumentsOnlyKnownBrowserStorage()
    {
        var inventory = File.ReadAllText(Path.Combine(
            RepoRoot,
            "docs",
            "legal",
            "12-inventario-tecnologias-cookies.md"));

        Assert.Contains("dietoexpress_professional_session", inventory);
        Assert.Contains("dietoexpress_patient_session", inventory);
        Assert.Contains("completed_meals_<clientId>_<date>", inventory);
        Assert.Contains("Google Identity Services", inventory);
        Assert.DoesNotContain("[IDENTIFICAR]", inventory, StringComparison.OrdinalIgnoreCase);
    }
}
