using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class ForwardedHeadersRegressionTests
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
    public void ForwardedHeaders_AreEnabledForClientIpAndProto()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Program.cs"));

        Assert.Contains("ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto", source);
        Assert.Contains("builder.Services.Configure<ForwardedHeadersOptions>", source);
        // No se debe borrar la lista de proxies/redes de confianza: solo deben aceptarse los proxies conocidos.
        Assert.DoesNotContain("KnownProxies.Clear()", source);
        Assert.DoesNotContain("KnownNetworks.Clear()", source);
    }

    [Fact]
    public void ForwardedHeaders_MiddlewareRunsBeforeRateLimiter()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Program.cs"));
        var forwarded = source.IndexOf("app.UseForwardedHeaders();", StringComparison.Ordinal);
        var limiter = source.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal);

        Assert.True(forwarded >= 0, "Debe procesarse la IP reenviada por el proxy.");
        Assert.True(limiter > forwarded, "El rate limiter debe ejecutarse después de Forwarded Headers.");
    }
}
