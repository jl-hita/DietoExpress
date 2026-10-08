using System.Text.RegularExpressions;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class DatabaseBootstrapRegressionTests
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
    public void Bootstrap_DollarQuotedPostgresBlocksHaveMatchingDelimiters()
    {
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));

        Assert.False(Regex.IsMatch(bootstrap, @"(?m)^\s*DO \$(?!\$)\s*$"),
            "Se encontró un bloque DO con delimitador de apertura incompleto.");
        Assert.False(Regex.IsMatch(bootstrap, @"(?m)^\s*END \$(?!\$);?\s*$"),
            "Se encontró un bloque DO con delimitador de cierre incompleto.");
    }

    [Fact]
    public void Bootstrap_VerifiesEveryTableCreatedBySchemaBootstrappers()
    {
        var schemaFiles = new[]
        {
            "Anguloso.Server/Logica/DatabaseBootstrap.cs",
            "Anguloso.Server/Logica/BillingSchemaBootstrap.cs",
            "Anguloso.Server/Logica/LegalCommunicationsSchema.cs",
            "Anguloso.Server/Logica/SupportSchemaBootstrap.cs",
            "Anguloso.Server/Logica/PublicDirectoryVerificationSchema.cs"
        };

        var sources = schemaFiles
            .Select(path => File.ReadAllText(Path.Combine(RepoRoot, path.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();
        var createdTables = sources
            .SelectMany(source => Regex.Matches(source, @"CREATE TABLE IF NOT EXISTS\s+([a-zA-Z_][a-zA-Z0-9_]*)",
                RegexOptions.IgnoreCase).Cast<Match>())
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var bootstrap = sources[0];
        var verifyMatch = Regex.Match(bootstrap,
            @"SELECT unnest\(ARRAY\[([\s\S]*?)\]\) AS required_table",
            RegexOptions.IgnoreCase);
        Assert.True(verifyMatch.Success, "No se encontró la lista de tablas verificadas por el bootstrap.");

        var verifiedTables = Regex.Matches(verifyMatch.Groups[1].Value, @"'([^']+)'")
            .Cast<Match>()
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = createdTables.Except(verifiedTables).OrderBy(table => table).ToArray();
        Assert.True(missing.Length == 0,
            $"El contrato de verificación no incluye estas tablas: {string.Join(", ", missing)}");
    }
}
