namespace DietoExpress.Security.Tests;

/// <summary>
/// La plantilla de <c>training-periodization.component</c> vive en su propio .html. Estos tests inspeccionan el código fuente
/// buscando textos de la interfaz, así que reconstruyen el componente con la plantilla en su sitio original (con la misma
/// sangría), de modo que las comprobaciones que recortan entre marcadores siguen funcionando igual.
/// </summary>
internal static class TrainingComponentSource
{
    public static string ReadAll()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
            directory = directory.Parent;
        var folder = Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio."),
            "anguloso.client", "src", "app", "componentes", "training-periodization");

        var ts = File.ReadAllText(Path.Combine(folder, "training-periodization.component.ts"));
        var html = File.ReadAllText(Path.Combine(folder, "training-periodization.component.html"));

        const string reference = "  templateUrl: './training-periodization.component.html',";
        if (!ts.Contains(reference, StringComparison.Ordinal)) return ts + "\n" + html;

        var newline = ts.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var indented = string.Join(newline, html.TrimEnd('\r', '\n').Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(line => line.Length == 0 ? line : "    " + line));
        return ts.Replace(reference, "  template: `" + newline + indented + newline + "  `,", StringComparison.Ordinal);
    }
}
