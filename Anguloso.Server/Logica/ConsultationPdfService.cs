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
    public byte[] GenerateConsultationPdf(clients client, DateOnly consultationDate, angulosodbContext context)
    {
        QuestPDF.Settings.License = LicenseType.Community;

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

        var trainingPlans = LoadTrainingPlans(client, consultationDate, context);

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
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9).FontColor("#2d2d2d"));

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
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9).FontColor("#2d2d2d"));
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


    private List<TrainingPlanPdf> LoadTrainingPlans(clients client, DateOnly date, angulosodbContext context)
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
                  AND (@tenantId IS NULL OR c.tenant_id = @tenantId)
                  AND p.start_date <= @date
                  AND (p.end_date IS NULL OR p.end_date >= @date OR p.status IN ('draft', 'active'))
                ORDER BY p.start_date DESC, p.id DESC
                """;
            var clientParam = command.CreateParameter();
            clientParam.ParameterName = "clientId"; clientParam.Value = client.id; command.Parameters.Add(clientParam);
            var tenantParam = command.CreateParameter();
            tenantParam.ParameterName = "tenantId"; tenantParam.Value = client.tenant_id.HasValue ? client.tenant_id.Value : DBNull.Value; command.Parameters.Add(tenantParam);
            var dateParam = command.CreateParameter();
            dateParam.ParameterName = "date"; dateParam.Value = date.ToDateTime(TimeOnly.MinValue); command.Parameters.Add(dateParam);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    plans.Add(new TrainingPlanPdf
                    {
                        Id = Convert.ToInt32(reader.GetValue(0)),
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
                    FROM training_mesocycles WHERE plan_id = @planId ORDER BY sequence_number, id
                    """;
                var planParam = mesoCommand.CreateParameter(); planParam.ParameterName = "planId"; planParam.Value = plan.Id; mesoCommand.Parameters.Add(planParam);
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
                using var sessionsCommand = connection.CreateCommand();
                sessionsCommand.CommandText = """
                    SELECT scheduled_date, title, session_type, duration_minutes, intensity_target, instructions,
                           exercise_prescription, exercise_prescription_data::text
                    FROM training_sessions WHERE plan_id = @planId ORDER BY scheduled_date, id
                    """;
                var planParam = sessionsCommand.CreateParameter(); planParam.ParameterName = "planId"; planParam.Value = plan.Id; sessionsCommand.Parameters.Add(planParam);
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

    private void ComposeTrainingPlan(IContainer container, TrainingPlanPdf plan)
    {
        container.Column(col =>
        {
            col.Spacing(10);
            col.Item().Background("#0d9488").Padding(10).Text("PLANIFICACIÓN DEPORTIVA").Bold().FontSize(16).FontColor("#ffffff");
            col.Item().Text(plan.Name).Bold().FontSize(13);
            col.Item().Text($"Modelo: {plan.Model} · Estado: {plan.Status}").FontColor("#555555");
            col.Item().Text($"Periodo: {plan.StartDate:dd/MM/yyyy} — {(plan.EndDate?.ToString("dd/MM/yyyy") ?? "Sin fecha final")}").FontColor("#555555");
            if (!string.IsNullOrWhiteSpace(plan.Notes))
                col.Item().Text($"Notas del plan: {plan.Notes}");
            col.Item().PaddingTop(4).Background("#e6fffb").Padding(7).Text("MACROCICLO · FASES Y OBJETIVOS").Bold().FontSize(11).FontColor("#0f766e");
            if (plan.Mesocycles.Count == 0)
                col.Item().Text("No hay mesociclos definidos todavía.").Italic().FontColor("#777777");
            foreach (var meso in plan.Mesocycles)
            {
                col.Item().Border(1).BorderColor("#d9dce3").Padding(8).Column(phase =>
                {
                    phase.Item().Text($"{meso.Sequence}. {meso.Name}").Bold().FontSize(10);
                    phase.Item().Text($"{meso.Start:dd/MM/yyyy} — {(meso.End?.ToString("dd/MM/yyyy") ?? "Sin fecha final")}");
                    if (!string.IsNullOrWhiteSpace(meso.Objective)) phase.Item().Text($"Objetivo: {meso.Objective}");
                    if (!string.IsNullOrWhiteSpace(meso.Notes)) phase.Item().Text($"Notas: {meso.Notes}").FontSize(8).FontColor("#555555");
                });
            }
            col.Item().PaddingTop(5).Background("#e6fffb").Padding(7).Text("RUTINAS Y SESIONES").Bold().FontSize(11).FontColor("#0f766e");
            if (plan.Sessions.Count == 0)
                col.Item().Text("No hay sesiones de entrenamiento registradas en este plan.").Italic().FontColor("#777777");
            foreach (var session in plan.Sessions)
            {
                col.Item().BorderBottom(1).BorderColor("#e2e5ea").PaddingVertical(6).Column(item =>
                {
                    item.Item().Text($"{session.Date:dddd dd/MM/yyyy} · {session.Title}").Bold().FontSize(10);
                    item.Item().Text($"{session.Type} · {(session.Duration.HasValue ? $"{session.Duration.Value} min" : "Duración no indicada")}" +
                        (string.IsNullOrWhiteSpace(session.Intensity) ? "" : $" · Intensidad: {session.Intensity}")).FontColor("#555555");
                    if (!string.IsNullOrWhiteSpace(session.Instructions)) item.Item().Text(session.Instructions);
                    if (!string.IsNullOrWhiteSpace(session.StructuredPrescription))
                    {
                        try
                        {
                            using var json = System.Text.Json.JsonDocument.Parse(session.StructuredPrescription);
                            if (json.RootElement.TryGetProperty("exercises", out var exercises) && exercises.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                item.Item().PaddingTop(3).Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(4); columns.RelativeColumn(2); columns.RelativeColumn(2); columns.RelativeColumn(2);
                                    });
                                    table.Header(header =>
                                    {
                                        header.Cell().Background("#f0fdfa").Padding(3).Text("Ejercicio").Bold();
                                        header.Cell().Background("#f0fdfa").Padding(3).Text("Series").Bold();
                                        header.Cell().Background("#f0fdfa").Padding(3).Text("Repeticiones").Bold();
                                        header.Cell().Background("#f0fdfa").Padding(3).Text("Descanso").Bold();
                                    });
                                    foreach (var exercise in exercises.EnumerateArray())
                                    {
                                        string Get(string name) => exercise.TryGetProperty(name, out var value) ? value.ToString() : "—";
                                        var name = Get("exerciseNameSnapshot");
                                        if (name == "—") name = Get("ExerciseNameSnapshot");
                                        var sets = Get("sets"); if (sets == "—") sets = Get("Sets");
                                        var min = Get("minRepetitions"); if (min == "—") min = Get("MinRepetitions");
                                        var max = Get("maxRepetitions"); if (max == "—") max = Get("MaxRepetitions");
                                        var rest = Get("restSeconds"); if (rest == "—") rest = Get("RestSeconds");
                                        item.Cell().Padding(3).Text(name);
                                        item.Cell().Padding(3).Text(sets);
                                        item.Cell().Padding(3).Text(min == max ? min : $"{min}–{max}");
                                        item.Cell().Padding(3).Text(rest == "—" ? rest : $"{rest}s");
                                    }
                                });
                            }
                            else if (!string.IsNullOrWhiteSpace(session.Prescription))
                                item.Item().Text(session.Prescription).FontSize(8);
                        }
                        catch (System.Text.Json.JsonException)
                        {
                            if (!string.IsNullOrWhiteSpace(session.Prescription)) item.Item().Text(session.Prescription).FontSize(8);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(session.Prescription))
                        item.Item().Text(session.Prescription).FontSize(8);
                });
            }
        });
    }

    private sealed class TrainingPlanPdf
    {
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
