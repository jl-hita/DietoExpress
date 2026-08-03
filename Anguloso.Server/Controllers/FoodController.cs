using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

//[Authorize] //TODO RECORDAR DESCOMENTAR EN PRODUCCIÓN
[ApiController]
[Route("api/[controller]")]
[Route("api/foods")]
public class FoodController : ControllerBase
{
    private readonly OpenFoodFactsService _openFood;
    private readonly angulosodbContext _dbContext;

    public FoodController(OpenFoodFactsService openFood, angulosodbContext dbContext)
    {
        _openFood = openFood;
        _dbContext = dbContext;
    }

    [HttpGet("barcode/{code}")]
    public async Task<IActionResult> GetByBarcode(string code)
    {
        var p = await _openFood.GetProductByBarcodeAsync(code);
        if (p == null) return NotFound();
        return Ok(p);
    }

    [HttpGet("search/{query}")]
    public async Task<IActionResult> Search(string query)
    {
        string? userName = User.Identity?.Name ?? null;
        /*
         * Este pedazo de código solo busca en OFF + USDA
         * 
        string pais = "spain";
        string lang = "es";

        if (userName != null)
        {
            users? user = null;
            try
            {
                user = _dbContext.users.AsNoTracking().Where(u => u.username == userName).FirstOrDefault();
            }
            catch (Exception) { }

            pais = user != null ? user.country : "spain";
            lang = user != null ? user.lang : "es";
        }

        var results = await _openFood.SearchProductsAsync(query, pais, lang);
        */

        //Esta línea busca en la BBDD local antes de buscar en OFF + USDA (hace las dos cosas en un mismo método)
        var results = await _openFood.SearchAsync(query, userName);

        return Ok(results);
    }

    // GET: api/foods/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetFoodById(int id)
    {
        var food = await _dbContext.foods.FindAsync(id);
        if (food == null) return NotFound();
        return Ok(food);
    }

    // POST: api/foods
    [HttpPost]
    [Authorize] // Solo nutricionistas logueados pueden crear alimentos
    public async Task<IActionResult> CreateCustomFood([FromBody] CustomFoodDto dto)
    {
        if (dto == null) return BadRequest("Los datos del alimento son requeridos.");
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("El nombre del alimento es requerido.");

        var food = new foods
        {
            name = dto.Name,
            brands = dto.Brands,
            category = dto.Category,
            nutriscore = dto.Nutriscore,
            kcal = dto.Kcal,
            protein = dto.Protein,
            carbs = dto.Carbs,
            fat = dto.Fat,
            saturated_fat = dto.SaturatedFat,
            fiber = dto.Fiber,
            sugar = dto.Sugar,
            salt = dto.Salt,
            serving_size = dto.ServingSize,
            serving_size_unit = dto.ServingSizeUnit,
            serving_size_text = dto.ServingSizeText,
            default_grams = dto.DefaultGrams ?? 100,
            source = dto.Source ?? "local",
            exchange_group_id = dto.ExchangeGroupId,
            grams_per_exchange = dto.GramsPerExchange,
            created_at = DateTime.UtcNow,
            last_synced_at = DateTime.UtcNow
        };

        _dbContext.foods.Add(food);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetFoodById), new { id = food.id }, food);
    }

    // PUT: api/foods/{id}
    [HttpPut("{id:int}")]
    [Authorize]
    public async Task<IActionResult> UpdateCustomFood(int id, [FromBody] CustomFoodDto dto)
    {
        if (dto == null) return BadRequest("Los datos del alimento son requeridos.");
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("El nombre del alimento es requerido.");

        var food = await _dbContext.foods.FindAsync(id);
        if (food == null) return NotFound();

        // Actualizar campos
        food.name = dto.Name;
        food.brands = dto.Brands;
        food.category = dto.Category;
        food.nutriscore = dto.Nutriscore;
        food.kcal = dto.Kcal;
        food.protein = dto.Protein;
        food.carbs = dto.Carbs;
        food.fat = dto.Fat;
        food.saturated_fat = dto.SaturatedFat;
        food.fiber = dto.Fiber;
        food.sugar = dto.Sugar;
        food.salt = dto.Salt;
        food.serving_size = dto.ServingSize;
        food.serving_size_unit = dto.ServingSizeUnit;
        food.serving_size_text = dto.ServingSizeText;
        food.default_grams = dto.DefaultGrams ?? food.default_grams;
        food.source = dto.Source ?? food.source ?? "local";
        food.exchange_group_id = dto.ExchangeGroupId;
        food.grams_per_exchange = dto.GramsPerExchange;
        food.last_synced_at = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return NoContent();
    }

    // DELETE: api/foods/{id}
    [HttpDelete("{id:int}")]
    [Authorize]
    public async Task<IActionResult> DeleteCustomFood(int id)
    {
        var food = await _dbContext.foods.FindAsync(id);
        if (food == null) return NotFound();

        // Evitar eliminar alimentos usados en dietas
        bool isUsed = await _dbContext.meal_items.AnyAsync(m => m.food_id == id);
        if (isUsed)
        {
            return BadRequest("No se puede eliminar el alimento porque está siendo utilizado en una o más dietas.");
        }

        _dbContext.foods.Remove(food);
        await _dbContext.SaveChangesAsync();

        return NoContent();
    }
}