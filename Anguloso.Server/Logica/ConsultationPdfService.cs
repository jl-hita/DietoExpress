using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Anguloso.Server.Logica;

public class ConsultationPdfService
{
    public byte[] GenerateConsultationPdf(clients client, DateOnly consultationDate, angulosodbContext context)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var nutritionist = context.users
            .AsNoTracking()
            .FirstOrDefault(u => u.id == client.user_id);

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

        var dietsCreated = context.diets
            .AsNoTracking()
            .Where(d => d.archived_at == null &&
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
                         cd.start_date == consultationDate)
            .OrderBy(cd => cd.assigned_at)
            .ToList();

        var activeDiet = context.client_diets
            .AsNoTracking()
            .Include(cd => cd.diet)
            .Where(cd => cd.client_id == client.id &&
                         cd.is_active == true &&
                         cd.start_date <= consultationDate &&
                         (!cd.end_date.HasValue || cd.end_date.Value >= consultationDate))
            .OrderByDescending(cd => cd.start_date)
            .FirstOrDefault();

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
                            .Bold().FontSize(16).FontColor("#3f51b5");

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
        });

        return document.GeneratePdf();
    }

    private void ComposeHeader(IContainer container, users nutritionist)
    {
        container.Column(header =>
        {
            header.Item().Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(nutritionist?.clinic_name ?? "DietoExpress")
                        .Bold().FontSize(15).FontColor("#3f51b5");
                    if (!string.IsNullOrWhiteSpace(nutritionist?.clinic_address))
                        col.Item().Text(nutritionist.clinic_address).FontSize(8);
                    if (!string.IsNullOrWhiteSpace(nutritionist?.clinic_phone))
                        col.Item().Text(nutritionist.clinic_phone).FontSize(8);
                });

                row.ConstantItem(150).AlignRight().Column(col =>
                {
                    col.Item().Text("INFORME CLÍNICO").Bold().FontSize(11).FontColor("#3f51b5");
                    if (!string.IsNullOrWhiteSpace(nutritionist?.full_name))
                        col.Item().Text(nutritionist.full_name).FontSize(8);
                });
            });

            header.Item().BorderBottom(2).BorderColor("#3f51b5");
        });
    }

    private void ComposePatientSummary(IContainer container, clients client, users nutritionist)
    {
        container.Border(1).BorderColor("#d9dce3").Padding(10).Column(col =>
        {
            col.Item().Text("DATOS DE LA CONSULTA").Bold().FontSize(11).FontColor("#3f51b5");
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
            col.Item().Text("MEDICIONES DEL DÍA").Bold().FontSize(11).FontColor("#3f51b5");

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
            col.Item().Text("EVOLUCIÓN HISTÓRICA").Bold().FontSize(11).FontColor("#3f51b5");

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
                    header.Cell().Background("#eef1f8").Padding(4).Text("Fecha").Bold();
                    header.Cell().Background("#eef1f8").Padding(4).Text("Peso").Bold();
                    header.Cell().Background("#eef1f8").Padding(4).Text("% grasa").Bold();
                    header.Cell().Background("#eef1f8").Padding(4).Text("M. muscular").Bold();
                    header.Cell().Background("#eef1f8").Padding(4).Text("Cintura").Bold();
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
            col.Item().Text("ACTIVIDAD DIETÉTICA DEL DÍA").Bold().FontSize(11).FontColor("#3f51b5");

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
