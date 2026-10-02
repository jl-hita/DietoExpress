using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Anguloso.Server.Logica.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace Anguloso.Server.Controllers;

[Authorize(Policy = "Professional")]
[ApiController]
[Route("api/[controller]")]
[Route("api/foods")]
// La visibilidad del alimento depende de su origen y del tenant actual; estos filtros forman parte de la autorización funcional.
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
    [EnableRateLimiting("expensive")]
    public async Task<IActionResult> GetByBarcode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || !System.Text.RegularExpressions.Regex.IsMatch(code, @"^\d{8,14}$"))
            return BadRequest("El código de barras no es válido.");

        var p = await _openFood.GetProductByBarcodeAsync(code);
        if (p == null) return NotFound();
        return Ok(p);
    }

    [HttpGet("search/{query}")]
    [EnableRateLimiting("expensive")]
    public async Task<IActionResult> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 100)
            return BadRequest("La búsqueda debe contener entre 1 y 100 caracteres.");

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
    // Los alimentos globales son visibles para todos; los locales solo para su creador/clinica.
    public async Task<IActionResult> GetFoodById(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        var tenantId = AuthHelpers.GetTenantId(User);
        var food = await _dbContext.foods.FirstOrDefaultAsync(f =>
            f.id == id &&
            ((f.source == null || f.source.ToLower() != "local") ||
             User.IsInRole("superadmin") ||
             (userId.HasValue && tenantId.HasValue && f.tenant_id == tenantId.Value && f.created_by_user_id == userId.Value) ||
             (User.IsInRole("clinic_admin") && tenantId.HasValue && f.tenant_id == tenantId.Value)));
        if (food == null) return NotFound();
        return Ok(food);
    }

    // POST: api/foods
    [HttpPost]
    [Authorize(Policy = "Professional")] // Solo profesionales logueados pueden crear alimentos
    // El tenant y el usuario se obtienen siempre de los claims, nunca del payload, para evitar suplantación.
    public async Task<IActionResult> CreateCustomFood([FromBody] CustomFoodDto dto)
    {
        if (dto == null) return BadRequest("Los datos del alimento son requeridos.");
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("El nombre del alimento es requerido.");

        var tenantId = AuthHelpers.GetTenantId(User);
        var userId = AuthHelpers.GetUserId(User);
        if (!User.IsInRole("superadmin") && !tenantId.HasValue)
            return BadRequest("La cuenta profesional no tiene una clínica asociada.");
        if (!userId.HasValue) return Unauthorized();

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
            source = "local",
            tenant_id = tenantId,
            created_by_user_id = userId,
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
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> UpdateCustomFood(int id, [FromBody] CustomFoodDto dto)
    {
        if (dto == null) return BadRequest("Los datos del alimento son requeridos.");
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("El nombre del alimento es requerido.");

        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var tenantId = AuthHelpers.GetTenantId(User);
        var food = await _dbContext.foods.FirstOrDefaultAsync(f =>
            f.id == id &&
            (f.source != null && f.source.ToLower() == "local") &&
            (User.IsInRole("superadmin") ||
             (tenantId.HasValue && f.tenant_id == tenantId.Value && f.created_by_user_id == userId.Value) ||
             (User.IsInRole("clinic_admin") && tenantId.HasValue && f.tenant_id == tenantId.Value)));
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
        // El origen de un alimento personalizado no puede ser falsificado por el cliente.
        food.source = "local";
        food.exchange_group_id = dto.ExchangeGroupId;
        food.grams_per_exchange = dto.GramsPerExchange;
        food.last_synced_at = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return NoContent();
    }

    // DELETE: api/foods/{id}
    [HttpDelete("{id:int}")]
    [Authorize(Policy = "Professional")]
    // Antes de borrar se comprueba si el alimento está referenciado por una dieta para no romper datos existentes.
    public async Task<IActionResult> DeleteCustomFood(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var tenantId = AuthHelpers.GetTenantId(User);
        var food = await _dbContext.foods.FirstOrDefaultAsync(f =>
            f.id == id &&
            (f.source != null && f.source.ToLower() == "local") &&
            (User.IsInRole("superadmin") ||
             (tenantId.HasValue && f.tenant_id == tenantId.Value && f.created_by_user_id == userId.Value) ||
             (User.IsInRole("clinic_admin") && tenantId.HasValue && f.tenant_id == tenantId.Value)));
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
    [HttpGet("favorites")]
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> GetFavorites()
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var tenantId = AuthHelpers.GetTenantId(User);
        var favorites = await _dbContext.food_favorites
            .Where(f => f.user_id == userId.Value &&
                ((f.food.source == null || f.food.source.ToLower() != "local") ||
                 User.IsInRole("superadmin") ||
                 (tenantId.HasValue && f.food.tenant_id == tenantId.Value && f.food.created_by_user_id == userId.Value) ||
                 (User.IsInRole("clinic_admin") && tenantId.HasValue && f.food.tenant_id == tenantId.Value)))
            .OrderByDescending(f => f.created_at)
            .Select(f => f.food)
            .AsNoTracking()
            .ToListAsync();

        return Ok(favorites);
    }

    [HttpPost("{id:int}/favorite")]
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> AddFavorite(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        var tenantId = AuthHelpers.GetTenantId(User);

        var food = await _dbContext.foods
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.id == id &&
                ((f.source == null || f.source.ToLower() != "local") ||
                 User.IsInRole("superadmin") ||
                 (tenantId.HasValue && f.tenant_id == tenantId.Value && f.created_by_user_id == userId.Value) ||
                 (User.IsInRole("clinic_admin") && tenantId.HasValue && f.tenant_id == tenantId.Value)));
        if (food == null) return NotFound();

        var exists = await _dbContext.food_favorites.AnyAsync(f => f.user_id == userId.Value && f.food_id == id);
        if (!exists)
        {
            _dbContext.food_favorites.Add(new food_favorites { user_id = userId.Value, food_id = id });
            await _dbContext.SaveChangesAsync();
        }

        return NoContent();
    }

    [HttpDelete("{id:int}/favorite")]
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> RemoveFavorite(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var tenantId = AuthHelpers.GetTenantId(User);
        var favorite = await _dbContext.food_favorites
            .Include(f => f.food)
            .FirstOrDefaultAsync(f => f.user_id == userId.Value && f.food_id == id &&
                ((f.food.source == null || f.food.source.ToLower() != "local") ||
                 User.IsInRole("superadmin") ||
                 (tenantId.HasValue && f.food.tenant_id == tenantId.Value && f.food.created_by_user_id == userId.Value) ||
                 (User.IsInRole("clinic_admin") && tenantId.HasValue && f.food.tenant_id == tenantId.Value)));
        if (favorite == null) return NoContent();

        _dbContext.food_favorites.Remove(favorite);
        await _dbContext.SaveChangesAsync();
        return NoContent();
    }


}