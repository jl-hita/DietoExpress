using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Anguloso.Server.Logica;

/// <summary>
/// Convierte las plantillas markdown de paciente en PDF. Resuelve las variables {{...}} con los datos
/// reales y elimina las notas internas de trabajo para que el paciente solo vea el texto del documento.
/// </summary>
public static class PatientDocumentRenderer
{
    public const string BlankLine = "______________________";

    private static readonly Regex Placeholder = new(@"\{\{([a-zA-Z0-9_.-]+)\}\}", RegexOptions.Compiled);

    /// <summary>
    /// Sustituye variables y elimina notas internas. Las variables sin valor se sustituyen por una línea
    /// en blanco rellenable, nunca se deja la sintaxis {{...}} visible.
    /// </summary>
    public static string Render(string markdown, IReadOnlyDictionary<string, string> values)
    {
        var kept = new List<string>();
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (IsInternalNote(raw.Trim())) continue;
            kept.Add(raw);
        }

        var text = string.Join("\n", kept);
        return Placeholder.Replace(text, m =>
            values.TryGetValue(m.Groups[1].Value, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : BlankLine);
    }

    private static bool IsInternalNote(string line) =>
        line.StartsWith("> ", StringComparison.Ordinal)
        || line.StartsWith("**Nota interna", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("**Evidencia técnica", StringComparison.OrdinalIgnoreCase);

    public static void GeneratePdf(string markdown, string destination, string footerText)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(42);
                page.DefaultTextStyle(x => x.FontFamily("Lato").FontSize(10));

                page.Header().AlignRight().Text("DietoExpress · Documento de paciente")
                    .FontSize(8).FontColor("#777777");

                page.Content().Column(column =>
                {
                    column.Spacing(7);
                    foreach (var rawLine in lines)
                    {
                        var line = rawLine.Trim();
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            column.Item().Height(4);
                            continue;
                        }

                        if (line.StartsWith("# "))
                            column.Item().Text(line[2..]).Bold().FontSize(18);
                        else if (line.StartsWith("## "))
                            column.Item().PaddingTop(6).Text(line[3..]).Bold().FontSize(13);
                        else if (line.StartsWith("### "))
                            column.Item().PaddingTop(4).Text(line[4..]).Bold().FontSize(11);
                        else if (line.StartsWith("- "))
                            column.Item().PaddingLeft(12).Text("• " + line[2..]);
                        else if (line.StartsWith("> "))
                            column.Item().PaddingLeft(10).Text(line[2..]).Italic().FontColor("#555555");
                        else if (line.StartsWith("|"))
                            column.Item().Text(line.Replace("|", " ").Trim()).FontSize(8);
                        else
                            column.Item().Text(line.Replace("**", string.Empty));
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span(footerText);
                    text.CurrentPageNumber();
                });
            });
        });

        document.GeneratePdf(destination);
    }
}
