using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Collections.Generic;

namespace Anguloso.Server.Logica;

public class DietPdfService
{
    public byte[] GenerateDietPdf(clients client, diets diet, client_diets assignment, angulosodbContext context)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // Fetch nutritionist profile
        var nutritionist = context.users
            .AsNoTracking()
            .FirstOrDefault(u => u.id == client.user_id);

        // Fetch exchange foods if any
        var exchangeFoods = context.foods
            .Include(f => f.exchange_group)
            .Where(f => f.exchange_group_id != null && f.grams_per_exchange.HasValue)
            .OrderBy(f => f.exchange_group_id)
            .ThenBy(f => f.name)
            .ToList();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10).FontColor("#2d2d2d"));

                // ─── HEADER ───────────────────────────────────────────────────
                page.Header().Element(c => ComposeHeader(c, nutritionist));

                // ─── CONTENT ──────────────────────────────────────────────────
                page.Content().Element(content =>
                {
                    content.Column(col =>
                    {
                        col.Spacing(16);

                        // Bloque de datos del paciente y dieta
                        col.Item().Element(c => ComposePatientInfo(c, client, diet, assignment));

                        // Separador
                        col.Item().BorderBottom(1).BorderColor("#3f51b5").PaddingBottom(4).Text("PLAN ALIMENTARIO SEMANAL")
                            .Bold().FontSize(13).FontColor("#3f51b5");

                        // DÃ­as de la dieta
                        if (diet.diet_days != null)
                        {
                            foreach (var day in diet.diet_days.OrderBy(d => d.day_index))
                            {
                                col.Item().Element(c => ComposeDay(c, day));
                            }
                        }

                        // Resumen de macros de la dieta
                        col.Item().Element(c => ComposeMacroSummary(c, diet));

                        // Tabla de equivalencias (nueva pÃ¡gina)
                        if (exchangeFoods.Any())
                        {
                            col.Item().PageBreak();
                            col.Item().Element(c => ComposeEquivalenceTable(c, exchangeFoods));
                        }
                    });
                });

                // â”€â”€â”€ FOOTER â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Documento generado el ").FontColor("#888888").FontSize(8);
                    text.Span(DateTime.Now.ToString("dd/MM/yyyy")).FontColor("#888888").FontSize(8);
                    text.Span("  |  PÃ¡gina ").FontColor("#888888").FontSize(8);
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
        container.Row(row =>
        {
            // Left part: Logo + Clinic Details
            row.RelativeItem().Row(leftRow =>
            {
                leftRow.Spacing(10);
                
                // 1. Logo
                if (nutritionist != null && !string.IsNullOrWhiteSpace(nutritionist.clinic_logo))
                {
                    try
                    {
                        var logoBase64 = nutritionist.clinic_logo;
                        if (logoBase64.Contains(","))
                        {
                            logoBase64 = logoBase64.Split(',')[1];
                        }
                        byte[] logoBytes = System.Convert.FromBase64String(logoBase64);
                        leftRow.ConstantItem(50).Height(50).Image(logoBytes);
                    }
                    catch
                    {
                        // Ignore logo parsing errors
                    }
                }
                
                // 2. Clinic Details
                leftRow.RelativeItem().Column(col =>
                {
                    string clinicName = !string.IsNullOrWhiteSpace(nutritionist?.clinic_name) 
                        ? nutritionist.clinic_name 
                        : "DietoExpress";
                        
                    string clinicSub = !string.IsNullOrWhiteSpace(nutritionist?.full_name) 
                        ? $"Consulta de {nutritionist.full_name}" 
                        : "Consulta de Nutrición Profesional";
                        
                    col.Item().Text(clinicName).Bold().FontSize(18).FontColor("#3f51b5");
                    col.Item().Text(clinicSub).FontSize(10).FontColor("#666666");
                    
                    if (nutritionist != null)
                    {
                        var details = new System.Collections.Generic.List<string>();
                        if (!string.IsNullOrWhiteSpace(nutritionist.clinic_address))
                            details.Add(nutritionist.clinic_address);
                        if (!string.IsNullOrWhiteSpace(nutritionist.clinic_phone))
                            details.Add($"Tlf: {nutritionist.clinic_phone}");
                            
                        if (details.Count > 0)
                        {
                            col.Item().Text(string.Join(" | ", details)).FontSize(8).FontColor("#888888");
                        }
                    }
                });
            });

            // Right part: PLAN NUTRICIONAL badge
            row.ConstantItem(140).AlignRight().Column(col =>
            {
                col.Item().Background("#3f51b5").Padding(8).AlignCenter().Text("PLAN NUTRICIONAL")
                    .Bold().FontSize(12).FontColor(Colors.White);
            });
        });

        // Línea divisoria
        container.BorderBottom(2).BorderColor("#3f51b5");
    }

    private void ComposePatientInfo(IContainer container, clients client, diets diet, client_diets assignment)
    {
        container.Background("#f5f7ff").Padding(12).Column(col =>
        {
            col.Spacing(4);
            col.Item().Text("DATOS DEL PACIENTE").Bold().FontSize(10).FontColor("#3f51b5");

            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text($"Nombre: {client.full_name}").SemiBold();
                    if (client.birth_date.HasValue)
                        c.Item().Text($"Fecha de nacimiento: {client.birth_date.Value:dd/MM/yyyy}");
                    if (!string.IsNullOrWhiteSpace(client.gender))
                        c.Item().Text($"Sexo: {client.gender}");
                });

                row.RelativeItem().Column(c =>
                {
                    c.Item().Text($"Dieta: {diet.name}").SemiBold();
                    c.Item().Text($"Inicio: {assignment.start_date:dd/MM/yyyy}");
                    if (assignment.end_date.HasValue)
                        c.Item().Text($"Fin previsto: {assignment.end_date.Value:dd/MM/yyyy}");
                    if (!string.IsNullOrWhiteSpace(assignment.notes))
                        c.Item().Text($"Notas: {assignment.notes}");
                });
            });
        });
    }

    private void ComposeDay(IContainer container, diet_days day)
    {
        var dayNames = new[] { "Lunes", "Martes", "MiÃ©rcoles", "Jueves", "Viernes", "SÃ¡bado", "Domingo" };
        var dayName = day.day_index >= 0 && day.day_index < dayNames.Length
            ? dayNames[day.day_index]
            : $"DÃ­a {day.day_index + 1}";

        container.Column(col =>
        {
            col.Spacing(6);

            // TÃ­tulo del dÃ­a
            col.Item().Background("#3f51b5").Padding(6).Text(dayName.ToUpper())
                .Bold().FontSize(11).FontColor(Colors.White);

            // Comidas del dÃ­a
            if (day.meals != null)
            {
                foreach (var meal in day.meals.OrderBy(m => m.meal_index))
                {
                    col.Item().Element(c => ComposeMeal(c, meal));
                }
            }
        });
    }

    private void ComposeMeal(IContainer container, meals meal)
    {
        var items = meal.meal_items?.ToList() ?? new List<meal_items>();
        var totalKcal = items.Sum(i => (double)(i.kcal ?? 0));
        var totalProtein = items.Sum(i => (double)(i.protein ?? 0));
        var totalCarbs = items.Sum(i => (double)(i.carbs ?? 0));
        var totalFat = items.Sum(i => (double)(i.fat ?? 0));

        container.Column(col =>
        {
            col.Spacing(3);

            // Nombre de la comida
            col.Item().BorderLeft(3).BorderColor("#7986cb").PaddingLeft(8)
                .Text(meal.name).SemiBold().FontSize(10).FontColor("#3f51b5");

            if (items.Any())
            {
                // Tabla de alimentos
                col.Item().PaddingLeft(12).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(4); // Alimento
                        cols.RelativeColumn(1); // Gramos
                        cols.RelativeColumn(1); // Kcal
                        cols.RelativeColumn(1); // Prot.
                        cols.RelativeColumn(1); // Carb.
                        cols.RelativeColumn(1); // Grasas
                    });

                    // Cabecera de la tabla
                    table.Header(header =>
                    {
                        header.Cell().Background("#e8eaf6").Padding(3).Text("Alimento").Bold().FontSize(8).FontColor("#3f51b5");
                        header.Cell().Background("#e8eaf6").Padding(3).AlignRight().Text("Gramos").Bold().FontSize(8).FontColor("#3f51b5");
                        header.Cell().Background("#e8eaf6").Padding(3).AlignRight().Text("Kcal").Bold().FontSize(8).FontColor("#3f51b5");
                        header.Cell().Background("#e8eaf6").Padding(3).AlignRight().Text("Prot.").Bold().FontSize(8).FontColor("#3f51b5");
                        header.Cell().Background("#e8eaf6").Padding(3).AlignRight().Text("Carb.").Bold().FontSize(8).FontColor("#3f51b5");
                        header.Cell().Background("#e8eaf6").Padding(3).AlignRight().Text("Grasas").Bold().FontSize(8).FontColor("#3f51b5");
                    });

                    // Filas de alimentos
                    foreach (var item in items)
                    {
                        var foodName = item.food?.name ?? (item.exchange_group != null ? $"[Intercambio] {item.exchange_group.name}" : $"Alimento #{item.food_id}");
                        table.Cell().Padding(3).Text(foodName).FontSize(8);
                        
                        var gramsText = item.grams.HasValue ? $"{item.grams.Value:F0} g" : (item.exchange_count.HasValue ? $"{item.exchange_count.Value:F1} int." : "-");
                        table.Cell().Padding(3).AlignRight().Text(gramsText).FontSize(8);
                        
                        table.Cell().Padding(3).AlignRight().Text(item.kcal.HasValue ? $"{item.kcal:F0}" : "-").FontSize(8);
                        table.Cell().Padding(3).AlignRight().Text(item.protein.HasValue ? $"{item.protein:F1}" : "-").FontSize(8);
                        table.Cell().Padding(3).AlignRight().Text(item.carbs.HasValue ? $"{item.carbs:F1}" : "-").FontSize(8);
                        table.Cell().Padding(3).AlignRight().Text(item.fat.HasValue ? $"{item.fat:F1}" : "-").FontSize(8);
                    }

                    // Fila de totales
                    table.Cell().BorderTop(1).BorderColor("#c5cae9").Padding(3).Text("TOTAL").Bold().FontSize(8).FontColor("#3f51b5");
                    table.Cell().BorderTop(1).BorderColor("#c5cae9").Padding(3).AlignRight().Text("").FontSize(8);
                    table.Cell().BorderTop(1).BorderColor("#c5cae9").Padding(3).AlignRight().Text($"{totalKcal:F0}").Bold().FontSize(8).FontColor("#3f51b5");
                    table.Cell().BorderTop(1).BorderColor("#c5cae9").Padding(3).AlignRight().Text($"{totalProtein:F1}").Bold().FontSize(8).FontColor("#3f51b5");
                    table.Cell().BorderTop(1).BorderColor("#c5cae9").Padding(3).AlignRight().Text($"{totalCarbs:F1}").Bold().FontSize(8).FontColor("#3f51b5");
                    table.Cell().BorderTop(1).BorderColor("#c5cae9").Padding(3).AlignRight().Text($"{totalFat:F1}").Bold().FontSize(8).FontColor("#3f51b5");
                });
            }
            else
            {
                col.Item().PaddingLeft(12).Text("Sin alimentos definidos.").Italic().FontColor("#aaaaaa").FontSize(8);
            }
        });
    }

    private void ComposeMacroSummary(IContainer container, diets diet)
    {
        if (!diet.target_kcal.HasValue && !diet.target_protein.HasValue &&
            !diet.target_carbs.HasValue && !diet.target_fat.HasValue)
            return;

        container.Column(col =>
        {
            col.Spacing(6);

            col.Item().BorderBottom(1).BorderColor("#3f51b5").PaddingBottom(4)
                .Text("OBJETIVOS NUTRICIONALES DIARIOS").Bold().FontSize(11).FontColor("#3f51b5");

            col.Item().Row(row =>
            {
                row.Spacing(8);
                MacroCard(row.RelativeItem(), "CalorÃ­as", $"{diet.target_kcal:F0}", "kcal", "#e53935");
                MacroCard(row.RelativeItem(), "ProteÃ­nas", $"{diet.target_protein:F1}", "g", "#43a047");
                MacroCard(row.RelativeItem(), "Carbohidratos", $"{diet.target_carbs:F1}", "g", "#fb8c00");
                MacroCard(row.RelativeItem(), "Grasas", $"{diet.target_fat:F1}", "g", "#1e88e5");
            });

            if (!string.IsNullOrWhiteSpace(diet.notes))
            {
                col.Item().Background("#fff8e1").Padding(10).Column(c =>
                {
                    c.Item().Text("Observaciones del dietista:").SemiBold().FontSize(9).FontColor("#f57f17");
                    c.Item().Text(diet.notes).FontSize(9).FontColor("#555555");
                });
            }
        });
    }

    private void MacroCard(IContainer container, string label, string value, string unit, string color)
    {
        container.Background(color).CornerRadius(4).Padding(10).Column(col =>
        {
            col.Item().AlignCenter().Text(label).Bold().FontSize(9).FontColor(Colors.White);
            col.Item().AlignCenter().Text(value).Bold().FontSize(20).FontColor(Colors.White);
            col.Item().AlignCenter().Text(unit).FontSize(9).FontColor(Colors.White);
        });
    }

    private void ComposeEquivalenceTable(IContainer container, List<foods> exchangeFoods)
    {
        container.Column(col =>
        {
            col.Spacing(10);
            col.Item().Text("TABLA DE EQUIVALENCIAS DE INTERCAMBIOS").Bold().FontSize(14).FontColor("#3f51b5");
            col.Item().Text("Use esta tabla para cambiar un alimento por otro equivalente de su mismo grupo. Las cantidades mostradas corresponden a 1 INTERCAMBIO.").FontSize(9).Italic().FontColor("#555555");

            var groupedFoods = exchangeFoods.GroupBy(f => f.exchange_group).ToList();
            
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3); // Grupo
                    cols.RelativeColumn(3); // Alimento
                    cols.RelativeColumn(2); // Cantidad (1 int.)
                    cols.RelativeColumn(4); // Macros (1 int.)
                });

                table.Header(header =>
                {
                    header.Cell().Background("#e8eaf6").Padding(4).Text("Grupo").Bold().FontSize(9).FontColor("#3f51b5");
                    header.Cell().Background("#e8eaf6").Padding(4).Text("Alimento").Bold().FontSize(9).FontColor("#3f51b5");
                    header.Cell().Background("#e8eaf6").Padding(4).AlignRight().Text("Cantidad").Bold().FontSize(9).FontColor("#3f51b5");
                    header.Cell().Background("#e8eaf6").Padding(4).Text("Macros (Carb / Prot / Gras / Kcal)").Bold().FontSize(9).FontColor("#3f51b5");
                });

                foreach (var group in groupedFoods)
                {
                    if (group.Key == null) continue;
                    var groupMacros = $"{group.Key.carbs:F0}g HC / {group.Key.protein:F0}g P / {group.Key.fat:F0}g G ({group.Key.kcal:F0} kcal)";
                    
                    bool isFirst = true;
                    foreach (var food in group)
                    {
                        table.Cell().Padding(3).BorderBottom(0.5f).BorderColor("#dddddd").Text(isFirst ? group.Key.name : "").Bold().FontSize(8);
                        table.Cell().Padding(3).BorderBottom(0.5f).BorderColor("#dddddd").Text(food.name).FontSize(8);
                        table.Cell().Padding(3).BorderBottom(0.5f).BorderColor("#dddddd").AlignRight().Text($"{food.grams_per_exchange:F0} g").FontSize(8);
                        table.Cell().Padding(3).BorderBottom(0.5f).BorderColor("#dddddd").Text(isFirst ? groupMacros : "").FontSize(8).FontColor("#666666");
                        isFirst = false;
                    }
                }
            });
        });
    }
}
