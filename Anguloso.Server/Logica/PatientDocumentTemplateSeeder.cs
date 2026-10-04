using System.Security.Cryptography;
using System.Text;
using Npgsql;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Anguloso.Server.Logica;

/// <summary>
/// Instala de forma idempotente el catálogo documental base de DietoExpress en cada tenant.
/// Los documentos fuente viven en docs/patient-documents y se convierten a PDF privado para
/// reutilizar la infraestructura existente de plantillas y documentos del paciente.
/// </summary>
public sealed class PatientDocumentTemplateSeeder
{
    private readonly string _connectionString;
    private readonly IWebHostEnvironment _environment;

    private sealed record Definition(
        string Code,
        string Name,
        string FileName,
        string DocumentType,
        bool RequiredOnCreation,
        bool RequiredBeforeConsultation,
        bool RequiresSignature);

    private static readonly Definition[] Definitions =
    [
        new("PAT-001", "Información y consentimiento informado del servicio de nutrición", "PAT-001-consentimiento-servicio-nutricion.md", "consent", true, false, true),
        new("PAT-002", "Información sobre tratamiento de datos personales y datos de salud", "PAT-002-datos-personales-y-salud.md", "privacy", true, false, true),
        new("PAT-003", "Autorización y preferencias de comunicaciones", "PAT-003-comunicaciones.md", "communications", true, false, true),
        new("PAT-004", "Consentimiento para teleconsulta", "PAT-004-teleconsulta.md", "teleconsultation", false, true, true),
        new("PAT-005", "Consentimiento para fotografías de seguimiento", "PAT-005-fotografias-seguimiento.md", "photography", false, false, true),
        new("PAT-006", "Declaración de información sanitaria y responsabilidad del paciente", "PAT-006-declaracion-informacion-sanitaria.md", "health_information", true, false, true),
        new("PAT-007", "Representación de menores o personas representadas", "PAT-007-menores-representacion.md", "representation", false, false, true),
        new("PAT-008", "Consentimiento para comunicaciones comerciales", "PAT-008-comunicaciones-comerciales.md", "commercial_communications", false, false, true),
        new("PAT-009", "Anamnesis y antecedentes nutricionales", "PAT-009-anamnesis-y-antecedentes-nutricionales.md", "anamnesis", false, false, false),
        new("PAT-010", "Registro de seguimiento y evolución", "PAT-010-seguimiento-y-evolucion.md", "follow_up", false, false, false),
        new("PAT-011", "Autorización para intercambio de información asistencial", "PAT-011-autorizacion-intercambio-informacion.md", "information_sharing", false, false, true),
        new("PAT-012", "Acceso al portal y comunicaciones digitales", "PAT-012-acceso-portal-y-comunicaciones-digitales.md", "portal", false, false, true),
        new("PAT-013", "Persona de contacto autorizada", "PAT-013-persona-de-contacto.md", "authorized_contact", false, false, true),
        new("PAT-014", "Constancia de entrega y recepción de documentación", "PAT-014-entrega-y-recepcion-documentacion.md", "delivery", false, false, false)
    ];

    public PatientDocumentTemplateSeeder(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection no está configurada.");
        _environment = environment;
    }

    public async Task SeedTenantAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        var root = GetStorageRoot();
        var sourceRoot = Path.Combine(_environment.ContentRootPath, "PatientDocumentTemplates");
        if (!Directory.Exists(sourceRoot))
            return;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lockKey, 0));", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("lockKey", $"patient-template-seed:{tenantId}");
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var createdFiles = new List<string>();
        try
        {
            foreach (var definition in Definitions)
            {
                var source = Path.Combine(sourceRoot, definition.FileName);
                if (!File.Exists(source))
                    continue;

                await using var existsCommand = new NpgsqlCommand("""
                    SELECT 1
                    FROM document_templates
                    WHERE tenant_id=@tenant
                      AND LOWER(name)=LOWER(@name)
                      AND version=1
                    LIMIT 1;
                    """, connection, transaction);
                existsCommand.Parameters.AddWithValue("tenant", tenantId);
                existsCommand.Parameters.AddWithValue("name", definition.Name);
                if (await existsCommand.ExecuteScalarAsync(cancellationToken) != null)
                    continue;

                var storageKey = $"{tenantId}/templates/{Guid.NewGuid():N}.pdf";
                var destination = Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                var markdown = await File.ReadAllTextAsync(source, Encoding.UTF8, cancellationToken);
                GeneratePdf(markdown, destination);
                createdFiles.Add(destination);

                var fileInfo = new FileInfo(destination);
                var sha256 = await ComputeSha256Async(destination, cancellationToken);

                await using var insert = new NpgsqlCommand("""
                    INSERT INTO document_templates
                        (tenant_id, name, description, document_type, content_html,
                         file_name, storage_key, mime_type, file_size, sha256, version,
                         is_active, is_required_on_client_creation, requires_signature,
                         is_required_before_consultation, created_at, updated_at)
                    VALUES
                        (@tenant, @name, @description, @type, @content,
                         @filename, @storage, 'application/pdf', @size, @sha, 1,
                         true, @requiredCreation, @requires, @requiredConsultation,
                         NOW(), NOW())
                    ON CONFLICT (tenant_id, name, version) DO NOTHING;
                    """, connection, transaction);

                insert.Parameters.AddWithValue("tenant", tenantId);
                insert.Parameters.AddWithValue("name", definition.Name);
                insert.Parameters.AddWithValue("description", $"Plantilla base {definition.Code}. Requiere adaptación y validación jurídica antes de uso real.");
                insert.Parameters.AddWithValue("type", definition.DocumentType);
                insert.Parameters.AddWithValue("content", markdown);
                insert.Parameters.AddWithValue("filename", $"{definition.Code}.pdf");
                insert.Parameters.AddWithValue("storage", storageKey);
                insert.Parameters.AddWithValue("size", fileInfo.Length);
                insert.Parameters.AddWithValue("sha", sha256);
                insert.Parameters.AddWithValue("requiredCreation", definition.RequiredOnCreation);
                insert.Parameters.AddWithValue("requires", definition.RequiresSignature);
                insert.Parameters.AddWithValue("requiredConsultation", definition.RequiredBeforeConsultation);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            foreach (var file in createdFiles)
            {
                try { File.Delete(file); } catch { }
            }
            throw;
        }
    }

    public async Task SeedAllTenantsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT id FROM tenants WHERE status='active' ORDER BY id;", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tenantIds = new List<int>();
        while (await reader.ReadAsync(cancellationToken))
            tenantIds.Add(reader.GetInt32(0));
        await reader.DisposeAsync();

        foreach (var tenantId in tenantIds)
            await SeedTenantAsync(tenantId, cancellationToken);
    }

    private string GetStorageRoot()
    {
        var configured = _environment.IsDevelopment()
            ? null
            : Environment.GetEnvironmentVariable("DIETOEXPRESS_DOCUMENTS_PATH");
        configured ??= "/var/lib/dietoexpress/documents";
        Directory.CreateDirectory(configured);
        return Path.GetFullPath(configured);
    }

    private static void GeneratePdf(string markdown, string destination)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(42);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10));

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
                            column.Item().Text(line);
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Plantilla base · revisar antes de uso real · página ");
                    text.CurrentPageNumber();
                });
            });
        });

        document.GeneratePdf(destination);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
