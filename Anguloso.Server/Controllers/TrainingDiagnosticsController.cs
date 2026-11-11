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
                        content.Item().Text("DATOS ESTRUCTURADOS (JSON)").Bold().FontSize(10);
                        foreach (var line in json.Split('\n'))
                            content.Item().Text(line.Length == 0 ? " " : line)
                                .FontFamily("Lato").FontSize(5.8f);
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
    }
}
