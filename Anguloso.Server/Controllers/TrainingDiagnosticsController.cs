using System.Text.Json;
using System.Diagnostics;
using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Herramientas de inventario y diagnóstico de planes de entrenamiento, exclusivas del superadministrador.
/// </summary>
[ApiController]
[Route("api/training/diagnostics")]
[Authorize(Policy = "Professional")]
public sealed class TrainingDiagnosticsController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly ILogger<TrainingDiagnosticsController> _logger;

    public TrainingDiagnosticsController(
        angulosodbContext context,
        ILogger<TrainingDiagnosticsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet("plans")]
    public async Task<IActionResult> GetAllTrainingPlans(CancellationToken cancellationToken)
    {
        if (!User.IsInRole("superadmin")) return Forbid();

        const string sql = """
            SELECT p.id, p.name, p.periodization_model, p.start_date, p.end_date,
                   p.status, p.version, p.notes, f.client_id, c.full_name,
                   f.enabled AS profile_enabled, c.archived_at IS NOT NULL AS client_archived,
                   (SELECT COUNT(*) FROM training_mesocycles m WHERE m.plan_id = p.id) AS mesocycle_count,
                   (SELECT COUNT(*) FROM training_sessions s WHERE s.plan_id = p.id) AS session_count,
                   (SELECT COUNT(*) FROM training_session_records r
                    JOIN training_sessions s ON s.id = r.session_id WHERE s.plan_id = p.id) AS record_count
            FROM training_plans p
            JOIN training_profiles f ON f.id = p.profile_id
            JOIN clients c ON c.id = f.client_id
            ORDER BY p.start_date DESC, p.id DESC
            """;

        var items = new List<object>();
        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        try
        {
            if (shouldClose) await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new
                {
                    id = reader.GetInt32(0), name = reader.GetString(1), periodizationModel = reader.GetString(2),
                    startDate = reader.GetFieldValue<DateOnly>(3),
                    endDate = reader.IsDBNull(4) ? (DateOnly?)null : reader.GetFieldValue<DateOnly>(4),
                    status = reader.GetString(5), version = reader.GetInt32(6),
                    notes = reader.IsDBNull(7) ? null : reader.GetString(7),
                    clientId = reader.GetInt32(8),
                    clientName = reader.IsDBNull(9) ? "Cliente sin nombre" : reader.GetString(9),
                    profileEnabled = reader.GetBoolean(10), clientArchived = reader.GetBoolean(11),
                    mesocycleCount = reader.GetInt64(12), sessionCount = reader.GetInt64(13), recordCount = reader.GetInt64(14)
                });
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not load superadmin training-plan inventory. RequestId={RequestId}", HttpContext.TraceIdentifier);
            throw;
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
        return Ok(items);
    }

    [HttpGet("plans/{planId:int}/test-plan.pdf")]
    public async Task<IActionResult> ExportTestPlan(int planId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestId = HttpContext.TraceIdentifier;
        _logger.LogInformation(
            "Test-plan PDF export started. PlanId={PlanId}; RequestId={RequestId}; Authenticated={Authenticated}; Roles={Roles}",
            planId,
            requestId,
            User.Identity?.IsAuthenticated == true,
            string.Join(",", User.Claims.Where(c => c.Type == System.Security.Claims.ClaimTypes.Role || c.Type == "role").Select(c => c.Value).Distinct()));

        if (!User.IsInRole("superadmin"))
        {
            _logger.LogWarning("Test-plan PDF export forbidden: caller is not superadmin. RequestId={RequestId}", requestId);
            return Forbid();
        }

        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        try
        {
            _logger.LogInformation("Opening database connection for test-plan PDF export. RequestId={RequestId}; WasAlreadyOpen={WasAlreadyOpen}", requestId, !shouldClose);
            if (shouldClose) await connection.OpenAsync(cancellationToken);
            _logger.LogInformation("Database connection ready for test-plan PDF export. RequestId={RequestId}; ElapsedMs={ElapsedMs}", requestId, stopwatch.ElapsedMilliseconds);

            await using (var countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = "SELECT COUNT(*) FROM training_plans WHERE id = @planId";
                countCommand.Parameters.AddWithValue("planId", planId);
                var planExists = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken)) == 1;
                if (!planExists)
                {
                    _logger.LogWarning("Test-plan PDF export stopped: plan not found. PlanId={PlanId}; RequestId={RequestId}", planId, requestId);
                    return NotFound(new { message = "No existe la planificación solicitada." });
                }
            }

            _logger.LogInformation("Test-plan PDF export will include the selected plan. PlanId={PlanId}; RequestId={RequestId}; ElapsedMs={ElapsedMs}", planId, requestId, stopwatch.ElapsedMilliseconds);

            const string sql = """
                SELECT jsonb_pretty(jsonb_build_object(
                    'exportedAt', to_jsonb(clock_timestamp()),
                    'plan', (SELECT to_jsonb(p) FROM training_plans p WHERE p.id = @planId),
                    'profile', (
                        SELECT jsonb_build_object(
                            'id', f.id, 'clientId', f.client_id, 'enabled', f.enabled,
                            'primaryGoal', f.primary_goal, 'experienceLevel', f.experience_level,
                            'managementMode', f.management_mode
                        )
                        FROM training_plans p JOIN training_profiles f ON f.id = p.profile_id WHERE p.id = @planId
                    ),
                    'macrocycles', COALESCE((
                        SELECT jsonb_agg(to_jsonb(m) ORDER BY m.start_date, m.id)
                        FROM training_macrocycles m
                        WHERE m.plan_id = @planId
                    ), '[]'::jsonb),
                    'macrocyclePhases', COALESCE((
                        SELECT jsonb_agg(to_jsonb(ph) ORDER BY ph.start_date, ph.id)
                        FROM training_macrocycle_phases ph
                        JOIN training_macrocycles m ON m.id = ph.macrocycle_id
                        WHERE m.plan_id = @planId
                    ), '[]'::jsonb),
                    'mesocycles', COALESCE((
                        SELECT jsonb_agg(to_jsonb(m) ORDER BY m.sequence_number, m.id)
                        FROM training_mesocycles m
                        WHERE m.plan_id = @planId
                    ), '[]'::jsonb),
                    'microcycles', COALESCE((
                        SELECT jsonb_agg(to_jsonb(mc) ORDER BY mc.sequence_number, mc.id)
                        FROM training_microcycles mc
                        JOIN training_mesocycles m ON m.id = mc.mesocycle_id
                        WHERE m.plan_id = @planId
                    ), '[]'::jsonb),
                    'sessions', COALESCE((
                        SELECT jsonb_agg(to_jsonb(s) ORDER BY s.scheduled_date, s.id)
                        FROM training_sessions s
                        WHERE s.plan_id = @planId
                    ), '[]'::jsonb),
                    'sessionRecords', COALESCE((
                        SELECT jsonb_agg(to_jsonb(r) ORDER BY r.session_id, r.id)
                        FROM training_session_records r
                        JOIN training_sessions s ON s.id = r.session_id
                        WHERE s.plan_id = @planId
                    ), '[]'::jsonb),
                    'splitPreferences', (
                        SELECT to_jsonb(sp) FROM training_plan_split_preferences sp
                        WHERE sp.plan_id = @planId
                    ),
                    'adaptationProposals', COALESCE((
                        SELECT jsonb_agg(to_jsonb(a) ORDER BY a.id)
                        FROM training_adaptation_proposals a
                        WHERE a.plan_id = @planId
                    ), '[]'::jsonb)
                ))
                """;

            string json;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.Parameters.AddWithValue("planId", planId);
                _logger.LogInformation("Executing diagnostic snapshot SQL. PlanId={PlanId}; RequestId={RequestId}", planId, requestId);
                var result = await command.ExecuteScalarAsync(cancellationToken);
                json = Convert.ToString(result) ?? throw new InvalidOperationException("La exportación no devolvió datos.");
            }

            _logger.LogInformation("Diagnostic snapshot SQL completed. RequestId={RequestId}; JsonLength={JsonLength}; ElapsedMs={ElapsedMs}", requestId, json.Length, stopwatch.ElapsedMilliseconds);
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;
            var sessionCount = root.GetProperty("sessions").GetArrayLength();
            var mesocycleCount = root.GetProperty("mesocycles").GetArrayLength();

            _logger.LogInformation("Building diagnostic PDF. RequestId={RequestId}; Mesocycles={MesocycleCount}; Sessions={SessionCount}", requestId, mesocycleCount, sessionCount);
            QuestPDF.Settings.License = LicenseType.Community;
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(28);
                    page.DefaultTextStyle(style => style.FontFamily("Lato").FontSize(8).FontColor("#202124"));
                    page.Header().Column(header =>
                    {
                        header.Item().Text("DIETOEXPRESS · EXPORTACIÓN DE DIAGNÓSTICO").Bold().FontSize(13).FontColor("#0f766e");
                        header.Item().Text($"Plan: {planId} · Mesociclos: {mesocycleCount} · Sesiones: {sessionCount}")
                            .FontSize(9).FontColor("#555555");
                        header.Item().Text("Exportación íntegra de datos asociados. Los campos ausentes o nulos se conservan como tales.")
                            .FontSize(8).FontColor("#555555");
                    });
                    page.Content().PaddingTop(12).Column(content =>
                    {
                        content.Spacing(10);
                        content.Item().Text("INFORME DE PLANIFICACIÓN DE ENTRENAMIENTO").Bold().FontSize(15).FontColor("#0f766e");
                        content.Item().Text("Resumen legible de la planificación y sus datos asociados. Los valores no registrados se muestran como tales; no se presuponen cargas, RIR ni descansos.").FontSize(8).FontColor("#555555");
                        ComposeObject(content, "DATOS GENERALES DEL PLAN", root.GetProperty("plan"));
                        ComposeObject(content, "PERFIL DE ENTRENAMIENTO", root.GetProperty("profile"));
                        ComposeArray(content, "MACROCICLOS", root.GetProperty("macrocycles"));
                        ComposeArray(content, "FASES DEL MACROCICLO", root.GetProperty("macrocyclePhases"));
                        ComposeArray(content, "MESOCICLOS", root.GetProperty("mesocycles"));
                        ComposeArray(content, "MICROCICLOS", root.GetProperty("microcycles"));
                        ComposeSessions(content, root.GetProperty("sessions"), root.GetProperty("sessionRecords"));
                        ComposeObject(content, "PREFERENCIAS DE DISTRIBUCIÓN (SPLIT)", root.GetProperty("splitPreferences"));
                        ComposeArray(content, "PROPUESTAS DE ADAPTACIÓN", root.GetProperty("adaptationProposals"));
                        content.Item().PageBreak();
                        content.Item().Text("ANEXO · DATOS JSON ÍNTEGROS").Bold().FontSize(11).FontColor("#0f766e");
                        foreach (var line in json.Split('\n'))
                            content.Item().Text(line.Length == 0 ? " " : line).FontFamily("Lato").FontSize(5.8f);
                    });
                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Exportado el ").FontSize(7).FontColor("#777777");
                        text.Span(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")).FontSize(7).FontColor("#777777");
                        text.Span(" · Página ").FontSize(7).FontColor("#777777");
                        text.CurrentPageNumber().FontSize(7).FontColor("#777777");
                        text.Span(" / ").FontSize(7).FontColor("#777777");
                        text.TotalPages().FontSize(7).FontColor("#777777");
                    });
                });
            });

            var pdf = document.GeneratePdf();
            _logger.LogInformation("Diagnostic PDF generated successfully. RequestId={RequestId}; PdfBytes={PdfBytes}; ElapsedMs={ElapsedMs}", requestId, pdf.Length, stopwatch.ElapsedMilliseconds);
            return File(pdf, "application/pdf", $"dietoexpress-plan-{planId}-{DateTime.UtcNow:yyyyMMdd-HHmm}.pdf");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Test-plan PDF export was cancelled. RequestId={RequestId}; ElapsedMs={ElapsedMs}", requestId, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Test-plan PDF export failed. RequestId={RequestId}; ElapsedMs={ElapsedMs}; DatabaseState={DatabaseState}", requestId, stopwatch.ElapsedMilliseconds, connection.State);
            throw;
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }

    private static void ComposeSessions(ColumnDescriptor content, JsonElement sessions, JsonElement records)
    {
        content.Item().Text("SESIONES Y EJERCICIOS").Bold().FontSize(11).FontColor("#0f766e");
        if (sessions.ValueKind != JsonValueKind.Array || sessions.GetArrayLength() == 0)
        {
            content.Item().Text("No hay sesiones registradas.").Italic().FontColor("#777777");
            return;
        }

        var recordRows = records.ValueKind == JsonValueKind.Array
            ? records.EnumerateArray().ToList()
            : new List<JsonElement>();
        foreach (var session in sessions.EnumerateArray())
        {
            var sessionId = session.TryGetProperty("id", out var id) ? id.ToString() : "";
            content.Item().Border(1).BorderColor("#d9e2e8").Padding(8).Column(card =>
            {
                card.Spacing(4);
                var title = GetText(session, "title", "name", "session_name");
                var date = GetText(session, "scheduled_date", "scheduledDate", "date");
                card.Item().Text($"{(string.IsNullOrWhiteSpace(title) ? "Sesión" : title)} · {date}").Bold().FontSize(9);
                ComposeKeyValueTable(card, session);
                var matching = recordRows.Where(record =>
                    record.TryGetProperty("session_id", out var sid) && sid.ToString() == sessionId ||
                    record.TryGetProperty("sessionId", out var sidCamel) && sidCamel.ToString() == sessionId).ToList();
                if (matching.Count > 0)
                {
                    card.Item().PaddingTop(4).Text($"Ejercicios / registros ({matching.Count})").Bold().FontSize(8);
                    foreach (var record in matching)
                        ComposeKeyValueTable(card, record);
                }
            });
        }
    }

    private static void ComposeArray(ColumnDescriptor content, string heading, JsonElement array)
    {
        content.Item().Text(heading).Bold().FontSize(11).FontColor("#0f766e");
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
        {
            content.Item().Text("No hay datos registrados.").Italic().FontColor("#777777");
            return;
        }
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            index++;
            content.Item().BorderBottom(1).BorderColor("#e5e7eb").PaddingVertical(5).Column(row =>
            {
                row.Item().Text($"Registro {index}").Bold().FontSize(8);
                ComposeKeyValueTable(row, item);
            });
        }
    }

    private static void ComposeObject(ColumnDescriptor content, string heading, JsonElement value)
    {
        content.Item().Text(heading).Bold().FontSize(11).FontColor("#0f766e");
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            content.Item().Text("No hay datos registrados.").Italic().FontColor("#777777");
            return;
        }
        content.Item().Border(1).BorderColor("#e5e7eb").Padding(7).Element(box => ComposeKeyValueTable(box.Column(), value));
    }

    private static void ComposeKeyValueTable(ColumnDescriptor content, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return;
        content.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(1);
                columns.RelativeColumn(2);
            });
            foreach (var property in value.EnumerateObject())
            {
                table.Cell().Background("#f8fafc").Padding(3).Text(Humanize(property.Name)).FontSize(7).Bold();
                table.Cell().Padding(3).Text(FormatJsonValue(property.Value)).FontSize(7);
            }
        });
    }

    private static string FormatJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => "No especificado",
            JsonValueKind.String => value.GetString() ?? "No especificado",
            JsonValueKind.True => "Sí",
            JsonValueKind.False => "No",
            JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
            _ => value.ToString()
        };
    }

    private static string GetText(JsonElement value, params string[] names)
    {
        foreach (var name in names)
            if (value.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null)
                return FormatJsonValue(property);
        return "";
    }

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var chars = new List<char>(value.Length + 8);
        foreach (var ch in value)
        {
            if (char.IsUpper(ch)) chars.Add(' ');
            chars.Add(ch);
        }
        return char.ToUpperInvariant(chars[0]) + new string(chars.Skip(1).ToArray()).Replace('_', ' ');
    }
    }
}
