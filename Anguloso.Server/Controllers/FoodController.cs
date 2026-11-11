using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Middleware;
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
    private readonly FoodSubstitutionService _foodSubstitutionService;

    public FoodController(OpenFoodFactsService openFood, angulosodbContext dbContext, FoodSubstitutionService foodSubstitutionService)
    {
        _openFood = openFood;
        _dbContext = dbContext;
        _foodSubstitutionService = foodSubstitutionService;
    }

    private bool IsSuperAdmin() => User.IsInRole("superadmin");

    [HttpGet("admin/data-quality")]
    public async Task<IActionResult> GetFoodDataQuality(
        [FromQuery] string? search = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!IsSuperAdmin()) return Forbid();

        limit = Math.Clamp(limit, 1, 250);
        var foodsQuery = _dbContext.foods.AsNoTracking()
            .Where(f => f.data_quality_ignored_until == null || f.data_quality_ignored_until <= DateTime.UtcNow);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 100) return BadRequest("La búsqueda no puede superar 100 caracteres.");
            foodsQuery = foodsQuery.Where(f => f.name.Contains(term) || (f.brands != null && f.brands.Contains(term)));
        }

        var directUsage = await _dbContext.meal_items.AsNoTracking()
            .Where(item => item.food_id.HasValue)
            .GroupBy(item => item.food_id!.Value)
            .Select(group => new { FoodId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.FoodId, item => item.Count, cancellationToken);

        var recipeUsage = await (
            from mealItem in _dbContext.meal_items.AsNoTracking()
            where mealItem.recipe_id.HasValue
            join ingredient in _dbContext.recipe_items.AsNoTracking()
                on mealItem.recipe_id!.Value equals ingredient.recipe_id
            group ingredient by ingredient.food_id into grouped
            select new { FoodId = grouped.Key, Count = grouped.Count() }
        ).ToDictionaryAsync(item => item.FoodId, item => item.Count, cancellationToken);

        var candidates = await foodsQuery.ToListAsync(cancellationToken);
        var result = candidates.Select(food =>
        {
            var usage = directUsage.GetValueOrDefault(food.id) + recipeUsage.GetValueOrDefault(food.id);
            var missing = new List<string>();
            if (!food.kcal.HasValue) missing.Add("kcal");
            if (!food.protein.HasValue) missing.Add("protein");
            if (!food.carbs.HasValue) missing.Add("carbs");
            if (!food.fat.HasValue) missing.Add("fat");
            if (!food.sugar.HasValue) missing.Add("sugar");
            if (!food.saturated_fat.HasValue) missing.Add("saturated_fat");
            if (!food.monounsaturated_fat.HasValue) missing.Add("monounsaturated_fat");
            if (!food.polyunsaturated_fat.HasValue) missing.Add("polyunsaturated_fat");
            if (!food.fiber.HasValue) missing.Add("fiber");
            if (!food.salt.HasValue) missing.Add("salt");
            if (!food.vitamin_a_ug.HasValue) missing.Add("vitamin_a_ug");
            if (!food.vitamin_c_mg.HasValue) missing.Add("vitamin_c_mg");
            if (!food.vitamin_d_ug.HasValue) missing.Add("vitamin_d_ug");
            if (!food.vitamin_e_mg.HasValue) missing.Add("vitamin_e_mg");
            if (!food.vitamin_b12_ug.HasValue) missing.Add("vitamin_b12_ug");
            if (!food.folate_ug.HasValue) missing.Add("folate_ug");
            if (!food.calcium_mg.HasValue) missing.Add("calcium_mg");
            if (!food.iron_mg.HasValue) missing.Add("iron_mg");
            if (!food.magnesium_mg.HasValue) missing.Add("magnesium_mg");
            if (!food.potassium_mg.HasValue) missing.Add("potassium_mg");
            if (!food.zinc_mg.HasValue) missing.Add("zinc_mg");

            return new
            {
                food.id, food.name, food.brands, food.source, food.category, usageCount = usage,
                missingFields = missing,
                food.kcal, food.protein, food.carbs, food.fat, food.sugar, food.saturated_fat,
                food.monounsaturated_fat, food.polyunsaturated_fat, food.fiber, food.salt,
                food.vitamin_a_ug, food.vitamin_c_mg, food.vitamin_d_ug, food.vitamin_e_mg,
                food.vitamin_b12_ug, food.folate_ug, food.calcium_mg, food.iron_mg,
                food.magnesium_mg, food.potassium_mg, food.zinc_mg
            };
        })
        .Where(item => item.usageCount > 0 && item.missingFields.Count > 0)
        .OrderByDescending(item => item.usageCount)
        .ThenBy(item => item.name)
        .Take(limit)
        .ToList();

        return Ok(result);
    }

    [HttpPut("admin/data-quality/{id:int}")]
    public async Task<IActionResult> UpdateFoodDataQuality(
        int id, [FromBody] FoodDataQualityUpdateDto dto, CancellationToken cancellationToken)
    {
        if (!IsSuperAdmin()) return Forbid();
        if (dto == null) return BadRequest("No se han recibido datos.");

        var food = await _dbContext.foods.FirstOrDefaultAsync(f => f.id == id, cancellationToken);
        if (food == null) return NotFound();

        if (dto.Kcal.HasValue) food.kcal = dto.Kcal;
        if (dto.Protein.HasValue) food.protein = dto.Protein;
        if (dto.Carbs.HasValue) food.carbs = dto.Carbs;
        if (dto.Fat.HasValue) food.fat = dto.Fat;
        if (dto.Sugar.HasValue) food.sugar = dto.Sugar;
        if (dto.SaturatedFat.HasValue) food.saturated_fat = dto.SaturatedFat;
        if (dto.MonounsaturatedFat.HasValue) food.monounsaturated_fat = dto.MonounsaturatedFat;
        if (dto.PolyunsaturatedFat.HasValue) food.polyunsaturated_fat = dto.PolyunsaturatedFat;
        if (dto.Fiber.HasValue) food.fiber = dto.Fiber;
        if (dto.Salt.HasValue) food.salt = dto.Salt;
        if (dto.VitaminAUg.HasValue) food.vitamin_a_ug = dto.VitaminAUg;
        if (dto.VitaminCMg.HasValue) food.vitamin_c_mg = dto.VitaminCMg;
        if (dto.VitaminDUg.HasValue) food.vitamin_d_ug = dto.VitaminDUg;
        if (dto.VitaminEMg.HasValue) food.vitamin_e_mg = dto.VitaminEMg;
        if (dto.VitaminB12Ug.HasValue) food.vitamin_b12_ug = dto.VitaminB12Ug;
        if (dto.FolateUg.HasValue) food.folate_ug = dto.FolateUg;
        if (dto.CalciumMg.HasValue) food.calcium_mg = dto.CalciumMg;
        if (dto.IronMg.HasValue) food.iron_mg = dto.IronMg;
        if (dto.MagnesiumMg.HasValue) food.magnesium_mg = dto.MagnesiumMg;
        if (dto.PotassiumMg.HasValue) food.potassium_mg = dto.PotassiumMg;
        if (dto.ZincMg.HasValue) food.zinc_mg = dto.ZincMg;

        food.data_quality_ignored_until = null;
        food.data_quality_ignored_by_user_id = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("admin/data-quality/{id:int}/ignore")]
    public async Task<IActionResult> IgnoreFoodDataQuality(int id, CancellationToken cancellationToken)
    {
        if (!IsSuperAdmin()) return Forbid();
        var food = await _dbContext.foods.FirstOrDefaultAsync(f => f.id == id, cancellationToken);
        if (food == null) return NotFound();
        food.data_quality_ignored_until = DateTime.UtcNow.AddDays(30);
        food.data_quality_ignored_by_user_id = AuthHelpers.GetUserId(User);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("barcode/{code}")]
    [EnableRateLimiting("expensive")]
    public async Task<IActionResult> GetByBarcode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || !System.Text.RegularExpressions.Regex.IsMatch(code, @"^\d{8,14}$"))
            return BadRequest("El código de barras no es válido.");

        try
        {
            var p = await _openFood.GetProductByBarcodeAsync(code);
            if (p == null) return NotFound();
            return Ok(p);
        }
        catch (HttpRequestException)
        {
            return FoodProviderProblem();
        }
        catch (TaskCanceledException)
        {
            return FoodProviderProblem();
        }
        catch (System.Text.Json.JsonException)
        {
            return FoodProviderProblem();
        }
    }

    [HttpGet("count")]
    public async Task<IActionResult> GetFoodCount(CancellationToken cancellationToken)
    {
        // Reutiliza exactamente las reglas de visibilidad de OpenFoodFactsService.SearchAsync.
        // Se consulta el usuario por su nombre, igual que hace el buscador, y no se
        // introducen llamadas a User.IsInRole dentro de la expresión LINQ de EF.
        var userName = User.Identity?.Name;
        users? user = null;

        if (!string.IsNullOrWhiteSpace(userName))
        {
            user = await _dbContext.users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.username == userName, cancellationToken);
        }

        IQueryable<foods> visibleFoods = _dbContext.foods.AsNoTracking();

        if (user == null)
        {
            // Sin usuario asociado solo se cuentan alimentos globales, nunca locales.
            visibleFoods = visibleFoods.Where(f =>
                f.tenant_id == null && (f.source == null || f.source.ToLower() != "local"));
        }
        else if (user.role != "superadmin")
        {
            if (user.tenant_id.HasValue)
            {
                visibleFoods = visibleFoods.Where(f =>
                    f.tenant_id == user.tenant_id.Value ||
                    (f.tenant_id == null && (f.source == null || f.source.ToLower() != "local")));
            }
            else
            {
                visibleFoods = visibleFoods.Where(f =>
                    f.tenant_id == null && (f.source == null || f.source.ToLower() != "local"));
            }
        }

        var count = await visibleFoods.CountAsync(cancellationToken);
        return Ok(new { count });
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

        // Esta búsqueda combina catálogo local y proveedores externos. Un fallo externo
        // debe conservarse como error reportable en vez de parecer una búsqueda sin resultados.
        try
        {
            var results = await _openFood.SearchAsync(query, userName);
            return Ok(results);
        }
        catch (HttpRequestException)
        {
            return FoodProviderProblem();
        }
        catch (TaskCanceledException)
        {
            return FoodProviderProblem();
        }
        catch (System.Text.Json.JsonException)
        {
            return FoodProviderProblem();
        }
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
    [Idempotent]
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
            dietary_flags = NormalizeDietaryFlags(dto.DietaryFlags),
            allergen_tags = NormalizeDietaryFlags(dto.AllergenTags),
            traces_tags = NormalizeDietaryFlags(dto.TracesTags),
            allergen_data_status = NormalizeAllergenDataStatus(dto.AllergenDataStatus),
            allergen_data_source = dto.AllergenDataSource,
            allergen_data_checked_at = dto.AllergenDataCheckedAt,
            created_at = DateTime.UtcNow,
            last_synced_at = DateTime.UtcNow
        };

        _dbContext.foods.Add(food);
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetFoodById), new { id = food.id }, food);
    }

    // PUT: api/foods/{id}
    [HttpPut("{id:int}")]
    [Idempotent]
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
        food.dietary_flags = NormalizeDietaryFlags(dto.DietaryFlags);
        // Mantener los metadatos existentes si un cliente antiguo no envía estos campos.
        if (dto.AllergenTags != null) food.allergen_tags = NormalizeDietaryFlags(dto.AllergenTags);
        if (dto.TracesTags != null) food.traces_tags = NormalizeDietaryFlags(dto.TracesTags);
        if (dto.AllergenDataStatus != null) food.allergen_data_status = NormalizeAllergenDataStatus(dto.AllergenDataStatus);
        if (dto.AllergenDataSource != null) food.allergen_data_source = dto.AllergenDataSource;
        if (dto.AllergenDataCheckedAt.HasValue) food.allergen_data_checked_at = dto.AllergenDataCheckedAt;
        food.last_synced_at = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:int}/substitutions")]
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> GetSubstitutions(
        int id,
        [FromQuery] string target = "balanced",
        [FromQuery] string[]? excludeFlags = null,
        [FromQuery] int limit = 10,
        [FromQuery] int? clientId = null,
        [FromQuery] bool sameGroupOnly = false,
        CancellationToken cancellationToken = default)
    {
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!User.IsInRole("superadmin") && !tenantId.HasValue)
            return BadRequest("La cuenta profesional no tiene una clínica asociada.");

        var visibleFood = await _dbContext.foods.AsNoTracking().AnyAsync(f =>
            f.id == id &&
            ((f.source == null || f.source.ToLower() != "local") ||
             User.IsInRole("superadmin") ||
             (tenantId.HasValue && f.tenant_id == tenantId.Value)),
            cancellationToken);

        if (!visibleFood) return NotFound();

        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        clients? restrictionClient = null;
        if (clientId.HasValue)
        {
            restrictionClient = await _dbContext.clients
                .AsNoTracking()
                .Include(c => c.food_preferences)
                .Include(c => c.digestive_health)
                .FirstOrDefaultAsync(c => c.id == clientId.Value && c.archived_at == null &&
                    (User.IsInRole("superadmin") ||
                     (tenantId.HasValue && c.tenant_id == tenantId.Value &&
                      (c.user_id == userId.Value ||
                       _dbContext.client_nutritionist_assignments.Any(a =>
                           a.client_id == c.id && a.nutritionist_id == userId.Value && a.is_active) ||
                       User.IsInRole("clinic_admin")))), cancellationToken);

            if (restrictionClient == null) return NotFound("Cliente no encontrado o no pertenece al usuario.");
        }

        try
        {
            var result = await _foodSubstitutionService.SuggestAsync(
                id, tenantId, User.IsInRole("superadmin"), target, excludeFlags, limit, cancellationToken,
                restrictionClient?.food_preferences?.allergies,
                restrictionClient?.digestive_health?.gluten_intolerance == true,
                restrictionClient?.digestive_health?.lactose_intolerance == true,
                restrictionClient?.digestive_health?.fodmaps_intolerance == true,
                sameGroupOnly);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private ObjectResult FoodProviderProblem()
    {
        var result = Problem(
            detail: "El catálogo externo de alimentos no está disponible temporalmente. Inténtalo de nuevo más tarde.",
            statusCode: StatusCodes.Status502BadGateway,
            title: "No se pudo consultar el proveedor de alimentos");
        if (result.Value is ProblemDetails details)
        {
            details.Extensions["code"] = "FOOD_PROVIDER_ERROR";
            details.Extensions["reportable"] = true;
        }

        return result;
    }

    private static string NormalizeAllergenDataStatus(string? status)
    {
        var normalized = status?.Trim().ToLowerInvariant();
        return normalized is "reported" or "incomplete" or "stale" ? normalized : "unknown";
    }

    private static string[] NormalizeDietaryFlags(IEnumerable<string>? flags)
    {
        return (flags ?? Array.Empty<string>())
            .Where(flag => !string.IsNullOrWhiteSpace(flag))
            .Select(flag => flag.Trim().ToLowerInvariant())
            .Where(flag => flag.Length <= 50)
            .Distinct(StringComparer.Ordinal)
            .Take(30)
            .ToArray();
    }

    // DELETE: api/foods/{id}
    [HttpDelete("{id:int}")]
    [Idempotent]
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
    [Idempotent]
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
    [Idempotent]
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