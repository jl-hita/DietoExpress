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
[Authorize]
[Route("api/[controller]")]
[Route("api/recipes")]
public class RecipesController : ControllerBase
{
    private readonly angulosodbContext _context;

    public RecipesController(angulosodbContext context)
    {
        _context = context;
    }

    // GET: api/recipes
    [HttpGet]
    public async Task<ActionResult<List<RecipeListDto>>> GetRecipes()
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var list = await _context.recipes
            .Where(r => r.user_id == userId.Value)
            .OrderByDescending(r => r.created_at)
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

        var recipe = await _context.recipes
            .Include(r => r.recipe_items)
                .ThenInclude(ri => ri.food)
            .FirstOrDefaultAsync(r => r.id == id && r.user_id == userId.Value);

        if (recipe == null) return NotFound();

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
                    FoodName = ri.food?.name ?? "Alimento desconocido",
                    Brands = ri.food?.brands,
                    Grams = ri.grams,
                    Kcal = ri.food?.kcal.HasValue == true ? (double?)Math.Round(ri.food.kcal.Value * factor, 2) : null,
                    Protein = ri.food?.protein.HasValue == true ? (double?)Math.Round(ri.food.protein.Value * factor, 2) : null,
                    Carbs = ri.food?.carbs.HasValue == true ? (double?)Math.Round(ri.food.carbs.Value * factor, 2) : null,
                    Fat = ri.food?.fat.HasValue == true ? (double?)Math.Round(ri.food.fat.Value * factor, 2) : null
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

        if (dto == null) return BadRequest("Los datos de la receta son requeridos.");
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("El nombre de la receta es requerido.");
        if (dto.Ingredients == null || !dto.Ingredients.Any()) return BadRequest("La receta debe contener al menos un ingrediente.");

        var recipe = new recipes
        {
            user_id = userId.Value,
            name = dto.Name,
            instructions = dto.Instructions ?? "",
            created_at = DateTime.UtcNow
        };

        foreach (var ingDto in dto.Ingredients)
        {
            // Validar que el alimento exista
            var foodExists = await _context.foods.AnyAsync(f => f.id == ingDto.FoodId);
            if (!foodExists)
            {
                return BadRequest($"El alimento con ID {ingDto.FoodId} no existe en el catálogo.");
            }

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

        if (dto == null) return BadRequest("Los datos de la receta son requeridos.");
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("El nombre de la receta es requerido.");
        if (dto.Ingredients == null || !dto.Ingredients.Any()) return BadRequest("La receta debe contener al menos un ingrediente.");

        var recipe = await _context.recipes
            .Include(r => r.recipe_items)
            .FirstOrDefaultAsync(r => r.id == id && r.user_id == userId.Value);

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
            var foodExists = await _context.foods.AnyAsync(f => f.id == ingDto.FoodId);
            if (!foodExists)
            {
                return BadRequest($"El alimento con ID {ingDto.FoodId} no existe en el catálogo.");
            }

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

        var recipe = await _context.recipes
            .Include(r => r.recipe_items)
            .FirstOrDefaultAsync(r => r.id == id && r.user_id == userId.Value);

        if (recipe == null) return NotFound();

        _context.recipe_items.RemoveRange(recipe.recipe_items);
        _context.recipes.Remove(recipe);

        await _context.SaveChangesAsync();
        return NoContent();
    }
}
