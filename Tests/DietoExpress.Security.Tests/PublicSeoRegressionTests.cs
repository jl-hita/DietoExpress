using Xunit;

namespace DietoExpress.Security.Tests;

public class PublicSeoRegressionTests
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

    [Fact]
    public void PublicDirectory_UsesDynamicSeoMetadataAndCanonicalProfileUrls()
    {
        var component = File.ReadAllText(Path.Combine(
            RepoRoot, "anguloso.client", "src", "app", "componentes", "directory", "directory.component.ts"));
        var service = File.ReadAllText(Path.Combine(
            RepoRoot, "anguloso.client", "src", "app", "servicios", "public-seo.service.ts"));

        Assert.Contains("PublicSeoService", component);
        Assert.Contains("setDirectorySeo", component);
        Assert.Contains("setProfileSeo", component);
        Assert.Contains("link.rel = 'canonical'", service);
        Assert.Contains("application/ld+json", service);
        Assert.Contains("Person", service);
        Assert.Contains("og:title", service);
        Assert.Contains("twitter:card", service);
    }

    [Fact]
    public void PublicDirectory_RoutesRemainPublicAndProfileUsesSlug()
    {
        var routes = File.ReadAllText(Path.Combine(
            RepoRoot, "anguloso.client", "src", "app", "app.routes.ts"));

        Assert.Contains("path: 'nutricionistas'", routes);
        Assert.Contains("path: 'nutricionistas/:slug'", routes);
        Assert.Contains("DirectoryComponent", routes);
    }
}
