using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Anguloso.Server.Logica;

// Documentación: este componente encapsula lógica compartida para mantener las reglas y transformaciones fuera de los puntos de entrada del frontend.
public class ConsultationPdfService
{
    public static (DateOnly Start, DateOnly End) GetDefaultTrainingPeriod(DateOnly today)
    {
        var start = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return (start, start.AddDays(27));
    }

    public byte[] GenerateConsultationPdf(clients client, DateOnly consultationDate, angulosodbContext context,
        DateOnly? trainingStartDate = null, DateOnly? trainingEndDate = null, bool includeTraining = true)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var defaultPeriod = GetDefaultTrainingPeriod(DateOnly.FromDateTime(DateTime.Today));
        var periodStart = trainingStartDate ?? defaultPeriod.Start;
        var periodEnd = trainingEndDate ?? defaultPeriod.End;
        if (periodEnd < periodStart) throw new ArgumentException("El final del periodo no puede ser anterior al inicio.");

        var nutritionist = context.users
            .AsNoTracking()
            .FirstOrDefault(u => u.id == client.user_id &&
                                 (!client.tenant_id.HasValue || u.tenant_id == client.tenant_id.Value));

        // Se obtiene la medición más reciente de la fecha y, por separado, todo el histórico anterior para poder generar tanto la foto del día como la evolución.
        var measurements = context.biometrics
            .AsNoTracking()
            .Where(b => b.client_id == client.id && b.measurement_date == consultationDate)
            .OrderByDescending(b => b.id)
            .ToList();

        var evolution = context.biometrics
            .AsNoTracking()
            .Where(b => b.client_id == client.id && b.measurement_date <= consultationDate)
            .OrderBy(b => b.measurement_date)
            .ThenBy(b => b.id)
            .ToList();

        // La actividad de dietas se filtra por tenant y por relación con el paciente/profesional para evitar mezclar información de otras consultas.
        var dietsCreated = context.diets
            .AsNoTracking()
            .Where(d => d.archived_at == null &&
                        (!client.tenant_id.HasValue || d.tenant_id == client.tenant_id.Value) &&
                        d.created_at.HasValue &&
                        d.created_at.Value >= DateTime.SpecifyKind(consultationDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) &&
                        d.created_at.Value < DateTime.SpecifyKind(consultationDate.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) &&
                        (d.user_id == client.user_id || d.client_diets.Any(cd => cd.client_id == client.id)))
            .OrderBy(d => d.created_at)
            .ToList();

        var dietsAssigned = context.client_diets
            .AsNoTracking()
            .Include(cd => cd.diet)
            .Where(cd => cd.client_id == client.id &&
                         (!client.tenant_id.HasValue || (cd.diet != null && cd.diet.tenant_id == client.tenant_id.Value)) &&
                         cd.start_date == consultationDate)
            .OrderBy(cd => cd.assigned_at)
            .ToList();

        // La dieta activa se determina por el intervalo de vigencia en la fecha de la consulta, no simplemente por el último registro asignado.
        var activeDiet = context.client_diets
            .AsNoTracking()
            .Include(cd => cd.diet)
            .Where(cd => cd.client_id == client.id &&
                         (!client.tenant_id.HasValue || (cd.diet != null && cd.diet.tenant_id == client.tenant_id.Value)) &&
                         cd.is_active == true &&
                         cd.start_date <= consultationDate &&
                         (!cd.end_date.HasValue || cd.end_date.Value >= consultationDate))
            .OrderByDescending(cd => cd.start_date)
            .FirstOrDefault();

        // Sin planificación (o si el profesional la excluye) el informe no incluye la sección de entrenamiento.
        var trainingPlans = includeTraining ? LoadTrainingPlans(client, periodStart, periodEnd, context) : new List<TrainingPlanPdf>();

        var previous = evolution
            .Where(b => b.measurement_date < consultationDate)
            .OrderByDescending(b => b.measurement_date)
            .ThenByDescending(b => b.id)
            .FirstOrDefault();

        if (nutritionist == null)
        {
            nutritionist = new users
            {
                full_name = "Nutricionista Desconocido",
                clinic_name = "DietoExpress",
                clinic_logo = null,
                clinic_address = null,
                clinic_phone = null
            };
        }

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontFamily("Lato").FontSize(9).FontColor("#2d2d2d"));

                page.Header().Element(c => ComposeHeader(c, nutritionist));

                page.Content().Element(content =>
                {
                    content.Column(col =>
                    {
                        col.Spacing(12);

                        col.Item().Text($"INFORME DE CONSULTA · {consultationDate:dd/MM/yyyy}")
                            .Bold().FontSize(16).FontColor("#0d9488");

                        col.Item().Element(c => ComposePatientSummary(c, client, nutritionist));

                        col.Item().Element(c => ComposeMeasurements(c, measurements, previous));

                        col.Item().Element(c => ComposeEvolution(c, evolution));

                        col.Item().Element(c => ComposeDietActivity(c, dietsCreated, dietsAssigned, activeDiet, consultationDate));
                    });
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Documento generado el ").FontColor("#888888").FontSize(8);
                    text.Span(DateTime.Now.ToString("dd/MM/yyyy HH:mm")).FontColor("#888888").FontSize(8);
                    text.Span("  |  Página ").FontColor("#888888").FontSize(8);
                    text.CurrentPageNumber().FontColor("#888888").FontSize(8);
                    text.Span(" de ").FontColor("#888888").FontSize(8);
                    text.TotalPages().FontColor("#888888").FontSize(8);
                });
            });

            // La planificación deportiva se separa siempre del informe clínico y comienza en página nueva.
            foreach (var plan in trainingPlans)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(36);
                    page.DefaultTextStyle(x => x.FontFamily("Lato").FontSize(9).FontColor("#2d2d2d"));
                    page.Header().Element(c => ComposeHeader(c, nutritionist));
                    page.Content().Element(c => ComposeTrainingPlan(c, plan));
                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Documento generado el ").FontColor("#888888").FontSize(8);
                        text.Span(DateTime.Now.ToString("dd/MM/yyyy HH:mm")).FontColor("#888888").FontSize(8);
                        text.Span("  |  Página ").FontColor("#888888").FontSize(8);
                        text.CurrentPageNumber().FontColor("#888888").FontSize(8);
                        text.Span(" de ").FontColor("#888888").FontSize(8);
                        text.TotalPages().FontColor("#888888").FontSize(8);
                    });
                });
            }
        });

        return document.GeneratePdf();
    }


    public byte[] GenerateTrainingPdf(clients client, int planId, DateOnly startDate, DateOnly endDate, angulosodbContext context)
    {
        if (endDate < startDate) throw new ArgumentException("El final del periodo no puede ser anterior al inicio.");
        QuestPDF.Settings.License = LicenseType.Community;
        var nutritionist = context.users.AsNoTracking().FirstOrDefault(u => u.id == client.user_id &&
            (!client.tenant_id.HasValue || u.tenant_id == client.tenant_id.Value)) ?? new users
            { full_name = "Nutricionista Desconocido", clinic_name = "DietoExpress", clinic_logo = null, clinic_address = null, clinic_phone = null };
        var plans = LoadTrainingPlans(client, startDate, endDate, context, planId);
        if (plans.Count == 0) throw new InvalidOperationException("No se encontró una planificación para el periodo solicitado.");
        var document = Document.Create(container =>
        {
            foreach (var plan in plans)
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(36);
                    page.DefaultTextStyle(x => x.FontFamily("Lato").FontSize(9).FontColor("#2d2d2d"));
                    page.Header().Element(c => ComposeHeader(c, nutritionist));
                    page.Content().Element(c => ComposeTrainingPlan(c, plan));
                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Documento generado el ").FontColor("#888888").FontSize(8);
                        text.Span(DateTime.Now.ToString("dd/MM/yyyy HH:mm")).FontColor("#888888").FontSize(8);
                        text.Span("  |  Página ").FontColor("#888888").FontSize(8);
                        text.CurrentPageNumber().FontColor("#888888").FontSize(8);
                        text.Span(" de ").FontColor("#888888").FontSize(8);
                        text.TotalPages().FontColor("#888888").FontSize(8);
                    });
                });
        });
        return document.GeneratePdf();
    }

    private List<TrainingPlanPdf> LoadTrainingPlans(clients client, DateOnly startDate, DateOnly endDate, angulosodbContext context, int? onlyPlanId = null)
    {
        var plans = new List<TrainingPlanPdf>();
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        try
        {
            if (shouldClose) connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT p.id, p.name, p.periodization_model, p.start_date, p.end_date, p.status, p.notes
                FROM training_plans p
                INNER JOIN training_profiles f ON f.id = p.profile_id
                INNER JOIN clients c ON c.id = f.client_id
                WHERE c.id = @clientId
                  AND (@tenantId::int IS NULL OR c.tenant_id = @tenantId::int)
                  AND p.start_date <= @endDate
                  AND (p.end_date IS NULL OR p.end_date >= @startDate)
                  AND (@planId::int IS NULL OR p.id = @planId::int)
                ORDER BY p.start_date DESC, p.id DESC
                """;
            var clientParam = command.CreateParameter();
            clientParam.ParameterName = "clientId"; clientParam.Value = client.id; command.Parameters.Add(clientParam);
            var tenantParam = command.CreateParameter();
            tenantParam.ParameterName = "tenantId"; tenantParam.Value = client.tenant_id.HasValue ? client.tenant_id.Value : DBNull.Value; command.Parameters.Add(tenantParam);
            var startParam = command.CreateParameter();
            startParam.ParameterName = "startDate"; startParam.Value = startDate.ToDateTime(TimeOnly.MinValue); command.Parameters.Add(startParam);
            var endParam = command.CreateParameter();
            endParam.ParameterName = "endDate"; endParam.Value = endDate.ToDateTime(TimeOnly.MinValue); command.Parameters.Add(endParam);
            var planIdParam = command.CreateParameter();
            planIdParam.ParameterName = "planId"; planIdParam.Value = onlyPlanId.HasValue ? onlyPlanId.Value : DBNull.Value; command.Parameters.Add(planIdParam);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    plans.Add(new TrainingPlanPdf
                    {
                        Id = Convert.ToInt32(reader.GetValue(0)), PeriodStart = startDate, PeriodEnd = endDate,
                        Name = reader.GetString(1),
                        Model = reader.IsDBNull(2) ? "—" : reader.GetString(2),
                        StartDate = reader.GetFieldValue<DateOnly>(3),
                        EndDate = reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
                        Status = reader.IsDBNull(5) ? "—" : reader.GetString(5),
                        Notes = reader.IsDBNull(6) ? null : reader.GetString(6)
                    });
                }
            }
            foreach (var plan in plans)
            {
                using var mesoCommand = connection.CreateCommand();
                mesoCommand.CommandText = """
                    SELECT name, sequence_number, start_date, end_date, objective, notes
                    FROM training_mesocycles
                    WHERE plan_id = @planId AND start_date <= @endDate
                      AND (end_date IS NULL OR end_date >= @startDate)
                    ORDER BY sequence_number, id
                    """;
                var planParam = mesoCommand.CreateParameter(); planParam.ParameterName = "planId"; planParam.Value = plan.Id; mesoCommand.Parameters.Add(planParam);
                var mesoStartParam = mesoCommand.CreateParameter(); mesoStartParam.ParameterName = "startDate"; mesoStartParam.Value = startDate.ToDateTime(TimeOnly.MinValue); mesoCommand.Parameters.Add(mesoStartParam);
                var mesoEndParam = mesoCommand.CreateParameter(); mesoEndParam.ParameterName = "endDate"; mesoEndParam.Value = endDate.ToDateTime(TimeOnly.MinValue); mesoCommand.Parameters.Add(mesoEndParam);
                using var mesoReader = mesoCommand.ExecuteReader();
                while (mesoReader.Read())
                    plan.Mesocycles.Add(new TrainingMesoPdf(
                        mesoReader.GetString(0), mesoReader.GetInt32(1), mesoReader.GetFieldValue<DateOnly>(2),
                        mesoReader.IsDBNull(3) ? null : mesoReader.GetFieldValue<DateOnly>(3),
                        mesoReader.IsDBNull(4) ? null : mesoReader.GetString(4),
                        mesoReader.IsDBNull(5) ? null : mesoReader.GetString(5)));
            }
            foreach (var plan in plans)
            {
                using var macroCommand = connection.CreateCommand();
                macroCommand.CommandText = """
                    SELECT name, periodization_model, start_date, end_date, notes
                    FROM training_macrocycles WHERE plan_id = @planId ORDER BY start_date, id LIMIT 1
                    """;
                var macroPlanParam = macroCommand.CreateParameter(); macroPlanParam.ParameterName = "planId"; macroPlanParam.Value = plan.Id; macroCommand.Parameters.Add(macroPlanParam);
                using (var macroReader = macroCommand.ExecuteReader())
                    if (macroReader.Read())
                    {
                        plan.MacrocycleName = macroReader.GetString(0);
                        plan.MacrocycleModel = macroReader.GetString(1);
                        plan.MacrocycleStart = macroReader.GetFieldValue<DateOnly>(2);
                        plan.ShowMacrocycleOverview = startDate <= plan.MacrocycleStart.Value && endDate >= plan.MacrocycleStart.Value;
                        plan.MacrocycleEnd = macroReader.IsDBNull(3) ? null : macroReader.GetFieldValue<DateOnly>(3);
                        plan.MacrocycleNotes = macroReader.IsDBNull(4) ? null : macroReader.GetString(4);
                    }
                if (plan.MacrocycleStart.HasValue)
                {
                    using var phasesCommand = connection.CreateCommand();
                    phasesCommand.CommandText = """
                        SELECT ph.name, ph.sequence_number, ph.start_date, ph.end_date, ph.objective, ph.phase_type
                        FROM training_macrocycle_phases ph
                        JOIN training_macrocycles mc ON mc.id = ph.macrocycle_id
                        WHERE mc.plan_id = @planId ORDER BY ph.sequence_number, ph.start_date, ph.id
                        """;
                    var phasePlanParam = phasesCommand.CreateParameter(); phasePlanParam.ParameterName = "planId"; phasePlanParam.Value = plan.Id; phasesCommand.Parameters.Add(phasePlanParam);
                    using var phaseReader = phasesCommand.ExecuteReader();
                    while (phaseReader.Read()) plan.MacrocyclePhases.Add(new TrainingPhasePdf(
                        phaseReader.GetString(0), phaseReader.GetInt32(1), phaseReader.GetFieldValue<DateOnly>(2),
                        phaseReader.GetFieldValue<DateOnly>(3), phaseReader.IsDBNull(4) ? null : phaseReader.GetString(4), phaseReader.GetString(5)));
                }
                using var sessionsCommand = connection.CreateCommand();
                sessionsCommand.CommandText = """
                    SELECT scheduled_date, title, session_type, duration_minutes, intensity_target, instructions,
                           exercise_prescription, exercise_prescription_data::text
                    FROM training_sessions
                    WHERE plan_id = @planId AND scheduled_date >= @startDate AND scheduled_date <= @endDate
                    ORDER BY scheduled_date, id
                    """;
                var planParam = sessionsCommand.CreateParameter(); planParam.ParameterName = "planId"; planParam.Value = plan.Id; sessionsCommand.Parameters.Add(planParam);
                var sessionStartParam = sessionsCommand.CreateParameter(); sessionStartParam.ParameterName = "startDate"; sessionStartParam.Value = startDate.ToDateTime(TimeOnly.MinValue); sessionsCommand.Parameters.Add(sessionStartParam);
                var sessionEndParam = sessionsCommand.CreateParameter(); sessionEndParam.ParameterName = "endDate"; sessionEndParam.Value = endDate.ToDateTime(TimeOnly.MinValue); sessionsCommand.Parameters.Add(sessionEndParam);
                using var sessionReader = sessionsCommand.ExecuteReader();
                while (sessionReader.Read())
                    plan.Sessions.Add(new TrainingSessionPdf(
                        sessionReader.GetFieldValue<DateOnly>(0), sessionReader.GetString(1),
                        sessionReader.IsDBNull(2) ? "general" : sessionReader.GetString(2),
                        sessionReader.IsDBNull(3) ? null : sessionReader.GetInt32(3),
                        sessionReader.IsDBNull(4) ? null : sessionReader.GetString(4),
                        sessionReader.IsDBNull(5) ? null : sessionReader.GetString(5),
                        sessionReader.IsDBNull(6) ? null : sessionReader.GetString(6),
                        sessionReader.IsDBNull(7) ? null : sessionReader.GetString(7)));
            }
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
        {
            // Compatibilidad con instalaciones anteriores donde el módulo deportivo aún no creó sus tablas.
            return new List<TrainingPlanPdf>();
        }
        finally
        {
            if (shouldClose) connection.Close();
        }
        return plans;
    }

    private static readonly System.Globalization.CultureInfo EsCulture = new("es-ES");

    private static string PhaseTypeLabel(string type) => type switch
    {
        "accumulation" => "Acumulación",
        "intensification" => "Intensificación",
        "deload" => "Descarga",
        "realization" => "Realización",
        _ => "General"
    };

    private static (string Strong, string Pale) PhaseColors(string type) => type switch
    {
        "accumulation" => ("#0d9488", "#ccfbf1"),
        "intensification" => ("#d97706", "#fef3c7"),
        "deload" => ("#64748b", "#e2e8f0"),
        "realization" => ("#7c3aed", "#ede9fe"),
        _ => ("#2563eb", "#dbeafe")
    };

    private static string SessionTypeLabel(string type) => type.ToLowerInvariant() switch
    {
        "strength" => "Fuerza",
        "cardio" => "Cardio",
        "mobility" => "Movilidad",
        "recovery" => "Recuperación",
        "hiit" => "HIIT",
        "general" => "General",
        _ => type
    };

    private static string ModelLabel(string model) => model.ToLowerInvariant() switch
    {
        "custom" => "Personalizado",
        "linear" => "Lineal",
        "undulating" => "Ondulante",
        "block" => "Por bloques",
        _ => model
    };

    private static string StatusLabel(string status) => status.ToLowerInvariant() switch
    {
        "draft" => "Borrador",
        "active" => "Activo",
        "completed" => "Completado",
        "archived" => "Archivado",
        _ => status
    };

    private void ComposeTrainingPlan(IContainer container, TrainingPlanPdf plan)
    {
        container.Column(col =>
        {
            col.Spacing(10);

            // Cabecera del plan
            col.Item().Background("#0f766e").Padding(12).Column(head =>
            {
                head.Item().Text("PLANIFICACIÓN DE ENTRENAMIENTO").Bold().FontSize(9).FontColor("#99f6e4");
                head.Item().Text(plan.Name).Bold().FontSize(18).FontColor("#ffffff");
                head.Item().PaddingTop(2).Text($"{ModelLabel(plan.Model)} · {StatusLabel(plan.Status)} · Vigencia {plan.StartDate:dd/MM/yyyy} — {(plan.EndDate?.ToString("dd/MM/yyyy") ?? "sin fecha final")}")
                    .FontSize(9).FontColor("#ccfbf1");
            });

            // Resumen numérico del periodo del documento
            var totalMinutes = plan.Sessions.Sum(s => s.Duration ?? 0);
            var weekCount = plan.Sessions.Select(s => s.Date.AddDays(-(((int)s.Date.DayOfWeek + 6) % 7))).Distinct().Count();
            col.Item().Row(row =>
            {
                row.Spacing(8);
                void Stat(string value, string label) => row.RelativeItem().Background("#f0fdfa").Border(1).BorderColor("#99f6e4").CornerRadius(4).Padding(7).Column(c =>
                {
                    c.Item().AlignCenter().Text(value).Bold().FontSize(12.5f).FontColor("#0f766e");
                    c.Item().AlignCenter().Text(label).FontSize(7.5f).FontColor("#475569");
                });
                Stat($"{plan.PeriodStart:dd/MM}–{plan.PeriodEnd:dd/MM}", "Periodo del documento");
                Stat(plan.Sessions.Count.ToString(), "Sesiones");
                Stat(weekCount.ToString(), "Semanas con entrenamiento");
                Stat(totalMinutes > 0 ? $"{totalMinutes / 60}h {totalMinutes % 60:00}m" : "—", "Tiempo planificado");
            });

            if (!string.IsNullOrWhiteSpace(plan.Notes))
                col.Item().Text($"Notas del plan: {plan.Notes}").FontSize(8.5f).Italic().FontColor("#475569");

            ComposeMacrocycleOverview(col, plan);

            col.Item().PaddingTop(4).Text("RUTINAS").Bold().FontSize(13).FontColor("#0f766e");
            col.Item().Text("Anota en cada serie el peso y las repeticiones realizadas; marca «Hecho» al completarla.")
                .FontSize(8).FontColor("#64748b");
            if (plan.Sessions.Count == 0)
                col.Item().Text("No hay sesiones de entrenamiento en este periodo.").Italic().FontColor("#777777");

            var weeks = plan.Sessions
                .GroupBy(session => session.Date.AddDays(-(((int)session.Date.DayOfWeek + 6) % 7)))
                .OrderBy(group => group.Key)
                .ToList();
            foreach (var week in weeks)
            {
                var weekNumber = weeks.IndexOf(week) + 1;
                var phase = plan.MacrocyclePhases.FirstOrDefault(p => p.Start <= week.Key.AddDays(6) && p.End >= week.Key);
                col.Item().PaddingTop(6).EnsureSpace(120).Background("#f1f5f9").Padding(6).Row(row =>
                {
                    row.RelativeItem().Text($"SEMANA {weekNumber} · {week.Key:dd/MM} – {week.Key.AddDays(6):dd/MM/yyyy}").Bold().FontColor("#334155");
                    if (phase != null)
                    {
                        var colors = PhaseColors(phase.Type);
                        row.AutoItem().Background(colors.Strong).PaddingHorizontal(6).PaddingVertical(1)
                            .Text($"{phase.Name} · {PhaseTypeLabel(phase.Type)}").FontSize(7.5f).Bold().FontColor("#ffffff");
                    }
                });
                foreach (var session in week)
                    col.Item().EnsureSpace(260).Element(c => ComposeSessionCard(c, session));
            }
        });
    }

    private static void ComposeSessionCard(IContainer container, TrainingSessionPdf session)
    {
        container.Border(1).BorderColor("#cbd5e1").Column(card =>
        {
            card.Item().Background("#134e4a").Padding(7).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(session.Date.ToString("dddd dd/MM/yyyy", EsCulture).ToUpper(EsCulture)).FontSize(7.5f).FontColor("#99f6e4").Bold();
                    c.Item().Text(session.Title).Bold().FontSize(11).FontColor("#ffffff");
                });
                row.AutoItem().AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text(SessionTypeLabel(session.Type)).FontSize(8).Bold().FontColor("#ccfbf1");
                    c.Item().AlignRight().Text($"{(session.Duration.HasValue ? $"{session.Duration.Value} min" : "duración libre")}" +
                        (string.IsNullOrWhiteSpace(session.Intensity) ? "" : $" · {session.Intensity}")).FontSize(8).FontColor("#ffffff");
                });
            });

            card.Item().Padding(8).Column(body =>
            {
                body.Spacing(7);
                if (!string.IsNullOrWhiteSpace(session.Instructions))
                    body.Item().Background("#fffbeb").Padding(5).Text(session.Instructions).FontSize(8.5f).FontColor("#78350f");

                var exercises = ParseExercises(session.StructuredPrescription);
                if (exercises.Count > 0)
                {
                    for (var i = 0; i < exercises.Count; i++)
                    {
                        var exercise = exercises[i];
                        var number = i + 1;
                        body.Item().ShowEntire().Element(c => ComposeExerciseLog(c, number, exercise));
                    }
                }
                else if (!string.IsNullOrWhiteSpace(session.Prescription))
                {
                    body.Item().Border(1).BorderColor("#e2e8f0").Padding(6).Text(session.Prescription).FontSize(9);
                }

                // Espacio para anotaciones a mano durante el entrenamiento
                body.Item().ShowEntire().Column(notes =>
                {
                    notes.Item().Text("Notas de la sesión (sensaciones, molestias, cambios de ejercicio…)").FontSize(7.5f).FontColor("#64748b");
                    for (var line = 0; line < 2; line++)
                        notes.Item().Height(15).BorderBottom(0.6f).BorderColor("#cbd5e1");
                });
            });
        });
    }

    private sealed record ExerciseRow(string Name, int Sets, string Reps, string? Rir, string? LoadKg, int? RestSeconds, string? Progression, int WarmupSets, string? WarmupNote);

    private static List<ExerciseRow> ParseExercises(string? json)
    {
        var result = new List<ExerciseRow>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("exercises", out var exercises) || exercises.ValueKind != System.Text.Json.JsonValueKind.Array)
                return result;
            foreach (var exercise in exercises.EnumerateArray())
            {
                // Los datos históricos pueden venir en camelCase o PascalCase.
                string? Get(string name)
                {
                    if (exercise.TryGetProperty(name, out var value) && value.ValueKind is not (System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined)) return value.ToString();
                    var pascal = char.ToUpperInvariant(name[0]) + name[1..];
                    return exercise.TryGetProperty(pascal, out var other) && other.ValueKind is not (System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined) ? other.ToString() : null;
                }
                int? ToInt(string? text) => int.TryParse(text, out var number) ? number : null;
                var min = Get("minRepetitions"); var max = Get("maxRepetitions");
                var reps = min == null && max == null ? "—" : (min == max || max == null ? min! : min == null ? max! : $"{min}–{max}");
                result.Add(new ExerciseRow(
                    Get("exerciseNameSnapshot") ?? "Ejercicio",
                    Math.Clamp(ToInt(Get("sets")) ?? 1, 1, 12), reps, Get("targetRepetitionsInReserve"), Get("targetLoadKg"),
                    ToInt(Get("restSeconds")), Get("progressionRule"), Math.Clamp(ToInt(Get("warmupSets")) ?? 0, 0, 6), Get("warmupNote")));
            }
        }
        catch (System.Text.Json.JsonException)
        {
            result.Clear();
        }
        return result;
    }

    // Bloque de un ejercicio pensado para el gimnasio: prescripción arriba y una fila en blanco por serie.
    private static void ComposeExerciseLog(IContainer container, int number, ExerciseRow exercise)
    {
        container.Column(block =>
        {
            block.Item().Row(row =>
            {
                row.ConstantItem(20).Height(20).Background("#0d9488").AlignCenter().AlignMiddle().Text(number.ToString()).Bold().FontColor("#ffffff");
                row.RelativeItem().PaddingLeft(6).AlignMiddle().Text(exercise.Name).Bold().FontSize(10.5f);
            });
            var parts = new List<string> { $"{exercise.Sets} series × {exercise.Reps} reps" };
            if (!string.IsNullOrWhiteSpace(exercise.Rir)) parts.Add($"RIR {exercise.Rir}");
            if (exercise.RestSeconds.HasValue) parts.Add($"descanso {exercise.RestSeconds} s");
            if (!string.IsNullOrWhiteSpace(exercise.LoadKg)) parts.Add($"carga orientativa {exercise.LoadKg} kg");
            block.Item().PaddingLeft(26).PaddingBottom(3).Text(string.Join("  ·  ", parts)).FontSize(8.5f).FontColor("#0f766e").Bold();
            if (exercise.WarmupSets > 0 || !string.IsNullOrWhiteSpace(exercise.WarmupNote))
                block.Item().PaddingLeft(26).PaddingBottom(3).Text($"Calentamiento: {(exercise.WarmupSets > 0 ? $"{exercise.WarmupSets} series aproximación" : "")}{(string.IsNullOrWhiteSpace(exercise.WarmupNote) ? "" : $" · {exercise.WarmupNote}")}")
                    .FontSize(7.5f).FontColor("#64748b");

            block.Item().PaddingLeft(26).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(34); columns.RelativeColumn(1.1f); columns.RelativeColumn(1.4f);
                    columns.RelativeColumn(1.4f); columns.RelativeColumn(1f); columns.ConstantColumn(30);
                });
                IContainer Head(IContainer c) => c.Background("#f0fdfa").Border(0.6f).BorderColor("#99f6e4").PaddingVertical(2).PaddingHorizontal(3);
                IContainer Cell(IContainer c) => c.Border(0.6f).BorderColor("#cbd5e1").Height(17);
                table.Header(header =>
                {
                    header.Cell().Element(Head).AlignCenter().Text("Serie").FontSize(7.5f).Bold();
                    header.Cell().Element(Head).AlignCenter().Text("Objetivo").FontSize(7.5f).Bold();
                    header.Cell().Element(Head).AlignCenter().Text("Peso (kg)").FontSize(7.5f).Bold();
                    header.Cell().Element(Head).AlignCenter().Text("Reps hechas").FontSize(7.5f).Bold();
                    header.Cell().Element(Head).AlignCenter().Text("RIR").FontSize(7.5f).Bold();
                    header.Cell().Element(Head).AlignCenter().Text("Hecho").FontSize(6.5f).Bold();
                });
                for (var set = 1; set <= exercise.Sets; set++)
                {
                    table.Cell().Element(Cell).AlignCenter().AlignMiddle().Text(set.ToString()).FontSize(8).Bold().FontColor("#475569");
                    table.Cell().Element(Cell).AlignCenter().AlignMiddle().Text($"{exercise.Reps} reps").FontSize(8).FontColor("#64748b");
                    table.Cell().Element(Cell);
                    table.Cell().Element(Cell);
                    table.Cell().Element(Cell);
                    table.Cell().Element(Cell);
                }
            });
        });
    }

    // Esquema del macrociclo: barra proporcional por fases (intensas = incluidas en este documento) y leyenda con objetivos.
    private static void ComposeMacrocycleOverview(ColumnDescriptor col, TrainingPlanPdf plan)
    {
        var phases = plan.MacrocyclePhases;
        if (phases.Count == 0)
        {
            if (plan.Mesocycles.Count == 0) return;
            phases = plan.Mesocycles.Select(m => new TrainingPhasePdf(m.Name, m.Sequence, m.Start, m.End ?? m.Start, m.Objective, "general")).ToList();
        }

        col.Item().PaddingTop(4).Text("ESQUEMA DEL MACROCICLO").Bold().FontSize(13).FontColor("#0f766e");
        if (!string.IsNullOrWhiteSpace(plan.MacrocycleName))
            col.Item().Text($"{plan.MacrocycleName} · {plan.MacrocycleStart:dd/MM/yyyy} — {(plan.MacrocycleEnd?.ToString("dd/MM/yyyy") ?? "sin fecha final")}").FontSize(8.5f).FontColor("#475569");

        col.Item().ShowEntire().Column(block =>
        {
            block.Spacing(6);
            block.Item().Row(row =>
            {
                foreach (var phase in phases)
                {
                    var days = Math.Max(1, phase.End.DayNumber - phase.Start.DayNumber + 1);
                    var active = phase.Start <= plan.PeriodEnd && phase.End >= plan.PeriodStart;
                    var colors = PhaseColors(phase.Type);
                    row.RelativeItem((float)days).Border(0.5f).BorderColor("#ffffff")
                        .Background(active ? colors.Strong : colors.Pale).Padding(4).Column(segment =>
                    {
                        segment.Item().Text(phase.Name).Bold().FontSize(7).FontColor(active ? "#ffffff" : "#334155");
                        segment.Item().Text($"{phase.Start:dd/MM}–{phase.End:dd/MM}").FontSize(6).FontColor(active ? "#ffffff" : "#475569");
                    });
                }
            });

            foreach (var phase in phases)
            {
                var active = phase.Start <= plan.PeriodEnd && phase.End >= plan.PeriodStart;
                var colors = PhaseColors(phase.Type);
                block.Item().Row(row =>
                {
                    row.ConstantItem(9).Height(9).Background(colors.Strong);
                    row.RelativeItem().PaddingLeft(6).Text(text =>
                    {
                        text.Span($"{phase.Sequence}. {phase.Name}").Bold().FontSize(8.5f);
                        text.Span($"  {PhaseTypeLabel(phase.Type)} · {phase.Start:dd/MM/yy}–{phase.End:dd/MM/yy} · {Math.Max(1, (phase.End.DayNumber - phase.Start.DayNumber + 1) / 7)} sem")
                            .FontSize(8).FontColor("#64748b");
                        if (active) text.Span("  (en este documento)").FontSize(7.5f).Bold().FontColor("#0f766e");
                        if (!string.IsNullOrWhiteSpace(phase.Objective)) text.Span($"\n{phase.Objective}").FontSize(8).FontColor("#475569");
                    });
                });
            }
            block.Item().Text("Color intenso: fases que coinciden con el periodo de este documento.").FontSize(7).FontColor("#64748b");
        });
    }

    private sealed class TrainingPlanPdf
    {
        public DateOnly PeriodStart { get; set; }
        public DateOnly PeriodEnd { get; set; }
        public bool ShowMacrocycleOverview { get; set; }
        public string? MacrocycleName { get; set; }
        public string? MacrocycleModel { get; set; }
        public DateOnly? MacrocycleStart { get; set; }
        public DateOnly? MacrocycleEnd { get; set; }
        public string? MacrocycleNotes { get; set; }
        public List<TrainingPhasePdf> MacrocyclePhases { get; } = new();
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public string Model { get; init; } = "";
        public DateOnly StartDate { get; init; }
        public DateOnly? EndDate { get; init; }
        public string Status { get; init; } = "";
        public string? Notes { get; init; }
        public List<TrainingMesoPdf> Mesocycles { get; } = new();
        public List<TrainingSessionPdf> Sessions { get; } = new();
    }

    private sealed record TrainingPhasePdf(string Name, int Sequence, DateOnly Start, DateOnly End, string? Objective, string Type);
    private sealed record TrainingMesoPdf(string Name, int Sequence, DateOnly Start, DateOnly? End, string? Objective, string? Notes);
    private sealed record TrainingSessionPdf(DateOnly Date, string Title, string Type, int? Duration, string? Intensity, string? Instructions, string? Prescription, string? StructuredPrescription);

    private void ComposeHeader(IContainer container, users nutritionist)
    {
        container.Column(header =>
        {
            header.Item().Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(nutritionist?.clinic_name ?? "DietoExpress")
                        .Bold().FontSize(15).FontColor("#0d9488");
                    if (!string.IsNullOrWhiteSpace(nutritionist?.clinic_address))
                        col.Item().Text(nutritionist.clinic_address).FontSize(8);
                    if (!string.IsNullOrWhiteSpace(nutritionist?.clinic_phone))
                        col.Item().Text(nutritionist.clinic_phone).FontSize(8);
                });

                row.ConstantItem(150).AlignRight().Column(col =>
                {
                    col.Item().Text("INFORME CLÍNICO").Bold().FontSize(11).FontColor("#0d9488");
                    if (!string.IsNullOrWhiteSpace(nutritionist?.full_name))
                        col.Item().Text(nutritionist.full_name).FontSize(8);
                });
            });

            header.Item().BorderBottom(2).BorderColor("#0d9488");
        });
    }

    private void ComposePatientSummary(IContainer container, clients client, users nutritionist)
    {
        container.Border(1).BorderColor("#d9dce3").Padding(10).Column(col =>
        {
            col.Item().Background("#0d9488").Padding(7).Text("RESUMEN DEL PACIENTE").Bold().FontSize(11).FontColor("#ffffff");
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Cell().Text($"Paciente: {client.full_name}");
                table.Cell().Text($"Fecha de alta: {(client.created_at?.ToString("dd/MM/yyyy") ?? "—")}");
                table.Cell().Text($"Fecha de nacimiento: {(client.birth_date?.ToString("dd/MM/yyyy") ?? "—")}");
                table.Cell().Text($"Profesional: {nutritionist?.full_name ?? "—"}");
            });
        });
    }

    private void ComposeMeasurements(IContainer container, List<biometrics> measurements, biometrics? previous)
    {
        container.Column(col =>
        {
            col.Item().Background("#0d9488").Padding(7).Text("MEDICIONES DEL DÍA").Bold().FontSize(11).FontColor("#ffffff");

            if (measurements.Count == 0)
            {
                col.Item().Text("No hay mediciones registradas para esta fecha.").Italic().FontColor("#777777");
                return;
            }

            foreach (var b in measurements)
            {
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        for (var i = 0; i < 4; i++) columns.RelativeColumn();
                    });

                    AddMetric(table, "Peso", b.weight, "kg");
                    AddMetric(table, "% grasa", b.body_fat, "%");
                    AddMetric(table, "Masa muscular", b.muscle_mass, "kg");
                    AddMetric(table, "IMC", CalculateBmi(b), "");
                    AddMetric(table, "Cintura", b.waist, "cm");
                    AddMetric(table, "Cadera", b.hip, "cm");
                    AddMetric(table, "Grasa visceral", b.visceral_fat, "");
                    AddMetric(table, "Cuello", b.neck, "cm");
                });

                if (!string.IsNullOrWhiteSpace(b.notes))
                    col.Item().PaddingTop(4).Text($"Notas: {b.notes}").FontSize(8);

                if (previous != null)
                {
                    col.Item().PaddingTop(4).Text(text =>
                    {
                        text.Span("Comparación con el control anterior: ").Bold();
                        AddDelta(text, "peso", b.weight, previous.weight, " kg");
                        AddDelta(text, "grasa", b.body_fat, previous.body_fat, " pp");
                        AddDelta(text, "cintura", b.waist, previous.waist, " cm");
                    });
                }
            }
        });
    }

    private void ComposeEvolution(IContainer container, List<biometrics> evolution)
    {
        container.Column(col =>
        {
            col.Item().Background("#0d9488").Padding(7).Text("EVOLUCIÓN Y PROGRESO").Bold().FontSize(11).FontColor("#ffffff");

            if (evolution.Count == 0)
            {
                col.Item().Text("No hay histórico biométrico registrado.").Italic().FontColor("#777777");
                return;
            }

            var rows = evolution.TakeLast(18).ToList();

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(62);
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Background("#f0fdfa").Padding(4).Text("Fecha").Bold();
                    header.Cell().Background("#f0fdfa").Padding(4).Text("Peso").Bold();
                    header.Cell().Background("#f0fdfa").Padding(4).Text("% grasa").Bold();
                    header.Cell().Background("#f0fdfa").Padding(4).Text("M. muscular").Bold();
                    header.Cell().Background("#f0fdfa").Padding(4).Text("Cintura").Bold();
                });

                foreach (var b in rows)
                {
                    table.Cell().Padding(3).Text(b.measurement_date.ToString("dd/MM/yy"));
                    table.Cell().Padding(3).Text(Format(b.weight, "kg"));
                    table.Cell().Padding(3).Text(Format(b.body_fat, "%"));
                    table.Cell().Padding(3).Text(Format(b.muscle_mass, "kg"));
                    table.Cell().Padding(3).Text(Format(b.waist, "cm"));
                }
            });

            if (evolution.Count > 1)
            {
                var first = evolution.First();
                var last = evolution.Last();

                col.Item().PaddingTop(5).Text(text =>
                {
                    text.Span("Cambio desde el primer registro: ").Bold();
                    AddDelta(text, "peso", last.weight, first.weight, " kg");
                    AddDelta(text, "grasa", last.body_fat, first.body_fat, " pp");
                    AddDelta(text, "cintura", last.waist, first.waist, " cm");
                });
            }
        });
    }

    private void ComposeDietActivity(
        IContainer container,
        List<diets> dietsCreated,
        List<client_diets> dietsAssigned,
        client_diets? activeDiet,
        DateOnly date)
    {
        container.Column(col =>
        {
            col.Item().Text("ACTIVIDAD DIETÉTICA DEL DÍA").Bold().FontSize(11).FontColor("#0d9488");

            if (dietsCreated.Count == 0 && dietsAssigned.Count == 0)
            {
                col.Item().Text("No consta creación ni asignación de una dieta en esta fecha.")
                    .Italic().FontColor("#777777");
            }

            if (dietsCreated.Count > 0)
            {
                col.Item().Text("Dietas creadas").Bold();
                foreach (var diet in dietsCreated)
                {
                    col.Item().PaddingLeft(8).Text(
                        $"• {diet.name} · {Format(diet.target_kcal, "kcal")} · " +
                        $"P {Format(diet.target_protein, "g")} · C {Format(diet.target_carbs, "g")} · G {Format(diet.target_fat, "g")}");
                    if (!string.IsNullOrWhiteSpace(diet.notes))
                        col.Item().PaddingLeft(18).Text(diet.notes).FontSize(8).FontColor("#666666");
                }
            }

            if (dietsAssigned.Count > 0)
            {
                col.Item().PaddingTop(5).Text("Dietas asignadas al paciente").Bold();
                foreach (var assignment in dietsAssigned)
                    col.Item().PaddingLeft(8).Text($"• {assignment.diet?.name ?? $"Dieta #{assignment.diet_id}"} · inicio {assignment.start_date:dd/MM/yyyy}");
            }

            if (activeDiet?.diet != null)
            {
                col.Item().PaddingTop(5).Text("Plan activo en la fecha").Bold();
                col.Item().PaddingLeft(8).Text(
                    $"{activeDiet.diet.name} · {Format(activeDiet.diet.target_kcal, "kcal")} · " +
                    $"P {Format(activeDiet.diet.target_protein, "g")} · C {Format(activeDiet.diet.target_carbs, "g")} · G {Format(activeDiet.diet.target_fat, "g")}");
            }
        });
    }

    private static void AddMetric(TableDescriptor table, string label, double? value, string unit)
    {
        table.Cell().Border(1).BorderColor("#e2e5ea").Padding(5).Text(text =>
        {
            text.Span(label + "").Bold();
            text.Span(Format(value, unit));
        });
    }

    private static void AddDelta(TextDescriptor text, string label, double? current, double? previous, string unit)
    {
        if (current.HasValue && previous.HasValue)
        {
            var delta = current.Value - previous.Value;
            var sign = delta > 0 ? "+" : "";
            text.Span($"{label} {sign}{delta:0.0}{unit}  ");
        }
    }

    private static double? CalculateBmi(biometrics b)
    {
        if (!b.weight.HasValue || !b.height.HasValue || b.height.Value <= 0) return null;
        return b.weight.Value / Math.Pow(b.height.Value / 100.0, 2);
    }

    private static string Format(double? value, string unit)
        => value.HasValue ? $"{value.Value:0.0}{(string.IsNullOrWhiteSpace(unit) ? "" : " " + unit)}" : "—";

    private static string Format(decimal? value, string unit)
        => value.HasValue ? $"{value.Value:0.0}{(string.IsNullOrWhiteSpace(unit) ? "" : " " + unit)}" : "—";
}
