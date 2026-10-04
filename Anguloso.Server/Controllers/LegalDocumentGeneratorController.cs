using System.Text.RegularExpressions;
using Anguloso.Server.Logica.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/legal-documents/generator")]
[Authorize(Policy = "Professional")]
public sealed class LegalDocumentGeneratorController : ControllerBase
{
    private static readonly Dictionary<string, string> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["01-aviso-legal"] = "01-aviso-legal.md",
        ["02-privacidad-dietoexpress"] = "02-privacidad-dietoexpress.md",
        ["03-terminos-saas"] = "03-terminos-saas.md",
        ["04-politica-cookies"] = "04-politica-cookies.md",
        ["05-dpa-encargo-tratamiento"] = "05-dpa-encargo-tratamiento.md",
        ["06-informacion-privacidad-paciente"] = "06-informacion-privacidad-paciente.md",
        ["07-consentimiento-informado-paciente"] = "07-consentimiento-informado-paciente.md",
        ["08-condiciones-economicas"] = "08-condiciones-economicas.md",
        ["09-rat-minimo"] = "09-rat-minimo.md",
        ["10-matriz-conservacion"] = "10-matriz-conservacion.md",
        ["11-analisis-riesgos-eipd"] = "11-analisis-riesgos-eipd.md"
    };

    private readonly IConfiguration _configuration;

    public LegalDocumentGeneratorController(IConfiguration configuration) => _configuration = configuration;

    [HttpGet("templates")]
    public IActionResult ListTemplates() =>
        Ok(Templates.Keys.Select(key => new { key, file = Templates[key] }));

    [HttpPost("{templateKey}")]
    public async Task<IActionResult> Generate(string templateKey, CancellationToken cancellationToken)
    {
        if (!Templates.TryGetValue(templateKey, out var fileName))
            return NotFound("Plantilla legal no encontrada.");

        var scope = GetScope();
        if (scope == null) return Unauthorized();

        var templatePath = Path.Combine(AppContext.BaseDirectory, "LegalTemplates", fileName);
        if (!System.IO.File.Exists(templatePath))
            return Problem("La plantilla legal no está disponible en el despliegue.");

        var template = await System.IO.File.ReadAllTextAsync(templatePath, cancellationToken);
        var values = await ReadConfiguration(scope.Value.type, scope.Value.id, cancellationToken);

        // Compatibilidad con las plantillas existentes: display_name se deriva
        // de la identidad legal cuando no existe un campo específico.
        if (!values.ContainsKey("display_name"))
            values["display_name"] = values.GetValueOrDefault("legal_name", string.Empty);

        var rendered = Regex.Replace(template, @"{{([a-zA-Z0-9_.-]+)}}", match =>
        {
            var key = match.Groups[1].Value;
            return values.TryGetValue(key, out var value) ? value : match.Value;
        });

        var unresolved = Regex.Matches(rendered, @"{{([a-zA-Z0-9_.-]+)}}")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var title = GetTitle(templateKey);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(rendered))).ToLowerInvariant();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var versionCommand = new NpgsqlCommand("""
            SELECT COALESCE(MAX(version),0)+1
            FROM legal_generated_documents
            WHERE scope_type=@scope AND scope_id=@scopeId AND template_key=@key;
            """, connection);
        versionCommand.Parameters.AddWithValue("scope", scope.Value.type);
        versionCommand.Parameters.AddWithValue("scopeId", scope.Value.id);
        versionCommand.Parameters.AddWithValue("key", templateKey);
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken));

        await using var insert = new NpgsqlCommand("""
            INSERT INTO legal_generated_documents
                (scope_type, scope_id, template_key, version, title, content, status, sha256)
            VALUES
                (@scope,@scopeId,@key,@version,@title,@content,'draft',@sha)
            RETURNING id;
            """, connection);
        insert.Parameters.AddWithValue("scope", scope.Value.type);
        insert.Parameters.AddWithValue("scopeId", scope.Value.id);
        insert.Parameters.AddWithValue("key", templateKey);
        insert.Parameters.AddWithValue("version", version);
        insert.Parameters.AddWithValue("title", title);
        insert.Parameters.AddWithValue("content", rendered);
        insert.Parameters.AddWithValue("sha", hash);
        var id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));

        return Ok(new { id, templateKey, version, title, content = rendered, sha256 = hash, unresolved });
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var scope = GetScope();
        if (scope == null) return Unauthorized();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT id, template_key, version, title, status, sha256, generated_at
            FROM legal_generated_documents
            WHERE scope_type=@scope AND scope_id=@scopeId
            ORDER BY generated_at DESC;
            """, connection);
        command.Parameters.AddWithValue("scope", scope.Value.type);
        command.Parameters.AddWithValue("scopeId", scope.Value.id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<object>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new
            {
                id = reader.GetInt64(0),
                templateKey = reader.GetString(1),
                version = reader.GetInt32(2),
                title = reader.GetString(3),
                status = reader.GetString(4),
                sha256 = reader.GetString(5),
                generatedAt = reader.GetDateTime(6)
            });

        return Ok(result);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        var scope = GetScope();
        if (scope == null) return Unauthorized();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT id, template_key, version, title, content, status, sha256, generated_at
            FROM legal_generated_documents
            WHERE id=@id AND scope_type=@scope AND scope_id=@scopeId;
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("scope", scope.Value.type);
        command.Parameters.AddWithValue("scopeId", scope.Value.id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound();

        return Ok(new
        {
            id = reader.GetInt64(0),
            templateKey = reader.GetString(1),
            version = reader.GetInt32(2),
            title = reader.GetString(3),
            content = reader.GetString(4),
            status = reader.GetString(5),
            sha256 = reader.GetString(6),
            generatedAt = reader.GetDateTime(7)
        });
    }

    private (string type, int id)? GetScope()
    {
        var userId = AuthHelpers.GetUserId(User);
        if (!userId.HasValue) return null;

        var tenantId = AuthHelpers.GetTenantId(User);
        if (User.IsInRole("clinic_admin") && tenantId.HasValue)
            return ("tenant", tenantId.Value);

        return ("user", userId.Value);
    }

    private async Task<Dictionary<string, string>> ReadConfiguration(
        string scopeType, int scopeId, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT scope_type, setting_key, setting_value
            FROM legal_configuration
            WHERE (scope_type=@scope AND scope_id=@scopeId)
               OR (scope_type='platform' AND scope_id=1);
            """, connection);
        command.Parameters.AddWithValue("scope", scopeType);
        command.Parameters.AddWithValue("scopeId", scopeId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var type = reader.GetString(0);
            var key = reader.GetString(1);
            var prefix = type.Equals("platform", StringComparison.OrdinalIgnoreCase)
                ? "platform."
                : "professional.";
            result[prefix + key] = reader.GetString(2);
        }

        return result;
    }

    private static string GetTitle(string key) => key switch
    {
        "01-aviso-legal" => "Aviso legal",
        "02-privacidad-dietoexpress" => "Política de privacidad de DietoExpress",
        "03-terminos-saas" => "Términos y condiciones de DietoExpress",
        "04-politica-cookies" => "Política de cookies",
        "05-dpa-encargo-tratamiento" => "Acuerdo de encargo del tratamiento",
        "06-informacion-privacidad-paciente" => "Información de privacidad para pacientes",
        "07-consentimiento-informado-paciente" => "Consentimiento informado del paciente",
        "08-condiciones-economicas" => "Condiciones económicas",
        "09-rat-minimo" => "Registro de Actividades de Tratamiento",
        "10-matriz-conservacion" => "Matriz de conservación y supresión",
        "11-analisis-riesgos-eipd" => "Análisis de riesgos y decisión EIPD",
        _ => key
    };

    private string ConnectionString =>
        _configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection no está configurada.");
}
