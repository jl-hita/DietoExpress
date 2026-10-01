using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Anguloso.Server.Controllers;

[ApiController]
[Authorize(Policy = "Professional")]
[Route("api/[controller]")]
[Route("api/recipes")]
public class RecipesController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly ILicenseService _licenseService;

    public RecipesController(angulosodbContext context, ILicenseService licenseService)
    {
        _context = context;
        _licenseService = licenseService;
    }

    private async Task<bool> CanUseFoodAsync(int foodId, int userId, int? tenantId)
    {
        return await _context.foods.AnyAsync(f =>
            f.id == foodId &&
            ((f.source == null || f.source.ToLower() != "local") ||
             User.IsInRole("superadmin") ||
             (tenantId.HasValue && f.tenant_id == tenantId.Value && f.created_by_user_id == userId) ||
             (User.IsInRole("clinic_admin") && tenantId.HasValue && f.tenant_id == tenantId.Value)));
    }

    // GET: api/recipes
    [HttpGet]
    public async Task<ActionResult<List<RecipeListDto>>> GetRecipes()
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        if (!await _licenseService.CanUseFeatureAsync(AuthHelpers.GetTenantId(User), "RECIPES")) return Forbid();

        var tenantId = AuthHelpers.GetTenantId(User);
        var list = await _context.recipes
            .Where(r => tenantId.HasValue && r.tenant_id == tenantId.Value)
            .OrderByDescending(r => r.created_at)
            .Take(500)
            .Select(r => new RecipeListDto
            {
                Id = r.id,
                Name = r.name,
                Instructions = r.instructions,
                CreatedAt = r.created_at
            })
            .ToListAsync();

        return Ok(list);
    }

    // GET: api/recipes/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<RecipeDetailDto>> GetRecipe(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        if (!await _licenseService.CanUseFeatureAsync(AuthHelpers.GetTenantId(User), "RECIPES")) return Forbid();

        var tenantId = AuthHelpers.GetTenantId(User);
        var recipe = await _context.recipes
            .Include(r => r.recipe_items)
            .FirstOrDefaultAsync(r => r.id == id &&
                tenantId.HasValue && r.tenant_id == tenantId.Value);

        if (recipe == null) return NotFound();

        var foodIds = recipe.recipe_items
            .Select(ri => ri.food_id)
            .Distinct()
            .ToList();

        var accessibleFoods = await _context.foods
            .Where(f => foodIds.Contains(f.id) &&
                ((f.source == null || f.source.ToLower() != "local") ||
                 User.IsInRole("superadmin") ||
                 (tenantId.HasValue && f.tenant_id == tenantId.Value && f.created_by_user_id == userId.Value) ||
                 (User.IsInRole("clinic_admin") && tenantId.HasValue && f.tenant_id == tenantId.Value)))
            .ToDictionaryAsync(f => f.id);

        var dto = new RecipeDetailDto
        {
            Id = recipe.id,
            Name = recipe.name,
            Instructions = recipe.instructions,
            CreatedAt = recipe.created_at,
            Ingredients = recipe.recipe_items.Select(ri => {
                // Cálculo proporcional a los gramos (macros están almacenados por 100g)
                double factor = (double)ri.grams / 100.0;
                return new RecipeIngredientDto
                {
                    FoodId = ri.food_id,
                    FoodName = accessibleFoods.TryGetValue(ri.food_id, out var accessibleFood) ? accessibleFood.name : "Alimento no disponible",
                    Brands = accessibleFood?.brands,
                    Grams = ri.grams,
                    Kcal = accessibleFood?.kcal.HasValue == true ? (double?)Math.Round(accessibleFood.kcal.Value * factor, 2) : null,
                    Protein = accessibleFood?.protein.HasValue == true ? (double?)Math.Round(accessibleFood.protein.Value * factor, 2) : null,
                    Carbs = accessibleFood?.carbs.HasValue == true ? (double?)Math.Round(accessibleFood.carbs.Value * factor, 2) : null,
                    Fat = accessibleFood?.fat.HasValue == true ? (double?)Math.Round(accessibleFood.fat.Value * factor, 2) : null
                };
            }).ToList()
        };

        return Ok(dto);
    }

    // POST: api/recipes
    [HttpPost]
    public async Task<ActionResult<RecipeListDto>> CreateRecipe([FromBody] CreateRecipeDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        if (!await _licenseService.CanUseFeatureAsync(AuthHelpers.GetTenantId(User), "RECIPES")) return Forbid();

        if (dto == null) return BadRequest("Los datos de la receta son requeridos.");
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("El nombre de la receta es requerido.");
        if (dto.Ingredients == null || !dto.Ingredients.Any()) return BadRequest("La receta debe contener al menos un ingrediente.");
        if (dto.Ingredients.Count > 100) return BadRequest("La receta no puede contener más de 100 ingredientes.");
        if (dto.Instructions?.Length > 10000) return BadRequest("Las instrucciones no pueden superar los 10000 caracteres.");

        var recipe = new recipes
        {
            user_id = userId.Value,
            tenant_id = AuthHelpers.GetTenantId(User),
            name = dto.Name,
            instructions = dto.Instructions ?? "",
            created_at = DateTime.UtcNow
        };

        foreach (var ingDto in dto.Ingredients)
        {
            // Validar que el alimento exista
            if (!await CanUseFoodAsync(ingDto.FoodId, userId.Value, AuthHelpers.GetTenantId(User)))
                return BadRequest($"El alimento con ID {ingDto.FoodId} no está disponible para esta cuenta.");

            recipe.recipe_items.Add(new recipe_items
            {
                food_id = ingDto.FoodId,
                grams = ingDto.Grams
            });
        }

        _context.recipes.Add(recipe);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetRecipe), new { id = recipe.id }, new RecipeListDto
        {
            Id = recipe.id,
            Name = recipe.name,
            Instructions = recipe.instructions,
            CreatedAt = recipe.created_at
        });
    }

    // PUT: api/recipes/5
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateRecipe(int id, [FromBody] CreateRecipeDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        if (!await _licenseService.CanUseFeatureAsync(AuthHelpers.GetTenantId(User), "RECIPES")) return Forbid();

        if (dto == null) return BadRequest("Los datos de la receta son requeridos.");
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("El nombre de la receta es requerido.");
        if (dto.Ingredients == null || !dto.Ingredients.Any()) return BadRequest("La receta debe contener al menos un ingrediente.");
        if (dto.Ingredients.Count > 100) return BadRequest("La receta no puede contener más de 100 ingredientes.");
        if (dto.Instructions?.Length > 10000) return BadRequest("Las instrucciones no pueden superar los 10000 caracteres.");

        var tenantId = AuthHelpers.GetTenantId(User);
        var recipe = await _context.recipes
            .Include(r => r.recipe_items)
            .FirstOrDefaultAsync(r => r.id == id &&
                tenantId.HasValue && r.tenant_id == tenantId.Value &&
                r.user_id == userId.Value);

        if (recipe == null) return NotFound();

        // Actualizar campos
        recipe.name = dto.Name;
        recipe.instructions = dto.Instructions ?? "";

        // Limpiar ingredientes antiguos
        _context.recipe_items.RemoveRange(recipe.recipe_items);
        recipe.recipe_items.Clear();

        // Agregar nuevos
        foreach (var ingDto in dto.Ingredients)
        {
            if (!await CanUseFoodAsync(ingDto.FoodId, userId.Value, AuthHelpers.GetTenantId(User)))
                return BadRequest($"El alimento con ID {ingDto.FoodId} no está disponible para esta cuenta.");

            recipe.recipe_items.Add(new recipe_items
            {
                food_id = ingDto.FoodId,
                grams = ingDto.Grams
            });
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    // DELETE: api/recipes/5
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteRecipe(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        if (!await _licenseService.CanUseFeatureAsync(AuthHelpers.GetTenantId(User), "RECIPES")) return Forbid();

        var tenantId = AuthHelpers.GetTenantId(User);
        var recipe = await _context.recipes
            .Include(r => r.recipe_items)
            .FirstOrDefaultAsync(r => r.id == id &&
                tenantId.HasValue && r.tenant_id == tenantId.Value &&
                r.user_id == userId.Value);

        if (recipe == null) return NotFound();

        _context.recipe_items.RemoveRange(recipe.recipe_items);
        _context.recipes.Remove(recipe);

        await _context.SaveChangesAsync();
        return NoContent();
    }
}
