using System.Text.Json;
using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DietoExpress.Security.Tests;

public class AuditFixesBehaviorTests
{
    private static angulosodbContext CreateContext() => new(new DbContextOptionsBuilder<angulosodbContext>()
        .UseInMemoryDatabase($"audit-fixes-{Guid.NewGuid():N}")
        .Options);

    [Fact]
    public void PrivacyExportRecords_SerializeEntitiesWithBackReferencesWithoutLeakingSecrets()
    {
        var client = new clients { id = 1, full_name = "Paciente", passcode_hash = "secreto-pin", access_token = "secreto-enlace" };
        var measurement = new biometrics { id = 3, client_id = 1, weight = 70, client = client };
        client.biometrics.Add(measurement);

        var json = JsonSerializer.Serialize(PrivacyExportRecords.From(client.biometrics));

        Assert.Contains("\"weight\":70", json);
        Assert.DoesNotContain("secreto", json);
        Assert.Null(PrivacyExportRecords.From(null));
    }

    [Fact]
    public async Task DietHistoryMove_CopiesThePlanToTheNewClinicAndClosesTheAssignment()
    {
        await using var context = CreateContext();
        var diet = new diets { id = 10, name = "Plan anterior", notes = "", user_id = 1, tenant_id = 1 };
        var day = new diet_days { day_index = 0 };
        var meal = new meals { name = "Comida", meal_index = 0 };
        meal.meal_items.Add(new meal_items { food_id = 5, grams = 100 });
        day.meals.Add(meal);
        diet.diet_days.Add(day);
        context.diets.Add(diet);
        context.client_diets.Add(new client_diets { id = 20, client_id = 7, diet_id = 10, is_active = true, notes = "", start_date = new DateOnly(2026, 1, 1) });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await DietSnapshots.MoveHistoryAsync(context, 7, sourceTenantId: 1, targetTenantId: 2, targetUserId: 9);
        await DietSnapshots.MoveHistoryAsync(context, 7, sourceTenantId: 1, targetTenantId: 2, targetUserId: 9);

        context.ChangeTracker.Clear();
        var assignment = await context.client_diets.SingleAsync();
        var copy = await context.diets.Include(d => d.diet_days).ThenInclude(d => d.meals).ThenInclude(m => m.meal_items)
            .SingleAsync(d => d.id == assignment.diet_id);
        Assert.NotEqual(10, copy.id);
        Assert.Equal(2, copy.tenant_id);
        Assert.NotNull(copy.archived_at);
        Assert.False(assignment.is_active);
        Assert.NotNull(assignment.end_date);
        Assert.Equal(100, copy.diet_days.Single().meals.Single().meal_items.Single().grams);
        // La original sigue intacta en su clínica y repetir el traslado no crea más copias.
        Assert.Equal(2, await context.diets.CountAsync());
    }
}
