using Xunit;
using System.Security.Claims;
using Anguloso.Server.Controllers;
using Anguloso.Server.Model;
using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Microsoft.Extensions.DependencyInjection;

namespace DietoExpress.Security.Tests;

public class TenantIsolationTests
{
    // Aquí sí se ejecutan los controladores contra una base InMemory: los datos de dos tenants permiten comprobar que un identificador válido por sí solo nunca basta para cruzar el límite.
    [Fact]
    public async Task AssignDiet_DoesNotAllowDietFromAnotherTenant()
    {
        await using var db = CreateDb();

        db.users.AddRange(
            new users { id = 1, tenant_id = 10, role = "nutritionist" },
            new users { id = 2, tenant_id = 20, role = "nutritionist" });

        db.clients.Add(new clients
        {
            id = 100,
            user_id = 1,
            tenant_id = 10,
            full_name = "Cliente A",
            archived_at = null
        });

        db.diets.AddRange(
            new diets { id = 200, user_id = 1, tenant_id = 10, name = "Dieta A", archived_at = null },
            new diets { id = 300, user_id = 2, tenant_id = 20, name = "Dieta B", archived_at = null });

        await db.SaveChangesAsync();

        var controller = CreateController(db, userId: 1, tenantId: 10, sharedDiets: false);

        var result = await controller.AssignDiet(
            100,
            new AssignDietDto
            {
                DietId = 300,
                StartDate = new DateTime(2026, 10, 1)
            });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("The selected diet does not exist.", badRequest.Value);
        Assert.Empty(await db.client_diets.ToListAsync());
    }

    [Fact]
    public async Task GetActiveDiet_DoesNotReturnCrossTenantDietEvenIfAssignmentIsCorrupted()
    {
        await using var db = CreateDb();

        db.users.AddRange(
            new users { id = 1, tenant_id = 10, role = "nutritionist" },
            new users { id = 2, tenant_id = 20, role = "nutritionist" });

        db.clients.Add(new clients
        {
            id = 100,
            user_id = 1,
            tenant_id = 10,
            full_name = "Cliente A",
            archived_at = null
        });

        db.diets.Add(new diets
        {
            id = 300,
            user_id = 2,
            tenant_id = 20,
            name = "Dieta B",
            archived_at = null
        });

        db.client_diets.Add(new client_diets
        {
            id = 400,
            client_id = 100,
            diet_id = 300,
            start_date = new DateOnly(2026, 10, 1),
            is_active = true
        });

        await db.SaveChangesAsync();

        var controller = CreateController(db, userId: 1, tenantId: 10, sharedDiets: false);

        var result = await controller.GetActiveDiet(100);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task DeactivateAssignment_DoesNotModifyAssignmentToCrossTenantDiet()
    {
        await using var db = CreateDb();

        db.users.AddRange(
            new users { id = 1, tenant_id = 10, role = "nutritionist" },
            new users { id = 2, tenant_id = 20, role = "nutritionist" });

        db.clients.Add(new clients
        {
            id = 100,
            user_id = 1,
            tenant_id = 10,
            full_name = "Cliente A",
            archived_at = null
        });

        db.diets.Add(new diets
        {
            id = 300,
            user_id = 2,
            tenant_id = 20,
            name = "Dieta B",
            archived_at = null
        });

        db.client_diets.Add(new client_diets
        {
            id = 400,
            client_id = 100,
            diet_id = 300,
            start_date = new DateOnly(2026, 10, 1),
            is_active = true
        });

        await db.SaveChangesAsync();

        var controller = CreateController(db, userId: 1, tenantId: 10, sharedDiets: false);

        var result = await controller.DeactivateAssignment(100, 400);

        Assert.IsType<NotFoundObjectResult>(result);
        var assignment = await db.client_diets.SingleAsync();
        Assert.True(assignment.is_active);
    }

    [Fact]
    public async Task DeleteAssignment_DoesNotDeleteAssignmentToCrossTenantDiet()
    {
        await using var db = CreateDb();

        db.users.AddRange(
            new users { id = 1, tenant_id = 10, role = "nutritionist" },
            new users { id = 2, tenant_id = 20, role = "nutritionist" });

        db.clients.Add(new clients
        {
            id = 100,
            user_id = 1,
            tenant_id = 10,
            full_name = "Cliente A",
            archived_at = null
        });

        db.diets.Add(new diets
        {
            id = 300,
            user_id = 2,
            tenant_id = 20,
            name = "Dieta B",
            archived_at = null
        });

        db.client_diets.Add(new client_diets
        {
            id = 400,
            client_id = 100,
            diet_id = 300,
            start_date = new DateOnly(2026, 10, 1),
            is_active = true
        });

        await db.SaveChangesAsync();

        var controller = CreateController(db, userId: 1, tenantId: 10, sharedDiets: false);

        var result = await controller.DeleteAssignment(100, 400);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Single(await db.client_diets.ToListAsync());
    }

    [Fact]
    public async Task UpdateAssignment_DoesNotModifyAssignmentToCrossTenantDiet()
    {
        await using var db = CreateDb();

        db.users.AddRange(
            new users { id = 1, tenant_id = 10, role = "nutritionist" },
            new users { id = 2, tenant_id = 20, role = "nutritionist" });

        db.clients.Add(new clients
        {
            id = 100,
            user_id = 1,
            tenant_id = 10,
            full_name = "Cliente A",
            archived_at = null
        });

        db.diets.Add(new diets
        {
            id = 300,
            user_id = 2,
            tenant_id = 20,
            name = "Dieta B",
            archived_at = null
        });

        db.client_diets.Add(new client_diets
        {
            id = 400,
            client_id = 100,
            diet_id = 300,
            start_date = new DateOnly(2026, 10, 1),
            is_active = true
        });

        await db.SaveChangesAsync();

        var controller = CreateController(db, userId: 1, tenantId: 10, sharedDiets: false);

        var result = await controller.UpdateAssignment(
            100,
            400,
            new UpdateClientDietDto
            {
                StartDate = new DateTime(2026, 10, 2),
                IsActive = false,
                Notes = "Intento cross-tenant"
            });

        Assert.IsType<NotFoundObjectResult>(result);
        var assignment = await db.client_diets.SingleAsync();
        Assert.True(assignment.is_active);
        Assert.Equal(new DateOnly(2026, 10, 1), assignment.start_date);
        Assert.NotEqual("Intento cross-tenant", assignment.notes);
    }

    [Fact]
    public async Task AssignDiet_AllowsOwnDietForOwnedClient()
    {
        await using var db = CreateDb();

        db.users.Add(new users { id = 1, tenant_id = 10, role = "nutritionist" });
        db.clients.Add(new clients
        {
            id = 100,
            user_id = 1,
            tenant_id = 10,
            full_name = "Cliente A",
            archived_at = null
        });
        db.diets.Add(new diets
        {
            id = 200,
            user_id = 1,
            tenant_id = 10,
            name = "Dieta A",
            archived_at = null
        });

        await db.SaveChangesAsync();

        var controller = CreateController(db, userId: 1, tenantId: 10, sharedDiets: false);

        var result = await controller.AssignDiet(
            100,
            new AssignDietDto
            {
                DietId = 200,
                StartDate = new DateTime(2026, 10, 1)
            });

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Single(await db.client_diets.ToListAsync());
    }

    [Fact]
    public async Task RemoveFavorite_DoesNotModifyFavoriteToAnotherTenantFood()
    {
        await using var db = CreateDb();

        db.users.Add(new users { id = 1, tenant_id = 10, role = "nutritionist" });
        db.foods.Add(new foods
        {
            id = 900,
            name = "Alimento de otro tenant",
            source = "local",
            tenant_id = 20,
            created_by_user_id = 2
        });
        db.food_favorites.Add(new food_favorites
        {
            id = 901,
            user_id = 1,
            food_id = 900
        });
        await db.SaveChangesAsync();

        var controller = CreateFoodController(db, userId: 1, tenantId: 10);

        var result = await controller.RemoveFavorite(900);

        Assert.IsType<NoContentResult>(result);
        Assert.Single(await db.food_favorites.ToListAsync());
    }

    // Los controladores se construyen con claims y RequestServices equivalentes a una petición autenticada para que las pruebas ejerciten la autorización real del endpoint.
    private static FoodController CreateFoodController(
        angulosodbContext db,
        int userId,
        int tenantId)
    {
        var controller = new FoodController(null!, db);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("tenantId", tenantId.ToString()),
            new(ClaimTypes.Role, "user")
        };

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
        httpContext.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    // Cada prueba recibe una base aislada para impedir que datos de una regresión contaminen otra.
    private static angulosodbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new angulosodbContext(options);
    }

    // Se inyecta una licencia simulada para poder probar por separado el acceso a dietas compartidas sin introducir Stripe ni configuración externa en el test.
    private static ClientDietsController CreateController(
        angulosodbContext db,
        int userId,
        int tenantId,
        bool sharedDiets)
    {
        var license = new Mock<ILicenseService>();
        license
            .Setup(x => x.CanUseFeatureAsync(It.IsAny<int?>(), "SHARED_DIETS"))
            .ReturnsAsync(sharedDiets);

        var controller = new ClientDietsController(
            db,
            null!,
            null!,
            license.Object,
            null!);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("tenantId", tenantId.ToString()),
            new(ClaimTypes.Role, "user")
        };

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
        httpContext.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }
}
