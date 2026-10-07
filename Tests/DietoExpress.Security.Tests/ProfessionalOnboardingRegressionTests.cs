using Xunit;

namespace DietoExpress.Security.Tests;

public class ProfessionalOnboardingRegressionTests
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

    private static string Read(string path) => File.ReadAllText(Path.Combine(RepoRoot, path));

    [Fact]
    public void ProfessionalOnboarding_UsesPersistedAvailabilityAndRequiredProgress()
    {
        var component = Read("anguloso.client/src/app/componentes/onboarding/onboarding.component.ts");
        var template = Read("anguloso.client/src/app/componentes/onboarding/onboarding.component.html");

        Assert.Contains("PatientPortalService", component);
        Assert.Contains("getAvailability()", component);
        Assert.Contains("key: 'schedule'", component);
        Assert.Contains("route: '/appointments'", component);
        Assert.Contains("const hasAvailability = availability.some(rule => rule.isActive", component);
        Assert.Contains("completedCount }} de {{ requiredSteps.length }}", template);
    }
}
