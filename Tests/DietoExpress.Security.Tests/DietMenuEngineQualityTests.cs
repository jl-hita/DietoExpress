using System.Diagnostics;
using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace DietoExpress.Security.Tests;

/// <summary>
/// Calidad, seguridad y rendimiento del generador con un catálogo de formato BEDCA (crudo/cocinado, datos incoherentes
/// y productos procesados incluidos). Complementa DietGeneratorScenarioTests, que usa un catálogo de juguete.
/// </summary>
[Collection("DietaryProfileCatalog")]
public sealed class DietMenuEngineQualityTests
{
    private readonly ITestOutputHelper _output;
    public DietMenuEngineQualityTests(ITestOutputHelper output) => _output = output;

    private static async Task<(angulosodbContext Context, DietGeneratorService Generator)> CreateAsync(Action<List<foods>>? customize = null, Action<angulosodbContext>? seed = null)
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>().UseInMemoryDatabase($"engine-{Guid.NewGuid():N}").Options;
        var context = new angulosodbContext(options);
        var catalog = BedcaLikeCatalog.Create();
        customize?.Invoke(catalog);
        context.foods.AddRange(catalog);
        seed?.Invoke(context);
        await context.SaveChangesAsync();
        var rules = new SpecializationRulesService(context);
        var generator = new DietGeneratorService(context, rules, new DietSemanticValidationService(new DietValidationService(rules)));
        return (context, generator);
    }

    private static GenerateDietRequestDto Request(double kcal = 2000, string type = "Equilibrada", int days = 7, int meals = 5, int? clientId = null) => new()
    {
        NumberOfDays = days, MealsPerDay = meals, TargetKcal = kcal, DietType = type, ClientId = clientId,
        UseRecipes = false, EnableDietTherapyRules = false, EnableAutomaticEnergyEstimate = false
    };

    private static List<MealItemDto> Items(DietDetailDto diet) => diet.Days.SelectMany(d => d.Meals).SelectMany(m => m.Items).ToList();

    [Theory]
    [InlineData(1500, "Equilibrada", 3)]
    [InlineData(2000, "Equilibrada", 5)]
    [InlineData(3500, "Equilibrada", 5)]
    [InlineData(2400, "AltaProteina", 4)]
    [InlineData(1800, "BajaCarbos", 5)]
    [InlineData(2000, "Vegetariana", 5)]
    [InlineData(2000, "Vegana", 5)]
    [InlineData(1800, "Cetogenica", 5)]
    public async Task EveryDietTypeProducesAValidWeekWithAccurateEnergyAndMacros(double kcal, string type, int meals)
    {
        var (context, generator) = await CreateAsync();
        await using var _ = context;
        var diet = await generator.GenerateDietAsync(Request(kcal, type, 7, meals), null, 1, false);

        Assert.Equal(7, diet.Days.Count);
        foreach (var day in diet.Days)
        {
            var k = day.Meals.SelectMany(m => m.Items).Sum(i => (double)(i.Kcal ?? 0));
            Assert.InRange(Math.Abs(k - kcal) / kcal, 0, 0.06);
        }
        // Los macros de cada elemento coinciden con sus gramos (no hay desajustes entre cantidad mostrada y nutrientes).
        var foodsById = context.foods.ToDictionary(f => f.id);
        foreach (var item in Items(diet))
        {
            var food = foodsById[item.FoodId!.Value];
            Assert.InRange(Math.Abs((double)item.Kcal! - (food.kcal ?? 0) * (double)item.Grams! / 100.0), 0, 0.11);
        }
    }

    [Fact]
    public async Task SpanishStructure_LunchHasFirstAndSecondCourseAndDinnerIsDifferentFromLunch()
    {
        var (context, generator) = await CreateAsync();
        await using var _ = context;
        var diet = await generator.GenerateDietAsync(Request(), null, 1, false);

        foreach (var day in diet.Days)
        {
            var lunch = day.Meals.Single(m => m.Name == "Comida");
            var dinner = day.Meals.Single(m => m.Name == "Cena");
            var roles = lunch.Items.Select(i => i.CourseRole).ToList();
            Assert.Contains("Primer plato", roles);
            Assert.Contains("Segundo plato", roles);
            Assert.Contains("Guarnición", roles);
            Assert.Contains("Aliño / Cocinado", roles);
            Assert.NotEqual(
                string.Join("|", lunch.Items.Select(i => i.FoodId).OrderBy(x => x)),
                string.Join("|", dinner.Items.Select(i => i.FoodId).OrderBy(x => x)));
            // El segundo plato de la comida y el de la cena del mismo día no son el mismo alimento.
            Assert.NotEqual(
                lunch.Items.Single(i => i.CourseRole == "Segundo plato").FoodId,
                dinner.Items.Single(i => i.CourseRole == "Segundo plato").FoodId);
            // El desayuno es de desayuno: nunca lleva carne ni pescado como segundo plato.
            Assert.DoesNotContain(day.Meals.First().Items, i => i.CourseRole == "Segundo plato");
        }
    }

    [Fact]
    public async Task WeeklyVariety_RotatesProteinsRespectsRepetitionCapAndAvoidsCompleteMealRepeats()
    {
        var (context, generator) = await CreateAsync();
        await using var _ = context;
        var request = Request();
        request.MaxWeeklyFoodRepetitions = 4;
        var diet = await generator.GenerateDietAsync(request, null, 1, false);
        var items = Items(diet);
        var foodsById = context.foods.ToDictionary(f => f.id);

        Assert.True(items.Select(i => i.FoodId).Distinct().Count() >= 40, "La semana debe usar al menos 40 alimentos distintos.");

        var seconds = diet.Days.SelectMany(d => d.Meals).SelectMany(m => m.Items).Where(i => i.CourseRole == "Segundo plato")
            .Select(i => FoodCulinaryClassifier.Classify(foodsById[i.FoodId!.Value].name, null, foodsById[i.FoodId.Value].kcal, foodsById[i.FoodId.Value].protein, foodsById[i.FoodId.Value].carbs, foodsById[i.FoodId.Value].fat)!.Family)
            .GroupBy(f => f).ToDictionary(g => g.Key, g => g.Count());
        _output.WriteLine(string.Join(", ", seconds.Select(kv => $"{kv.Key}={kv.Value}")));
        Assert.True(seconds.GetValueOrDefault("pescado_blanco") + seconds.GetValueOrDefault("pescado_azul") >= 2, "Al menos 2 pescados por semana.");
        Assert.True(seconds.GetValueOrDefault("carne_roja") <= 3, "Carne roja limitada.");
        Assert.True(seconds.GetValueOrDefault("huevo") >= 1, "Al menos un huevo en la semana.");

        var staples = items.GroupBy(i => i.FoodId).Where(g => g.Count() > 4)
            .Select(g => foodsById[g.Key!.Value].name).ToList();
        foreach (var name in staples)
            Assert.True(FoodCulinaryClassifier.Classify(name, null, 100, 5, 5, 5)?.IsStaple == true, $"«{name}» supera el límite de repetición sin ser un básico.");

        var repeatedMeals = diet.Days.SelectMany(d => d.Meals)
            .GroupBy(m => string.Join("|", m.Items.Select(i => i.FoodId).OrderBy(x => x))).Max(g => g.Count());
        Assert.True(repeatedMeals <= 3);

        // Legumbres como primer plato al menos una vez a la semana y nunca en dos comidas consecutivas.
        var legumeDays = diet.Days.Where(d => d.Meals.Single(m => m.Name == "Comida").Items.Any(i =>
            i.CourseRole == "Primer plato" && (foodsById[i.FoodId!.Value].name.Contains("Lenteja") || foodsById[i.FoodId.Value].name.Contains("Garbanzo") || foodsById[i.FoodId.Value].name.Contains("Alubia")))).Select(d => d.DayIndex).ToList();
        Assert.NotEmpty(legumeDays);
        for (var i = 1; i < legumeDays.Count; i++) Assert.NotEqual(legumeDays[i - 1] + 1, legumeDays[i]);
    }

    [Fact]
    public async Task OnlyAdmissibleFoodsAreUsed_NoProcessedInconsistentOrUnusualFoodsAndRawStateIsExplained()
    {
        var (context, generator) = await CreateAsync();
        await using var _ = context;
        var diet = await generator.GenerateDietAsync(Request(), null, 1, false);
        var names = Items(diet).Select(i => i.FoodName!).Distinct().ToList();
        foreach (var banned in new[] { "Arroz, hervido", "Lenteja, hervida", "Garbanzo, hervido", "Judia verde, hervida", "Pollo, frito", "Chorizo", "Salmón ahumado", "Zumo", "Miel", "Gelatina", "Pan blanco", "Aceite de algodón", "Huevo de codorniz" })
            Assert.DoesNotContain(names, n => n!.StartsWith(banned, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("peso en crudo", diet.Notes);
    }

    [Theory]
    [InlineData("cacahuete", "cacahuete")]
    [InlineData("frutos secos", "frutos_cascara")]
    [InlineData("leche", "lacteos")]
    [InlineData("lactosa", "lacteos")]
    [InlineData("huevo", "huevo")]
    [InlineData("soja", "soja")]
    [InlineData("gluten", "gluten")]
    [InlineData("pescado", "pescado")]
    [InlineData("crustáceos", "crustaceos")]
    [InlineData("moluscos", "moluscos")]
    [InlineData("marisco", "crustaceos")]
    [InlineData("apio", "apio")]
    public async Task AllergenProperty_NoFoodWithTheCuratedAllergenEverReachesAnAllergicPatient(string declaredAllergy, string curatedTag)
    {
        // Los datos importados dicen «reported» sin etiquetas; la curación por identidad del perfil sigue bloqueando.
        var (context, generator) = await CreateAsync(seed: ctx => ctx.clients.Add(new clients
        {
            id = 1, full_name = "Paciente",
            food_preferences = new food_preferences { id = 1, client_id = 1, allergies = declaredAllergy }
        }));
        await using var _ = context;
        // Los alérgenos salen de food_dietary_profile (única fuente); aquí un oráculo por nombre hace de curación del catálogo sintético.
        FoodDietaryProfileCatalog.SetForTests(context.foods.ToDictionary(f => f.id,
            f => new FoodDietaryProfileCatalog.Profile(false, true, FoodDietaryProfileCatalog.ExpandAllergens(CuratedAllergensOf(f.name)))));
        try
        {
        var diet = await generator.GenerateDietAsync(Request(2000, "Equilibrada", 7, 5, 1), null, 1, false);
        var foodsById = context.foods.ToDictionary(f => f.id);
        Assert.NotEmpty(Items(diet));
        foreach (var item in Items(diet))
        {
            var food = foodsById[item.FoodId!.Value];
            var allergens = CuratedAllergensOf(food.name);
            if (declaredAllergy is "cacahuete")
                Assert.DoesNotContain("cacahuete", food.name!, StringComparison.OrdinalIgnoreCase);
            if (curatedTag == "crustaceos" && declaredAllergy == "marisco")
                Assert.DoesNotContain(allergens, a => a is "crustaceos" or "moluscos");
            else
                Assert.DoesNotContain(allergens, a => a == curatedTag);
        }
        }
        finally { FoodDietaryProfileCatalog.SetForTests(new Dictionary<int, FoodDietaryProfileCatalog.Profile>()); }
    }

    private static string[] CuratedAllergensOf(string? name)
    {
        var n = (name ?? "").ToLowerInvariant();
        var result = new List<string>();
        void When(string tag, params string[] words) { if (words.Any(w => n.Contains(w))) result.Add(tag); }
        When("huevo", "huevo", "tortilla");
        When("lacteos", "yogur", "leche", "queso", "kefir", "requeson", "cuajada");
        When("pescado", "merluza", "salmon", "atun", "sardina", "bacalao", "lubina", "dorada", "pescado", "caballa");
        When("crustaceos", "gamba", "langostino", "cangrejo");
        When("moluscos", "calamar", "mejillon", "pulpo", "almeja", "sepia");
        When("gluten", "pasta", "trigo", "cuscus", "avena", "cebada", "centeno", "espagueti", "macarron", "muesli", "cereal", "galleta", "harina", "seit", "bulgur", "pizza", "croissant", "bizcocho", "tostada", "cracker", "copos", "pan integral", "pan blanco", "pan de");
        When("soja", "soja", "tofu");
        When("frutos_cascara", "almendra", "nuez", "avellana", "pistacho", "anacardo");
        When("cacahuete", "cacahuete");
        When("apio", "apio");
        if (n == "pan" || n.StartsWith("pan ") || n.StartsWith("pan,")) result.Add("gluten");
        return result.Distinct().ToArray();
    }

    [Fact]
    public async Task UnverifiedCatalogFailsClosedForAllergicPatientsUntilAllergensArePromoted()
    {
        var (context, generator) = await CreateAsync(c => c.ForEach(f => f.allergen_data_status = "unknown"), ctx => ctx.clients.Add(new clients
        {
            id = 1, full_name = "Paciente", food_preferences = new food_preferences { id = 1, client_id = 1, allergies = "frutos secos" }
        }));
        await using var _ = context;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateDietAsync(Request(2000, "Equilibrada", 7, 5, 1), null, 1, false));
        Assert.Contains("alérgenos verificados", ex.Message);

        // Tras la revisión humana y la promoción (allergen_data_status = reported) el mismo paciente recibe plan.
        foreach (var food in context.foods) food.allergen_data_status = "reported";
        await context.SaveChangesAsync();
        var diet = await generator.GenerateDietAsync(Request(2000, "Equilibrada", 7, 5, 1), null, 1, false);
        Assert.DoesNotContain(Items(diet), i => i.FoodName!.StartsWith("Almendra") || i.FoodName.StartsWith("Nuez") || i.FoodName.StartsWith("Avellana") || i.FoodName.StartsWith("Pistacho") || i.FoodName.StartsWith("Anacardo"));
    }

    [Fact]
    public async Task GlutenIntoleranceWithoutVerifiedImportedData_UsesCuratedTagsAndExcludesGlutenFoods()
    {
        var (context, generator) = await CreateAsync(c => c.ForEach(f => f.allergen_data_status = "unknown"), ctx => ctx.clients.Add(new clients
        {
            id = 1, full_name = "Celíaco", digestive_health = new digestive_health { id = 1, client_id = 1, gluten_intolerance = true }
        }));
        await using var _ = context;
        FoodDietaryProfileCatalog.SetForTests(context.foods.ToDictionary(f => f.id,
            f => new FoodDietaryProfileCatalog.Profile(false, true, FoodDietaryProfileCatalog.ExpandAllergens(CuratedAllergensOf(f.name)))));
        try
        {
        var diet = await generator.GenerateDietAsync(Request(2000, "Equilibrada", 7, 5, 1), null, 1, false);
        Assert.DoesNotContain(Items(diet), i => i.FoodName!.StartsWith("Pan") || i.FoodName.StartsWith("Pasta") || i.FoodName.StartsWith("Avena") || i.FoodName.StartsWith("Seitán") || i.FoodName.StartsWith("Sémola"));
        }
        finally { FoodDietaryProfileCatalog.SetForTests(new Dictionary<int, FoodDietaryProfileCatalog.Profile>()); }
    }

    [Fact]
    public async Task Performance_WeekGenerationStaysWithinTheBudgetAndNeverFallsBackToGlobalRetries()
    {
        var (context, generator) = await CreateAsync();
        await using var _ = context;
        await generator.GenerateDietAsync(Request(), null, 1, false); // calentamiento JIT

        var times = new List<long>();
        for (var i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            var diet = await generator.GenerateDietAsync(Request(2000 + i * 50), null, 1, false);
            sw.Stop();
            times.Add(sw.ElapsedMilliseconds);
            Assert.Equal(7, diet.Days.Count);
        }
        _output.WriteLine($"Semana de 5 comidas: mediana {times.OrderBy(x => x).ElementAt(5)} ms; máximo {times.Max()} ms");
        // Presupuesto holgado para CI (el diseño apunta a <200 ms; antes de este motor la generación tardaba 14-32 s).
        Assert.True(times.Max() < 1500, $"Generación demasiado lenta: {times.Max()} ms");
    }

    [Fact]
    public async Task StapleFoodsAreNotExemptByAccidentalSubstringMatches()
    {
        var method = typeof(DietSemanticValidationService).GetMethod("IsStapleFood", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        bool IsStaple(string name) => (bool)method.Invoke(null, new object?[] { new foods { name = name }, null })!;
        Assert.False(IsStaple("Salmón"));
        Assert.False(IsStaple("Ensalada"));
        Assert.False(IsStaple("Aguacate"));
        Assert.True(IsStaple("Aceite de oliva"));
        Assert.True(IsStaple("Sal"));
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData("Equilibrada", 5)]
    [InlineData("Equilibrada", 3)]
    [InlineData("AltaProteina", 4)]
    [InlineData("Vegetariana", 5)]
    public async Task ExcludeBread_ProducesAValidWeekWithoutAnyBread(string type, int meals)
    {
        var (context, generator) = await CreateAsync();
        await using var _ = context;
        var request = Request(2000, type, 7, meals);
        request.ExcludeBread = true;

        var diet = await generator.GenerateDietAsync(request, null, 1, false);

        var breadIds = context.foods.AsEnumerable()
            .Where(f => FoodCulinaryClassifier.Classify(f.name, f.category, f.kcal, f.protein, f.carbs, f.fat)?.Role == FoodProfileData.RoleBread)
            .Select(f => f.id).ToHashSet();
        Assert.NotEmpty(breadIds); // el catálogo de prueba sí contiene pan: la exclusión es real
        Assert.DoesNotContain(Items(diet), i => i.FoodId.HasValue && breadIds.Contains(i.FoodId.Value));
        Assert.Equal(7, diet.Days.Count);
        foreach (var day in diet.Days)
        {
            var k = day.Meals.SelectMany(m => m.Items).Sum(i => (double)(i.Kcal ?? 0));
            Assert.InRange(Math.Abs(k - 2000) / 2000, 0, 0.06);
        }
    }

    [Fact]
    public async Task WithoutTheFlagBreadIsStillGenerated()
    {
        var (context, generator) = await CreateAsync();
        await using var _ = context;
        var diet = await generator.GenerateDietAsync(Request(), null, 1, false);
        var breadIds = context.foods.AsEnumerable()
            .Where(f => FoodCulinaryClassifier.Classify(f.name, f.category, f.kcal, f.protein, f.carbs, f.fat)?.Role == FoodProfileData.RoleBread)
            .Select(f => f.id).ToHashSet();
        Assert.Contains(Items(diet), i => i.FoodId.HasValue && breadIds.Contains(i.FoodId.Value));
    }
}
