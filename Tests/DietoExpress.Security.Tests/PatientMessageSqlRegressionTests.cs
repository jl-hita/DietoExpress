using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class PatientMessageSqlRegressionTests
{
    private static string Root
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "Anguloso.Server", "Program.cs"))) d = d.Parent;
            return d?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    [Fact]
    public void PatientUnreadCountSqlMustBeComposableByEntityFramework()
    {
        var source = File.ReadAllText(Path.Combine(Root, "Anguloso.Server/Controllers/PatientMessagesController.cs"));
        var start = source.IndexOf("public async Task<ActionResult<int>> GetPatientUnreadCount()", StringComparison.Ordinal);
        var end = source.IndexOf("[HttpGet(\"patient\")]", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var method = source[start..end];
        Assert.Contains("AND m.read_at IS NULL", method);
        Assert.DoesNotContain("AND m.read_at IS NULL;", method);
    }

    [Fact]
    public void VideoViewChildrenMustSupportConditionallyRenderedElements()
    {
        var source = File.ReadAllText(Path.Combine(Root, "anguloso.client/src/app/componentes/video-consultation/video-consultation.component.ts"));
        Assert.Contains("@ViewChild('localVideo')", source);
        Assert.Contains("@ViewChild('remoteContainer')", source);
        Assert.DoesNotContain("@ViewChild('localVideo', { static: true })", source);
        Assert.DoesNotContain("@ViewChild('remoteContainer', { static: true })", source);
        Assert.Contains("this.remoteContainer?.nativeElement", source);
    }
}
