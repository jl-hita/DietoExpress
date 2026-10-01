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

namespace DietoExpress.Security.Tests;

public class TenantIsolationTests
{
    [Fact]
    public async Task AssignDiet_DoesNotAllowDietFromAnotherTenant()
    {
        await using var db = CreateDb();

        db.users.AddRange(
            new users { id = 1, tenant_id = 10, role = "user" },
            new users { id = 2, tenant_id = 20, role = "user" });

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
            new users { id = 1, tenant_id = 10, role = "user" },
            new users { id = 2, tenant_id = 20, role = "user" });

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
            new users { id = 1, tenant_id = 10, role = "user" },
            new users { id = 2, tenant_id = 20, role = "user" });

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
            new users { id = 1, tenant_id = 10, role = "user" },
            new users { id = 2, tenant_id = 20, role = "user" });

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
            new users { id = 1, tenant_id = 10, role = "user" },
            new users { id = 2, tenant_id = 20, role = "user" });

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
    public async Task AssignDiet_DoesNotDeactivateCorruptedCrossTenantActiveAssignment()
    {
        await using var db = CreateDb();

        db.users.AddRange(
            new users { id = 1, tenant_id = 10, role = "user" },
            new users { id = 2, tenant_id = 20, role = "user" });

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

        db.client_diets.Add(new client_diets
        {
            id = 400,
            client_id = 100,
            diet_id = 300,
            start_date = new DateOnly(2026, 9, 1),
            is_active = true
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

        var assignments = await db.client_diets
            .OrderBy(x => x.id)
            .ToListAsync();

        Assert.Equal(2, assignments.Count);
        Assert.True(assignments[0].is_active);
        Assert.Equal(300, assignments[0].diet_id);
        Assert.True(assignments[1].is_active);
        Assert.Equal(200, assignments[1].diet_id);
    }

    [Fact]
    public async Task AssignDiet_AllowsOwnDietForOwnedClient()
    {
        await using var db = CreateDb();

        db.users.Add(new users { id = 1, tenant_id = 10, role = "user" });
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

    private static angulosodbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<angulosodbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new angulosodbContext(options);
    }

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
            license.Object);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("tenantId", tenantId.ToString()),
            new(ClaimTypes.Role, "user")
        };

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };

        return controller;
    }
}
