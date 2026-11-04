using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Anguloso.Server.Models;
using Anguloso.Server.Model;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Collections.Generic;

namespace Anguloso.Server.Logica;

// Documentación: este componente encapsula lógica compartida para mantener las reglas y transformaciones fuera de los puntos de entrada del frontend.
public class DietPdfService
{
    public byte[] GenerateDietPdf(clients client, diets diet, client_diets assignment, angulosodbContext context, int userId, bool canUseTenantLocalFoods)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // Fetch nutritionist profile
        var nutritionist = context.users
            .AsNoTracking()
            .FirstOrDefault(u => u.id == client.user_id);

        // Fetch exchange foods if any
        // Los alimentos de intercambio globales son visibles siempre; los locales solo se incluyen si pertenecen al tenant y el usuario tiene permiso o es su creador.
        var exchangeFoods = context.foods
            .Include(f => f.exchange_group)
            .Where(f => f.exchange_group_id != null && f.grams_per_exchange.HasValue &&
                        ((f.source == null || f.source.ToLower() != "local") ||
                         (client.tenant_id.HasValue && f.tenant_id == client.tenant_id.Value &&
                          (canUseTenantLocalFoods || f.created_by_user_id == userId))))
            .OrderBy(f => f.exchange_group_id)
            .ThenBy(f => f.name)
            .ToList();

        // QuestPDF compone el documento por bloques para mantener separadas cabecera, contenido, equivalencias y lista de compra; así cada sección puede evolucionar sin alterar las demás.
        var document = Document.Create(container =>
        {
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

            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontFamily("Lato").FontSize(10).FontColor("#2d2d2d"));

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
                        col.Item().BorderBottom(1).BorderColor("#0d9488").PaddingBottom(4).Text("PLAN ALIMENTARIO SEMANAL")
                            .Bold().FontSize(13).FontColor("#0d9488");

                        // Días de la dieta
                        if (diet.diet_days != null)
                        {
                            var orderedDays = diet.diet_days.OrderBy(d => d.day_index).ToList();
                            foreach (var day in orderedDays)
                            {
                                // Mantiene cada dieta diaria completa en una misma página cuando cabe; si no cabe, QuestPDF puede continuarla en la siguiente.
                                col.Item().PreventPageBreak().Element(c => ComposeDay(c, day));

                                // El salto se coloca al terminar el día, no antes: el primero aprovecha el espacio disponible y los siguientes empiezan en página nueva.
                                col.Item().PageBreak();
                            }
                        }

                        // Resumen de macros de la dieta
                        col.Item().Element(c => ComposeMacroSummary(c, diet));

                        // Tabla de equivalencias (nueva página)
                        if (exchangeFoods.Any())
                        {
                            col.Item().PageBreak();
                            col.Item().Element(c => ComposeEquivalenceTable(c, exchangeFoods));
                        }

                        // Lista de la compra consolidada (nueva página)
                        col.Item().PageBreak();
                        col.Item().Element(c => ComposeShoppingList(c, diet, context, userId, canUseTenantLocalFoods));

                        // El resumen nutricional completo queda al final del PDF, después de la lista de la compra.
                        col.Item().PageBreak();
                        col.Item().Element(c => ComposeWeeklyNutritionSummary(c, diet, context, userId, canUseTenantLocalFoods, exchangeFoods));
                    });
                });

                // â”€â”€â”€ FOOTER â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Documento generado el ").FontColor("#888888").FontSize(8);
                    text.Span(DateTime.Now.ToString("dd/MM/yyyy")).FontColor("#888888").FontSize(8);
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
                        
                    col.Item().Text(clinicName).Bold().FontSize(18).FontColor("#0d9488");
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
                col.Item().Background("#0d9488").Padding(8).AlignCenter().Text("PLAN NUTRICIONAL")
                    .Bold().FontSize(12).FontColor(Colors.White);
            });
            });

            // Línea divisoria
            header.Item().BorderBottom(2).BorderColor("#0d9488");
        });
    }

    private void ComposePatientInfo(IContainer container, clients client, diets diet, client_diets assignment)
    {
        if (string.Equals(client.full_name, "Plan sin asignar", StringComparison.Ordinal))
        {
            container.Background("#f5f7ff").Padding(12).Column(col =>
            {
                col.Spacing(4);
                col.Item().Text("DATOS DEL PLAN").Bold().FontSize(10).FontColor("#0d9488");
                col.Item().Text($"Nombre: {diet.name}").SemiBold();
                if (!string.IsNullOrWhiteSpace(diet.notes))
                    col.Item().Text($"Observaciones: {diet.notes}");
            });
            return;
        }

        container.Background("#f5f7ff").Padding(12).Column(col =>
        {
            col.Spacing(4);
            col.Item().Text("DATOS DEL PACIENTE").Bold().FontSize(10).FontColor("#0d9488");

            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text($"Nombre: {client.full_name}").SemiBold();
                    if (client.birth_date.HasValue)
                        c.Item().Text($"Fecha de nacimiento: {client.birth_date.Value:dd/MM/yyyy}");
                    //if (!string.IsNullOrWhiteSpace(client.gender))
                    //    c.Item().Text($"Sexo: {client.gender}");
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
        var dayNames = new[] { "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo" };
        var dayName = day.day_index >= 0 && day.day_index < dayNames.Length
            ? dayNames[day.day_index]
            : $"Día {day.day_index + 1}";

        container.Column(col =>
        {
            col.Spacing(6);

            // Título del día
            col.Item().Background("#0d9488").Padding(6).Text(dayName.ToUpper())
                .Bold().FontSize(11).FontColor(Colors.White);

            // Comidas del día
            if (day.meals != null)
            {
                foreach (var meal in day.meals.OrderBy(m => m.meal_index))
                {
                    // Evita que el título, los alimentos y los totales de una comida queden partidos entre páginas.
                    col.Item().PreventPageBreak().Element(c => ComposeMeal(c, meal));
                }

                // Resumen diario de % CDR de Micronutrientes
                col.Item().Element(c => ComposeDayMicronutrients(c, day.meals));
            }
        });
    }

    private void ComposeDayMicronutrients(IContainer container, IEnumerable<meals> dayMeals)
    {
        var items = dayMeals.SelectMany(m => m.meal_items ?? Enumerable.Empty<meal_items>()).ToList();
        if (!items.Any(i => i.food != null)) return;

        double ca = 0, fe = 0, mg = 0, k = 0, zn = 0;
        double vitA = 0, vitC = 0, vitD = 0, vitE = 0, vitB12 = 0, folate = 0, fiber = 0;

        foreach (var item in items)
        {
            if (item.food == null || (item.grams ?? 0) <= 0) continue;
            var ratio = (double)item.grams!.Value / 100.0;
            ca += (item.food.calcium_mg ?? 0) * ratio;
            fe += (item.food.iron_mg ?? 0) * ratio;
            mg += (item.food.magnesium_mg ?? 0) * ratio;
            k += (item.food.potassium_mg ?? 0) * ratio;
            zn += (item.food.zinc_mg ?? 0) * ratio;
            vitA += (item.food.vitamin_a_ug ?? 0) * ratio;
            vitC += (item.food.vitamin_c_mg ?? 0) * ratio;
            vitD += (item.food.vitamin_d_ug ?? 0) * ratio;
            vitE += (item.food.vitamin_e_mg ?? 0) * ratio;
            vitB12 += (item.food.vitamin_b12_ug ?? 0) * ratio;
            folate += (item.food.folate_ug ?? 0) * ratio;
            fiber += (item.food.fiber ?? 0) * ratio;
        }

        const double rdaCa = 950.0;     // mg
        const double rdaFe = 14.0;      // mg
        const double rdaMg = 350.0;     // mg
        const double rdaK = 3500.0;     // mg
        const double rdaZn = 10.0;      // mg
        const double rdaVitA = 800.0;   // ug
        const double rdaVitC = 80.0;    // mg
        const double rdaVitD = 15.0;    // ug
        const double rdaVitE = 12.0;    // mg
        const double rdaVitB12 = 2.5;   // ug
        const double rdaFolate = 330.0; // ug
        const double rdaFiber = 30.0;   // g

        container.Background("#f8fafc").Border(1).BorderColor("#e2e8f0").CornerRadius(4).Padding(6).Column(col =>
        {
            col.Item().Text("RESUMEN DE MICRONUTRIENTES Y % CDR DEL DÍA").Bold().FontSize(8).FontColor("#475569");
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    for (int i = 0; i < 6; i++) cols.RelativeColumn(1);
                });

                table.Cell().Text($"Calcio: {ca:F0}mg ({Math.Round(ca/rdaCa*100):0}% CDR)").FontSize(6);
                table.Cell().Text($"Hierro: {fe:F1}mg ({Math.Round(fe/rdaFe*100):0}% CDR)").FontSize(6);
                table.Cell().Text($"Magnesio: {mg:F0}mg ({Math.Round(mg/rdaMg*100):0}% CDR)").FontSize(6);
                table.Cell().Text($"Potasio: {k:F0}mg ({Math.Round(k/rdaK*100):0}% CDR)").FontSize(6);
                table.Cell().Text($"Zinc: {zn:F1}mg ({Math.Round(zn/rdaZn*100):0}% CDR)").FontSize(6);
                table.Cell().Text($"Fibra: {fiber:F1}g ({Math.Round(fiber/rdaFiber*100):0}% CDR)").FontSize(6);

                table.Cell().Text($"Vit A: {vitA:F0}µg ({Math.Round(vitA/rdaVitA*100):0}% CDR)").FontSize(6);
                table.Cell().Text($"Vit C: {vitC:F0}mg ({Math.Round(vitC/rdaVitC*100):0}% CDR)").FontSize(6);
                table.Cell().Text($"Vit D: {vitD:F1}µg ({Math.Round(vitD/rdaVitD*100):0}% CDR)").FontSize(6);
                table.Cell().Text($"Vit E: {vitE:F1}mg ({Math.Round(vitE/rdaVitE*100):0}% CDR)").FontSize(6);
                table.Cell().Text($"Vit B12: {vitB12:F1}µg ({Math.Round(vitB12/rdaVitB12*100):0}% CDR)").FontSize(6);
                table.Cell().Text($"Folatos: {folate:F0}µg ({Math.Round(folate/rdaFolate*100):0}% CDR)").FontSize(6);
            });
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
                .Text(meal.name).SemiBold().FontSize(10).FontColor("#0d9488");

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
                        header.Cell().Background("#f0fdfa").Padding(3).Text("Alimento").Bold().FontSize(8).FontColor("#0d9488");
                        header.Cell().Background("#f0fdfa").Padding(3).AlignRight().Text("Gramos").Bold().FontSize(8).FontColor("#0d9488");
                        header.Cell().Background("#f0fdfa").Padding(3).AlignRight().Text("Kcal").Bold().FontSize(8).FontColor("#0d9488");
                        header.Cell().Background("#f0fdfa").Padding(3).AlignRight().Text("Prot.").Bold().FontSize(8).FontColor("#0d9488");
                        header.Cell().Background("#f0fdfa").Padding(3).AlignRight().Text("Carb.").Bold().FontSize(8).FontColor("#0d9488");
                        header.Cell().Background("#f0fdfa").Padding(3).AlignRight().Text("Grasas").Bold().FontSize(8).FontColor("#0d9488");
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
                    table.Cell().BorderTop(1).BorderColor("#99f6e4").Padding(3).Text("TOTAL").Bold().FontSize(8).FontColor("#0d9488");
                    table.Cell().BorderTop(1).BorderColor("#99f6e4").Padding(3).AlignRight().Text("").FontSize(8);
                    table.Cell().BorderTop(1).BorderColor("#99f6e4").Padding(3).AlignRight().Text($"{totalKcal:F0}").Bold().FontSize(8).FontColor("#0d9488");
                    table.Cell().BorderTop(1).BorderColor("#99f6e4").Padding(3).AlignRight().Text($"{totalProtein:F1}").Bold().FontSize(8).FontColor("#0d9488");
                    table.Cell().BorderTop(1).BorderColor("#99f6e4").Padding(3).AlignRight().Text($"{totalCarbs:F1}").Bold().FontSize(8).FontColor("#0d9488");
                    table.Cell().BorderTop(1).BorderColor("#99f6e4").Padding(3).AlignRight().Text($"{totalFat:F1}").Bold().FontSize(8).FontColor("#0d9488");
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

            col.Item().BorderBottom(1).BorderColor("#0d9488").PaddingBottom(4)
                .Text("OBJETIVOS NUTRICIONALES DIARIOS").Bold().FontSize(11).FontColor("#0d9488");

            col.Item().Row(row =>
            {
                row.Spacing(8);
                MacroCard(row.RelativeItem(), "Calorías", $"{diet.target_kcal:F0}", "kcal", "#e53935");
                MacroCard(row.RelativeItem(), "Proteínas", $"{diet.target_protein:F1}", "g", "#43a047");
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

    private void ComposeWeeklyNutritionSummary(
        IContainer container,
        diets diet,
        angulosodbContext context,
        int userId,
        bool canUseTenantLocalFoods,
        List<foods> exchangeFoods)
    {
        var orderedDays = diet.diet_days?.OrderBy(day => day.day_index).ToList() ?? new List<diet_days>();
        if (orderedDays.Count == 0) return;

        var mealItems = orderedDays
            .Where(day => day.meals != null)
            .SelectMany(day => day.meals)
            .Where(meal => meal.meal_items != null)
            .SelectMany(meal => meal.meal_items)
            .ToList();

        // Las recetas se resuelven con el mismo ámbito de tenant y propietario de la dieta.
        var recipeIds = mealItems.Where(item => item.recipe_id.HasValue)
            .Select(item => item.recipe_id!.Value).Distinct().ToList();
        var recipeIngredients = recipeIds.Count == 0
            ? new List<recipe_items>()
            : context.recipe_items.AsNoTracking()
                .Include(ingredient => ingredient.recipe)
                .Where(ingredient => recipeIds.Contains(ingredient.recipe_id) &&
                    ingredient.recipe != null &&
                    ingredient.recipe.tenant_id == diet.tenant_id &&
                    ingredient.recipe.user_id == diet.user_id)
                .ToList();

        var foodIds = mealItems.Where(item => item.food_id.HasValue).Select(item => item.food_id!.Value)
            .Concat(recipeIngredients.Select(ingredient => ingredient.food_id))
            .Distinct().ToList();
        var foodsById = foodIds.Count == 0
            ? new Dictionary<int, foods>()
            : context.foods.AsNoTracking()
                .Where(food => foodIds.Contains(food.id) &&
                    ((food.source == null || food.source.ToLower() != "local") ||
                     (diet.tenant_id.HasValue && food.tenant_id == diet.tenant_id.Value &&
                      (canUseTenantLocalFoods || food.created_by_user_id == userId))))
                .ToDictionary(food => food.id);

        var exchangeProfiles = new Dictionary<int, foods>();
        foreach (var group in exchangeFoods
            .Where(food => food.exchange_group_id.HasValue && food.grams_per_exchange.GetValueOrDefault() > 0)
            .GroupBy(food => food.exchange_group_id!.Value))
        {
            var members = group.ToList();
            double PerExchange(Func<foods, double?> selector) =>
                members.Average(food => (selector(food) ?? 0) * (double)food.grams_per_exchange!.Value);

            exchangeProfiles[group.Key] = new foods
            {
                id = -group.Key,
                calcium_mg = PerExchange(food => food.calcium_mg),
                iron_mg = PerExchange(food => food.iron_mg),
                magnesium_mg = PerExchange(food => food.magnesium_mg),
                potassium_mg = PerExchange(food => food.potassium_mg),
                zinc_mg = PerExchange(food => food.zinc_mg),
                vitamin_a_ug = PerExchange(food => food.vitamin_a_ug),
                vitamin_c_mg = PerExchange(food => food.vitamin_c_mg),
                vitamin_d_ug = PerExchange(food => food.vitamin_d_ug),
                vitamin_e_mg = PerExchange(food => food.vitamin_e_mg),
                vitamin_b12_ug = PerExchange(food => food.vitamin_b12_ug),
                folate_ug = PerExchange(food => food.folate_ug),
                fiber = PerExchange(food => food.fiber),
                sugar = members.All(food => food.sugar.HasValue) ? PerExchange(food => food.sugar) : null,
                salt = PerExchange(food => food.salt),
                saturated_fat = members.All(food => food.saturated_fat.HasValue) ? PerExchange(food => food.saturated_fat) : null,
                monounsaturated_fat = members.All(food => food.monounsaturated_fat.HasValue) ? PerExchange(food => food.monounsaturated_fat) : null,
                polyunsaturated_fat = members.All(food => food.polyunsaturated_fat.HasValue) ? PerExchange(food => food.polyunsaturated_fat) : null
            };
        }

        var foodsForCalculation = foodsById.Values.ToList();
        foodsForCalculation.AddRange(exchangeProfiles.Values);
        var recipesById = recipeIngredients
            .Where(ingredient => ingredient.recipe != null)
            .GroupBy(ingredient => ingredient.recipe_id)
            .ToDictionary(group => group.Key, group => group.ToList());

        var calculationDiet = new DietDetailDto
        {
            Days = orderedDays.Select(day => new DietDayDto
            {
                DayIndex = day.day_index,
                Meals = (day.meals ?? new List<meals>()).OrderBy(meal => meal.meal_index).Select(meal => new MealDto
                {
                    Name = meal.name ?? "Comida",
                    MealIndex = meal.meal_index,
                    Items = (meal.meal_items ?? new List<meal_items>()).SelectMany(item =>
                    {
                        if (item.food_id.HasValue && item.grams.GetValueOrDefault() > 0 &&
                            foodsById.ContainsKey(item.food_id.Value))
                            return new[] { new MealItemDto { FoodId = item.food_id, Grams = item.grams } };

                        if (item.recipe_id.HasValue && recipesById.TryGetValue(item.recipe_id.Value, out var ingredients))
                        {
                            var recipeServings = ingredients.FirstOrDefault()?.recipe?.servings ?? 1m;
                            if (recipeServings <= 0) recipeServings = 1m;
                            var servingsToUse = item.recipe_servings.GetValueOrDefault(1m);
                            if (servingsToUse <= 0) servingsToUse = 1m;
                            var factor = servingsToUse / recipeServings;
                            return ingredients
                                .Where(ingredient => foodsById.ContainsKey(ingredient.food_id) && ingredient.grams > 0)
                                .Select(ingredient => new MealItemDto
                                {
                                    FoodId = ingredient.food_id,
                                    Grams = ingredient.grams * factor
                                });
                        }

                        if (item.exchange_group_id.HasValue &&
                            exchangeProfiles.ContainsKey(item.exchange_group_id.Value) &&
                            item.exchange_count.GetValueOrDefault() > 0)
                            return new[]
                            {
                                new MealItemDto
                                {
                                    FoodId = -item.exchange_group_id.Value,
                                    Grams = item.exchange_count!.Value * 100m
                                }
                            };

                        return Array.Empty<MealItemDto>();
                    }).ToList()
                }).ToList()
            }).ToList()
        };

        DietGeneratorService.PopulateDailyMicronutrients(calculationDiet, foodsForCalculation);
        var summaries = calculationDiet.Days.Select(day => day.Micronutrients ?? new MicronutrientDailySummaryDto()).ToList();
        var dayCount = orderedDays.Count;

        double DayMacro(diet_days day, Func<meal_items, decimal?> selector) =>
            (day.meals ?? new List<meals>()).SelectMany(meal => meal.meal_items ?? new List<meal_items>())
                .Sum(item => (double)selector(item).GetValueOrDefault());
        double AverageMacro(Func<diet_days, Func<meal_items, decimal?>, double> selector, Func<meal_items, decimal?> macro) =>
            orderedDays.Sum(day => selector(day, macro)) / dayCount;

        double totalKcal = orderedDays.Sum(day => DayMacro(day, item => item.kcal));
        double avgKcal = totalKcal / dayCount;

        decimal AverageNutrient(Func<MicronutrientDailySummaryDto, decimal> selector) =>
            summaries.Sum(selector) / dayCount;
        int TotalFoods = summaries.Sum(summary => summary.TotalFoodCount);
        int Coverage(Func<MicronutrientDailySummaryDto, int> selector) =>
            TotalFoods > 0 ? (int)Math.Round(summaries.Sum(selector) * 100m / TotalFoods, MidpointRounding.AwayFromZero) : 0;
        string PartialValue(decimal value, int available, int total) =>
            available <= 0 ? "—" : $"{value:0.0} g ({(total > 0 ? (int)Math.Round(available * 100m / total) : 0)}%)";
        string AveragePartialValue(decimal value, int available) =>
            available <= 0 ? "—" : $"{value:0.0} g ({Coverage(_ => available)}%)";

        container.Column(col =>
        {
            col.Spacing(8);
            col.Item().BorderBottom(1).BorderColor("#0d9488").PaddingBottom(4)
                .Text("RESUMEN NUTRICIONAL DE LA DIETA").Bold().FontSize(14).FontColor("#0d9488");
            col.Item().Text($"{dayCount} días · valores diarios y media del periodo. Los micronutrientes se expresan como porcentaje de la CDR.")
                .FontSize(8).FontColor("#64748b");

            col.Item().Table(cards =>
            {
                cards.ColumnsDefinition(columns =>
                {
                    for (var i = 0; i < 4; i++) columns.RelativeColumn();
                });
                void Card(string label, decimal value, int coverage, string extra = "")
                {
                    cards.Cell().Background("#f0fdfa").Border(0.5f).BorderColor("#ccfbf1").Padding(6).Column(card =>
                    {
                        card.Item().Text(label).FontSize(7).FontColor("#475569").SemiBold();
                        card.Item().Text($"{value:0.0} g/día").FontSize(11).Bold().FontColor("#0f766e");
                        card.Item().Text($"{coverage}% de alimentos con dato").FontSize(6.5f).FontColor("#64748b");
                        if (!string.IsNullOrWhiteSpace(extra))
                            card.Item().Text(extra).FontSize(6.5f).FontColor("#64748b");
                    });
                }

                var sugarCount = summaries.Sum(summary => summary.SugarFoodCount);
                var saturatedCount = summaries.Sum(summary => summary.SaturatedFatFoodCount);
                var monoCount = summaries.Sum(summary => summary.MonounsaturatedFatFoodCount);
                var polyCount = summaries.Sum(summary => summary.PolyunsaturatedFatFoodCount);
                var sugarAverage = AverageNutrient(summary => summary.SugarG);
                var saturatedAverage = AverageNutrient(summary => summary.SaturatedFatG);
                var monoAverage = AverageNutrient(summary => summary.MonounsaturatedFatG);
                var polyAverage = AverageNutrient(summary => summary.PolyunsaturatedFatG);
                var sugarEnergy = avgKcal > 0 ? (double)sugarAverage * 4 / avgKcal * 100 : 0;
                var saturatedEnergy = avgKcal > 0 ? (double)saturatedAverage * 9 / avgKcal * 100 : 0;
                Card("Azúcares totales", sugarAverage, Coverage(summary => summary.SugarFoodCount),
                    sugarCount > 0 && avgKcal > 0 ? $"{sugarEnergy:0.0}% de energía" : "");
                Card("Grasas saturadas", saturatedAverage, Coverage(summary => summary.SaturatedFatFoodCount),
                    saturatedCount > 0 && avgKcal > 0 ? $"{saturatedEnergy:0.0}% de energía" : "");
                Card("Grasas monoinsaturadas", monoAverage, Coverage(summary => summary.MonounsaturatedFatFoodCount));
                Card("Grasas poliinsaturadas", polyAverage, Coverage(summary => summary.PolyunsaturatedFatFoodCount));
            });

            col.Item().Text("TABLA DIARIA Y MEDIA").Bold().FontSize(10).FontColor("#334155");
            // Se divide en bloques de siete días para conservar una tabla legible incluso en planes largos.
            for (var start = 0; start < dayCount; start += 7)
            {
                var chunk = Enumerable.Range(start, Math.Min(7, dayCount - start)).ToList();
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2.2f);
                        foreach (var _ in chunk) columns.RelativeColumn();
                        columns.RelativeColumn();
                    });
                    table.Header(header =>
                    {
                        header.Cell().Background("#0f766e").Padding(3).Text("Nutriente").Bold().FontSize(7).FontColor(Colors.White);
                        foreach (var index in chunk)
                            header.Cell().Background("#0f766e").Padding(3).AlignCenter().Text($"Día {index + 1}").Bold().FontSize(7).FontColor(Colors.White);
                        header.Cell().Background("#0f766e").Padding(3).AlignCenter().Text("Media").Bold().FontSize(7).FontColor(Colors.White);
                    });

                    void AddRow(string label, Func<int, string> dayValue, string average)
                    {
                        table.Cell().Background("#f8fafc").Padding(3).Text(label).FontSize(7).SemiBold();
                        foreach (var index in chunk)
                            table.Cell().Padding(3).AlignCenter().Text(dayValue(index)).FontSize(6.5f);
                        table.Cell().Background("#f0fdfa").Padding(3).AlignCenter().Text(average).FontSize(6.5f).SemiBold();
                    }

                    AddRow("Energía (kcal)", index => $"{DayMacro(orderedDays[index], item => item.kcal):0}", $"{avgKcal:0}");
                    AddRow("Proteínas (g)", index => $"{DayMacro(orderedDays[index], item => item.protein):0.0}", $"{AverageMacro(DayMacro, item => item.protein):0.0}");
                    AddRow("Hidratos (g)", index => $"{DayMacro(orderedDays[index], item => item.carbs):0.0}", $"{AverageMacro(DayMacro, item => item.carbs):0.0}");
                    AddRow("Grasas (g)", index => $"{DayMacro(orderedDays[index], item => item.fat):0.0}", $"{AverageMacro(DayMacro, item => item.fat):0.0}");
                    AddRow("Azúcares (g)", index => PartialValue(summaries[index].SugarG, summaries[index].SugarFoodCount, summaries[index].TotalFoodCount),
                        sugarCount > 0 ? $"{sugarAverage:0.0} g ({Coverage(summary => summary.SugarFoodCount)}%)" : "—");
                    AddRow("Saturadas (g)", index => PartialValue(summaries[index].SaturatedFatG, summaries[index].SaturatedFatFoodCount, summaries[index].TotalFoodCount),
                        saturatedCount > 0 ? $"{saturatedAverage:0.0} g ({Coverage(summary => summary.SaturatedFatFoodCount)}%)" : "—");
                    AddRow("Monoinsat. (g)", index => PartialValue(summaries[index].MonounsaturatedFatG, summaries[index].MonounsaturatedFatFoodCount, summaries[index].TotalFoodCount),
                        monoCount > 0 ? $"{monoAverage:0.0} g ({Coverage(summary => summary.MonounsaturatedFatFoodCount)}%)" : "—");
                    AddRow("Poliinsat. (g)", index => PartialValue(summaries[index].PolyunsaturatedFatG, summaries[index].PolyunsaturatedFatFoodCount, summaries[index].TotalFoodCount),
                        polyCount > 0 ? $"{polyAverage:0.0} g ({Coverage(summary => summary.PolyunsaturatedFatFoodCount)}%)" : "—");

                    AddRow("Calcio (% CDR)", index => $"{summaries[index].CalciumPctRda:0}%", $"{AverageNutrient(summary => summary.CalciumPctRda):0}%");
                    AddRow("Hierro (% CDR)", index => $"{summaries[index].IronPctRda:0}%", $"{AverageNutrient(summary => summary.IronPctRda):0}%");
                    AddRow("Magnesio (% CDR)", index => $"{summaries[index].MagnesiumPctRda:0}%", $"{AverageNutrient(summary => summary.MagnesiumPctRda):0}%");
                    AddRow("Potasio (% CDR)", index => $"{summaries[index].PotassiumPctRda:0}%", $"{AverageNutrient(summary => summary.PotassiumPctRda):0}%");
                    AddRow("Zinc (% CDR)", index => $"{summaries[index].ZincPctRda:0}%", $"{AverageNutrient(summary => summary.ZincPctRda):0}%");
                    AddRow("Vitamina A (% CDR)", index => $"{summaries[index].VitaminAPctRda:0}%", $"{AverageNutrient(summary => summary.VitaminAPctRda):0}%");
                    AddRow("Vitamina C (% CDR)", index => $"{summaries[index].VitaminCPctRda:0}%", $"{AverageNutrient(summary => summary.VitaminCPctRda):0}%");
                    AddRow("Vitamina D (% CDR)", index => $"{summaries[index].VitaminDPctRda:0}%", $"{AverageNutrient(summary => summary.VitaminDPctRda):0}%");
                    AddRow("Vitamina E (% CDR)", index => $"{summaries[index].VitaminEPctRda:0}%", $"{AverageNutrient(summary => summary.VitaminEPctRda):0}%");
                    AddRow("Vitamina B12 (% CDR)", index => $"{summaries[index].VitaminB12PctRda:0}%", $"{AverageNutrient(summary => summary.VitaminB12PctRda):0}%");
                    AddRow("Folato (% CDR)", index => $"{summaries[index].FolatePctRda:0}%", $"{AverageNutrient(summary => summary.FolatePctRda):0}%");
                    AddRow("Fibra (% CDR)", index => $"{summaries[index].FiberPctRda:0}%", $"{AverageNutrient(summary => summary.FiberPctRda):0}%");
                });
            }

            col.Item().Text("Los valores de azúcares y grasas son parciales cuando la cobertura es inferior al 100%; el porcentaje entre paréntesis indica la proporción de alimentos con dato disponible. Los azúcares son totales, no necesariamente libres o añadidos. Las alertas de exceso solo se interpretan con cobertura completa. Los porcentajes de micronutrientes son referencias orientativas de ingesta diaria recomendada.")
                .FontSize(7).Italic().FontColor("#64748b");
        });
    }

    private void ComposeEquivalenceTable(IContainer container, List<foods> exchangeFoods)
    {
        container.Column(col =>
        {
            col.Spacing(10);
            col.Item().Text("TABLA DE EQUIVALENCIAS DE INTERCAMBIOS").Bold().FontSize(14).FontColor("#0d9488");
            col.Item().Text("Use esta tabla para cambiar un alimento por otro equivalente de su mismo grupo. Las cantidades mostradas corresponden a 1 INTERCAMBIO.").FontSize(9).Italic().FontColor("#555555");

            // Agrupar por grupo permite imprimir una sola referencia nutricional del intercambio y después listar sus alimentos equivalentes.
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
                    header.Cell().Background("#f0fdfa").Padding(4).Text("Grupo").Bold().FontSize(9).FontColor("#0d9488");
                    header.Cell().Background("#f0fdfa").Padding(4).Text("Alimento").Bold().FontSize(9).FontColor("#0d9488");
                    header.Cell().Background("#f0fdfa").Padding(4).AlignRight().Text("Cantidad").Bold().FontSize(9).FontColor("#0d9488");
                    header.Cell().Background("#f0fdfa").Padding(4).Text("Macros (Carb / Prot / Gras / Kcal)").Bold().FontSize(9).FontColor("#0d9488");
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

    private void ComposeShoppingList(IContainer container, diets diet, angulosodbContext context, int userId, bool canUseTenantLocalFoods)
    {
        // Consolida alimentos directos e ingredientes de recetas para que la lista sea realmente útil para hacer la compra.
        var mealItems = diet.diet_days?.Where(day => day.meals != null)
            .SelectMany(day => day.meals).Where(meal => meal.meal_items != null)
            .SelectMany(meal => meal.meal_items).ToList() ?? new List<meal_items>();

        var recipeIds = mealItems.Where(item => item.recipe_id.HasValue)
            .Select(item => item.recipe_id!.Value).Distinct().ToList();
        var recipeIngredients = recipeIds.Count == 0
            ? new List<recipe_items>()
            : context.recipe_items.AsNoTracking().Include(ingredient => ingredient.recipe)
                .Where(ingredient => recipeIds.Contains(ingredient.recipe_id) &&
                    ingredient.recipe != null && ingredient.recipe.tenant_id == diet.tenant_id)
                .ToList();

        var foodIds = mealItems.Where(item => item.food_id.HasValue).Select(item => item.food_id!.Value)
            .Concat(recipeIngredients.Select(ingredient => ingredient.food_id)).Distinct().ToList();
        var accessibleFoods = foodIds.Count == 0
            ? new Dictionary<int, foods>()
            : context.foods.AsNoTracking()
                .Where(food => foodIds.Contains(food.id) &&
                    ((food.source == null || food.source.ToLower() != "local") ||
                     (diet.tenant_id.HasValue && food.tenant_id == diet.tenant_id.Value &&
                      (canUseTenantLocalFoods || food.created_by_user_id == userId))))
                .ToDictionary(food => food.id);

        var foodAmounts = new List<(foods Food, double Grams)>();
        foreach (var item in mealItems)
        {
            if (item.food_id.HasValue && item.grams.GetValueOrDefault() > 0 &&
                accessibleFoods.TryGetValue(item.food_id.Value, out var directFood))
                foodAmounts.Add((directFood, (double)item.grams!.Value));

            if (!item.recipe_id.HasValue) continue;
            var ingredients = recipeIngredients.Where(ingredient => ingredient.recipe_id == item.recipe_id.Value).ToList();
            if (ingredients.Count == 0) continue;
            var servings = ingredients.FirstOrDefault()?.recipe?.servings ?? 1m;
            if (servings <= 0) servings = 1m;
            var servingsToUse = item.recipe_servings.GetValueOrDefault(1m);
            if (servingsToUse <= 0) servingsToUse = 1m;
            foreach (var ingredient in ingredients)
                if (ingredient.grams > 0 && accessibleFoods.TryGetValue(ingredient.food_id, out var ingredientFood))
                    foodAmounts.Add((ingredientFood, (double)(ingredient.grams / servings * servingsToUse)));
        }

        var shoppingItems = new List<ShoppingListItem>();
        foreach (var amount in foodAmounts)
        {
            var existing = shoppingItems.FirstOrDefault(item => item.FoodId == amount.Food.id);
            if (existing != null)
                existing.Grams += amount.Grams;
            else
                shoppingItems.Add(new ShoppingListItem
                {
                    FoodId = amount.Food.id,
                    Name = amount.Food.name,
                    Grams = amount.Grams
                });
        }

        container.Column(col =>
        {
            col.Spacing(6);
            col.Item().Text("LISTA DE LA COMPRA").Bold().FontSize(14).FontColor("#0d9488");
            col.Item().Text("Alimentos necesarios para todos los días, incluidos los ingredientes de las recetas. Las cantidades están consolidadas.")
                .FontSize(9).Italic().FontColor("#555555");

            if (!shoppingItems.Any())
            {
                col.Item().Text("No hay alimentos en la dieta.").Italic().FontSize(10).FontColor("#888888");
                return;
            }

            foreach (var item in shoppingItems.OrderBy(item => item.Name))
            {
                col.Item().Row(row =>
                {
                    row.Spacing(8);
                    row.ConstantItem(10).Height(10).Border(1).BorderColor("#999999").Background(Colors.White);
                    row.RelativeItem().Text(item.Name).FontSize(9);
                    var quantity = item.Grams > 0 ? $"{item.Grams:F0} g" : "-";
                    row.ConstantItem(100).AlignRight().Text(quantity).FontSize(9).FontColor("#555555");
                });
            }
        });
    }

    private class ShoppingListItem
    {
        public int FoodId { get; set; }
        public string Name { get; set; } = string.Empty;
        public double Grams { get; set; }
    }
}
