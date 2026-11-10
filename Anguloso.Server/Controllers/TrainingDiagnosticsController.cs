using System.Text.Json;
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
/// Exportación temporal de diagnóstico para revisar la única planificación de pruebas.
/// No acepta identificadores del cliente ni del plan: solo funciona si existe exactamente un plan.
/// </summary>
[ApiController]
[Route("api/training/diagnostics")]
[Authorize(Policy = "Professional")]
public sealed class TrainingDiagnosticsController : ControllerBase
{
    private readonly angulosodbContext _context;

    public TrainingDiagnosticsController(angulosodbContext context) => _context = context;

    [HttpGet("test-plan.pdf")]
    public async Task<IActionResult> ExportUniqueTestPlan(CancellationToken cancellationToken)
    {
        if (!User.IsInRole("superadmin")) return Forbid();

        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);

        try
        {
            int planCount;
            await using (var countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = "SELECT COUNT(*) FROM training_plans";
                planCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
            }

            if (planCount == 0)
                return NotFound(new { message = "No existe ninguna planificación de entrenamiento para exportar." });
            if (planCount != 1)
                return Conflict(new { message = $"La exportación está limitada a una única planificación de pruebas. Se encontraron {planCount}; no se ha exportado ninguna." });

            const string sql = """
                SELECT jsonb_pretty(jsonb_build_object(
                    'exportedAt', to_jsonb(clock_timestamp()),
                    'plan', (SELECT to_jsonb(p) FROM training_plans p LIMIT 1),
                    'profile', (
                        SELECT jsonb_build_object(
                            'id', f.id, 'clientId', f.client_id, 'enabled', f.enabled,
                            'primaryGoal', f.primary_goal, 'experienceLevel', f.experience_level,
                            'managementMode', f.management_mode
                        )
                        FROM training_plans p JOIN training_profiles f ON f.id = p.profile_id LIMIT 1
                    ),
                    'macrocycles', COALESCE((
                        SELECT jsonb_agg(to_jsonb(m) ORDER BY m.start_date, m.id)
                        FROM training_macrocycles m
                        WHERE m.plan_id = (SELECT id FROM training_plans LIMIT 1)
                    ), '[]'::jsonb),
                    'macrocyclePhases', COALESCE((
                        SELECT jsonb_agg(to_jsonb(ph) ORDER BY ph.start_date, ph.id)
                        FROM training_macrocycle_phases ph
                        JOIN training_macrocycles m ON m.id = ph.macrocycle_id
                        WHERE m.plan_id = (SELECT id FROM training_plans LIMIT 1)
                    ), '[]'::jsonb),
                    'mesocycles', COALESCE((
                        SELECT jsonb_agg(to_jsonb(m) ORDER BY m.sequence_number, m.id)
                        FROM training_mesocycles m
                        WHERE m.plan_id = (SELECT id FROM training_plans LIMIT 1)
                    ), '[]'::jsonb),
                    'microcycles', COALESCE((
                        SELECT jsonb_agg(to_jsonb(mc) ORDER BY mc.sequence_number, mc.id)
                        FROM training_microcycles mc
                        JOIN training_mesocycles m ON m.id = mc.mesocycle_id
                        WHERE m.plan_id = (SELECT id FROM training_plans LIMIT 1)
                    ), '[]'::jsonb),
                    'sessions', COALESCE((
                        SELECT jsonb_agg(to_jsonb(s) ORDER BY s.scheduled_date, s.id)
                        FROM training_sessions s
                        WHERE s.plan_id = (SELECT id FROM training_plans LIMIT 1)
                    ), '[]'::jsonb),
                    'sessionRecords', COALESCE((
                        SELECT jsonb_agg(to_jsonb(r) ORDER BY r.session_id, r.id)
                        FROM training_session_records r
                        JOIN training_sessions s ON s.id = r.session_id
                        WHERE s.plan_id = (SELECT id FROM training_plans LIMIT 1)
                    ), '[]'::jsonb),
                    'splitPreferences', (
                        SELECT to_jsonb(sp) FROM training_plan_split_preferences sp
                        WHERE sp.plan_id = (SELECT id FROM training_plans LIMIT 1)
                    ),
                    'adaptationProposals', COALESCE((
                        SELECT jsonb_agg(to_jsonb(a) ORDER BY a.id)
                        FROM training_adaptation_proposals a
                        WHERE a.plan_id = (SELECT id FROM training_plans LIMIT 1)
                    ), '[]'::jsonb)
                ))
                """;

            string json;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                var result = await command.ExecuteScalarAsync(cancellationToken);
                json = Convert.ToString(result) ?? throw new InvalidOperationException("La exportación no devolvió datos.");
            }

            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;
            var sessionCount = root.GetProperty("sessions").GetArrayLength();
            var mesocycleCount = root.GetProperty("mesocycles").GetArrayLength();

            QuestPDF.Settings.License = LicenseType.Community;
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(28);
                    page.DefaultTextStyle(style => style.FontFamily("Arial").FontSize(8).FontColor("#202124"));
                    page.Header().Column(header =>
                    {
                        header.Item().Text("DIETOEXPRESS · EXPORTACIÓN DE DIAGNÓSTICO").Bold().FontSize(13).FontColor("#0f766e");
                        header.Item().Text($"Planificaciones: 1 · Mesociclos: {mesocycleCount} · Sesiones: {sessionCount}")
                            .FontSize(9).FontColor("#555555");
                        header.Item().Text("Exportación íntegra de datos asociados. Los campos ausentes o nulos se conservan como tales.")
                            .FontSize(8).FontColor("#555555");
                    });
                    page.Content().PaddingTop(12).Column(content =>
                    {
                        content.Item().Text("DATOS ESTRUCTURADOS (JSON)").Bold().FontSize(10);
                        foreach (var line in json.Split('\n'))
                            content.Item().Text(line.Length == 0 ? " " : line)
                                .FontFamily("Arial").FontSize(5.8f);
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
            return File(pdf, "application/pdf", $"dietoexpress-plan-pruebas-{DateTime.UtcNow:yyyyMMdd-HHmm}.pdf");
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }
}
