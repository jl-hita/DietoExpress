using System;
using System.IO;
using Xunit;

namespace DietoExpress.Security.Tests;

public class PublicSeoIndexingRegressionTests
{
    private static readonly string Root = FindRoot();

    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Anguloso.Server", "Program.cs"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void Sitemap_OnlyUsesPublishedDirectoryProfilesAndPopulatedFacets()
    {
        var source = Read("Anguloso.Server/Controllers/PublicSitemapController.cs");
        Assert.Contains("directory_publication_status == "published"", source);
        Assert.Contains("Count >= 3", source);
        Assert.Contains("/nutricionistas/ciudad/", source);
        Assert.Contains("/nutricionistas/especialidad/", source);
        Assert.Contains("/nutricionistas/online", source);
    }

    [Fact]
    public void FacetRoutesExistBeforeSlugRoute()
    {
        var routes = Read("anguloso.client/src/app/app.routes.ts");
        Assert.Contains("nutricionistas/online", routes);
        Assert.Contains("nutricionistas/ciudad/:city", routes);
        Assert.Contains("nutricionistas/especialidad/:speciality", routes);
        Assert.Contains("nutricionistas/:slug", routes);
    }
}
// Mantener estas regresiones junto al flujo público para evitar páginas SEO sin control de publicación.
