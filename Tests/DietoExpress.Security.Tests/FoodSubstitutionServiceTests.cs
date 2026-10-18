using Xunit;

using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace DietoExpress.Security.Tests;

public class FoodSubstitutionServiceTests
{
    private static angulosodbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new angulosodbContext(options);
    }

    [Fact]
    public async Task SuggestAsync_PrefersSameExchangeGroup()
    {
        await using var db = CreateContext();
        db.foods.AddRange(
            new foods { id = 1, name = "Fuente", kcal = 100, protein = 10, carbs = 10, fat = 2, exchange_group_id = 7, dietary_flags = Array.Empty<string>() },
            new foods { id = 2, name = "Misma equivalencia", kcal = 101, protein = 10, carbs = 10, fat = 2, exchange_group_id = 7, dietary_flags = Array.Empty<string>() },
            new foods { id = 3, name = "Otra equivalencia", kcal = 99, protein = 10, carbs = 10, fat = 2, exchange_group_id = 8, dietary_flags = Array.Empty<string>() });
        await db.SaveChangesAsync();

        var result = await new FoodSubstitutionService(db)
            .SuggestAsync(1, null, true, "balanced", null, 2);

        Assert.Equal(2, result[0].Id);
    }

    [Fact]
    public async Task SuggestAsync_ExcludesDietaryFlags()
    {
        await using var db = CreateContext();
        db.foods.AddRange(
            new foods { id = 1, name = "Fuente", kcal = 100, protein = 10, carbs = 10, fat = 2, dietary_flags = Array.Empty<string>() },
            new foods { id = 2, name = "Con gluten", kcal = 100, protein = 10, carbs = 10, fat = 2, dietary_flags = new[] { "gluten" } },
            new foods { id = 3, name = "Sin gluten", kcal = 101, protein = 10, carbs = 10, fat = 2, dietary_flags = new[] { "sin_gluten" } });
        await db.SaveChangesAsync();

        var result = await new FoodSubstitutionService(db)
            .SuggestAsync(1, null, true, "kcal", new[] { "gluten" }, 10);

        Assert.DoesNotContain(result, x => x.Id == 2);
        Assert.Contains(result, x => x.Id == 3);
    }

    [Fact]
    public async Task SuggestAsync_EnforcesTenantIsolationForLocalFoods()
    {
        await using var db = CreateContext();
        db.foods.AddRange(
            new foods { id = 1, name = "Fuente", kcal = 100, protein = 10, carbs = 10, fat = 2, source = "global", dietary_flags = Array.Empty<string>() },
            new foods { id = 2, name = "Otro tenant", kcal = 100, protein = 10, carbs = 10, fat = 2, source = "local", tenant_id = 2, dietary_flags = Array.Empty<string>() },
            new foods { id = 3, name = "Mi tenant", kcal = 101, protein = 10, carbs = 10, fat = 2, source = "local", tenant_id = 1, dietary_flags = Array.Empty<string>() });
        await db.SaveChangesAsync();

        var result = await new FoodSubstitutionService(db)
            .SuggestAsync(1, 1, false, "kcal", null, 10);

        Assert.DoesNotContain(result, x => x.Id == 2);
        Assert.Contains(result, x => x.Id == 3);
    }

    [Fact]
    public async Task SuggestAsync_ExcludesKnownAllergensAndTracesButKeepsUnknownMetadataCandidate()
    {
        await using var db = CreateContext();
        db.foods.AddRange(
            new foods { id = 1, name = "Fuente segura", kcal = 100, protein = 10, carbs = 10, fat = 2, source = "global", dietary_flags = Array.Empty<string>(), allergen_tags = Array.Empty<string>(), traces_tags = Array.Empty<string>(), allergen_data_status = "reported" },
            new foods { id = 2, name = "Candidato con alérgeno", kcal = 100, protein = 10, carbs = 10, fat = 2, source = "global", dietary_flags = Array.Empty<string>(), allergen_tags = new[] { "cacahuete" }, traces_tags = Array.Empty<string>(), allergen_data_status = "reported" },
            new foods { id = 3, name = "Candidato con trazas", kcal = 100, protein = 10, carbs = 10, fat = 2, source = "global", dietary_flags = Array.Empty<string>(), allergen_tags = Array.Empty<string>(), traces_tags = new[] { "leche" }, allergen_data_status = "reported" },
            new foods { id = 4, name = "Candidato sin datos verificables", kcal = 100, protein = 10, carbs = 10, fat = 2, source = "global", dietary_flags = Array.Empty<string>(), allergen_tags = Array.Empty<string>(), traces_tags = Array.Empty<string>(), allergen_data_status = "unknown" },
            new foods { id = 5, name = "Candidato seguro verificado", kcal = 101, protein = 10, carbs = 10, fat = 2, source = "global", dietary_flags = Array.Empty<string>(), allergen_tags = new[] { "huevo" }, traces_tags = Array.Empty<string>(), allergen_data_status = "reported" });
        await db.SaveChangesAsync();

        var result = await new FoodSubstitutionService(db)
            .SuggestAsync(1, null, true, "kcal", null, 10, default, "cacahuete, leche");

        Assert.DoesNotContain(result, x => x.Id == 2);
        Assert.DoesNotContain(result, x => x.Id == 3);
        Assert.Contains(result, x => x.Id == 4);
        Assert.Contains(result, x => x.Id == 5);
    }

    [Fact]
    public async Task SuggestAsync_RejectsUnknownTarget()
    {
        await using var db = CreateContext();
        db.foods.Add(new foods { id = 1, name = "Fuente", kcal = 100, dietary_flags = Array.Empty<string>() });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new FoodSubstitutionService(db).SuggestAsync(1, null, true, "unknown", null, 10));
    }
}
