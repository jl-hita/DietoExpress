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
// Las recetas pueden combinar ingredientes propios y compartidos; este alcance evita exponer datos entre tenants.
public class RecipesController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly ILicenseService _licenseService;
    private readonly RecipeNutritionService _recipeNutritionService;

    public RecipesController(angulosodbContext context, ILicenseService licenseService, RecipeNutritionService recipeNutritionService)
    {
        _context = context;
        _licenseService = licenseService;
        _recipeNutritionService = recipeNutritionService;
    }

    // Centraliza la regla de acceso a alimentos reutilizada al crear y modificar recetas.
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
            .Where(r => tenantId.HasValue && r.tenant_id == tenantId.Value && r.user_id == userId.Value)
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
    // Primero se valida la receta y después se filtran de nuevo sus ingredientes para no exponer alimentos locales ajenos.
    public async Task<ActionResult<RecipeDetailDto>> GetRecipe(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        if (!await _licenseService.CanUseFeatureAsync(AuthHelpers.GetTenantId(User), "RECIPES")) return Forbid();

        var tenantId = AuthHelpers.GetTenantId(User);
        var recipe = await _context.recipes
            .Include(r => r.recipe_items)
            .FirstOrDefaultAsync(r => r.id == id &&
                tenantId.HasValue && r.tenant_id == tenantId.Value && r.user_id == userId.Value);

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

        var dto = await _recipeNutritionService.BuildDetailAsync(recipe, accessibleFoods, HttpContext.RequestAborted);

        return Ok(dto);
    }

    // POST: api/recipes
    [HttpPost]
    // La validación de cada ingrediente se hace contra el tenant actual antes de persistir la receta.
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
            servings = dto.Servings <= 0 ? 1 : dto.Servings,
            yield_grams = dto.YieldGrams,
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
        recipe.servings = dto.Servings <= 0 ? 1 : dto.Servings;
        recipe.yield_grams = dto.YieldGrams;

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

    // Sustituye un ingrediente y recalcula automáticamente nutrición, restricciones y usos en dietas.
    [HttpPost("{id:int}/substitute")]
    public async Task<ActionResult<RecipeDetailDto>> SubstituteIngredient(
        int id,
        [FromBody] SubstituteRecipeIngredientDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!await _licenseService.CanUseFeatureAsync(tenantId, "RECIPES")) return Forbid();

        var recipe = await _context.recipes
            .Include(r => r.recipe_items).ThenInclude(i => i.food)
            .FirstOrDefaultAsync(r => r.id == id && tenantId.HasValue &&
                                       r.tenant_id == tenantId.Value && r.user_id == userId.Value);
        if (recipe == null) return NotFound();

        var ingredient = recipe.recipe_items.FirstOrDefault(i => i.food_id == dto.IngredientFoodId);
        if (ingredient == null) return NotFound("El ingrediente no forma parte de la receta.");
        if (!await CanUseFoodAsync(dto.ReplacementFoodId, userId.Value, tenantId))
            return BadRequest("El alimento sustituto no está disponible para esta cuenta.");

        var replacement = await _context.foods.AsNoTracking().FirstOrDefaultAsync(f => f.id == dto.ReplacementFoodId);
        if (replacement == null) return NotFound("Alimento sustituto no encontrado.");

        var originalFood = ingredient.food;
        var grams = ingredient.grams;
        if (dto.PreserveCalories && originalFood?.kcal > 0 && replacement.kcal > 0)
        {
            var adjusted = (double)grams * originalFood.kcal.Value / replacement.kcal.Value;
            grams = (decimal)Math.Clamp(adjusted, 0.1, 10000);
        }

        ingredient.food_id = replacement.id;
        ingredient.grams = Math.Round(grams, 2);
        await _context.SaveChangesAsync();
        await _recipeNutritionService.RefreshRecipeUsagesAsync(recipe.id, HttpContext.RequestAborted);

        var refreshed = await _context.recipes
            .Include(r => r.recipe_items).ThenInclude(i => i.food)
            .FirstAsync(r => r.id == recipe.id);
        var ids = refreshed.recipe_items.Select(i => i.food_id).Distinct().ToList();
        var foods = await _context.foods.Where(f => ids.Contains(f.id)).ToDictionaryAsync(f => f.id);
        return Ok(await _recipeNutritionService.BuildDetailAsync(refreshed, foods, HttpContext.RequestAborted));
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
