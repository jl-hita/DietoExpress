using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Authorize(Policy = "Professional")]
[Route("api/dietas")]
public class DietController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly DietGeneratorService _generatorService;
    private readonly DietValidationService _validationService;
    private readonly LogServ _logServ;
    private readonly ILicenseService _licenseService;

    public DietController(
        angulosodbContext context, 
        DietGeneratorService generatorService, 
        DietValidationService validationService,
        LogServ logServ,
        ILicenseService licenseService)
    {
        _context = context;
        _generatorService = generatorService;
        _validationService = validationService;
        _logServ = logServ;
        _licenseService = licenseService;
    }


    private async Task<bool> CanUseFoodAsync(int foodId, int userId, int? tenantId)
    {
        return await _context.foods.AnyAsync(f =>
            f.id == foodId &&
            (!EF.Functions.ILike(f.source ?? "", "local") ||
             User.IsInRole("superadmin") ||
             (tenantId.HasValue && f.tenant_id == tenantId.Value && f.created_by_user_id == userId) ||
             (User.IsInRole("clinic_admin") && tenantId.HasValue && f.tenant_id == tenantId.Value)));
    }

    [HttpGet]
    public async Task<ActionResult<object>> GetDiets([FromQuery] bool? onlyShared = null, [FromQuery] bool includeAll = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var tenantId = AuthHelpers.GetTenantId(User);
        var isSuperAdmin = User.IsInRole("superadmin");
        var sharedAllowed = await _licenseService.CanUseFeatureAsync(tenantId, "SHARED_DIETS");
        if (onlyShared == true && !sharedAllowed) return Forbid();

        // Una dieta es visible si:
        // 1. Es del propio nutricionista (user_id == userId)
        // 2. O pertenece a la misma clínica (tenant_id == tenantId) Y ha sido marcada explícitamente como compartida (is_shared == true)
        var query = _context.diets
            .Include(d => d.user)
            .Where(d => d.archived_at == null)
            .Where(d => includeAll && isSuperAdmin
                ? true
                : (tenantId.HasValue && d.tenant_id == tenantId.Value && d.user_id == userId.Value) || (sharedAllowed && tenantId.HasValue && d.tenant_id == tenantId.Value && d.is_shared));

        if (onlyShared == true)
        {
            query = query.Where(d => d.is_shared && d.user_id != userId.Value);
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            if (searchTerm.Length > 100) return BadRequest("El texto de búsqueda no puede superar los 100 caracteres.");
            var pattern = $"%{searchTerm}%";
            query = query.Where(d => EF.Functions.ILike(d.name, pattern));
        }

        var totalCount = await query.CountAsync();
        var list = await query
            .OrderByDescending(d => d.created_at)
            .ThenByDescending(d => d.id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DietListDto
            {
                Id = d.id,
                Name = d.name,
                TargetKcal = d.target_kcal,
                TargetProtein = d.target_protein,
                TargetCarbs = d.target_carbs,
                TargetFat = d.target_fat,
                Notes = d.notes,
                CreatedAt = d.created_at,
                IsShared = d.is_shared,
                IsTemplate = d.is_template,
                IsMine = d.user_id == userId.Value,
                AuthorName = d.user != null ? (d.user.full_name ?? d.user.username) : null
            })
            .ToListAsync();

        return Ok(new { items = list, totalCount, page, pageSize });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DietDetailDto>> GetDiet(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var tenantId = AuthHelpers.GetTenantId(User);
        var sharedAllowed = await _licenseService.CanUseFeatureAsync(tenantId, "SHARED_DIETS");

        // Visible si es propia O si es compartida dentro de la misma clínica
        var d = await _context.diets
            .Include(d => d.user)
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
                        .ThenInclude(i => i.food)
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
                        .ThenInclude(i => i.exchange_group)
            .FirstOrDefaultAsync(d => d.id == id && d.archived_at == null && ((tenantId.HasValue && d.tenant_id == tenantId.Value && d.user_id == userId.Value) || (sharedAllowed && tenantId.HasValue && d.tenant_id == tenantId.Value && d.is_shared)));

        if (d == null) return NotFound();

        return Ok(new DietDetailDto
        {
            Id = d.id,
            Name = d.name,
            TargetKcal = d.target_kcal,
            TargetProtein = d.target_protein,
            TargetCarbs = d.target_carbs,
            TargetFat = d.target_fat,
            Notes = d.notes,
            CreatedAt = d.created_at,
            IsShared = d.is_shared,
            IsTemplate = d.is_template,
            IsMine = d.user_id == userId.Value,
            AuthorName = d.user != null ? (d.user.full_name ?? d.user.username) : null,
            Days = d.diet_days.OrderBy(dd => dd.day_index).Select(dd => new DietDayDto
            {
                Id = dd.id,
                DayIndex = dd.day_index,
                Meals = dd.meals.OrderBy(m => m.meal_index).Select(m => new MealDto
                {
                    Id = m.id,
                    Name = m.name,
                    MealIndex = m.meal_index,
                    Items = m.meal_items.Select(i => new MealItemDto
                    {
                        Id = i.id,
                        FoodId = i.food_id,
                        Grams = i.grams,
                        Kcal = i.kcal,
                        Protein = i.protein,
                        Carbs = i.carbs,
                        Fat = i.fat,
                        FoodName = i.food != null ? i.food.name : null,
                        ExchangeGroupId = i.exchange_group_id,
                        ExchangeGroupName = i.exchange_group != null ? i.exchange_group.name : null,
                        ExchangeCount = i.exchange_count
                    }).ToList()
                }).ToList()
            }).ToList()
        });
    }

    [HttpPost]
    public async Task<ActionResult<DietListDto>> CreateDiet([FromBody] CreateDietDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var tenantId = AuthHelpers.GetTenantId(User);
        if (!tenantId.HasValue) return BadRequest("El usuario no pertenece a una clínica.");

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Serializamos la comprobación del límite de dietas por tenant.
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", tenantId.Value);

            var dietPermission = await _licenseService.CanCreateDietAsync(tenantId, userId.Value);
            if (!dietPermission.Allowed)
                return BadRequest(dietPermission.Reason);

            if (dto.IsTemplate && !await _licenseService.CanUseFeatureAsync(tenantId, "DIET_TEMPLATES"))
                return Forbid();

            // Si la dieta se crea desde la ficha de un paciente, validamos el acceso
        // antes de modificar nada. La asignación se hará en la misma transacción.
        if (dto.ClientId.HasValue)
        {
            var clientAllowed = await _context.clients.AnyAsync(c =>
                c.id == dto.ClientId.Value &&
                c.archived_at == null &&
                (User.IsInRole("superadmin") ||
                 (tenantId.HasValue && c.tenant_id == tenantId.Value &&
                  (c.user_id == userId.Value ||
                   _context.client_nutritionist_assignments.Any(a =>
                       a.client_id == c.id &&
                       a.nutritionist_id == userId.Value &&
                       a.is_active) ||
                   User.IsInRole("clinic_admin")))));

            if (!clientAllowed)
                return NotFound("Cliente no encontrado o no pertenece al usuario.");
        }

            var diet = new diets
            {
                user_id = userId.Value,
                tenant_id = tenantId,
                name = dto.Name,
                target_kcal = dto.TargetKcal,
                target_protein = dto.TargetProtein,
                target_carbs = dto.TargetCarbs,
                target_fat = dto.TargetFat,
                notes = dto.Notes ?? "",
                is_shared = dto.IsShared && await _licenseService.CanUseFeatureAsync(AuthHelpers.GetTenantId(User), "SHARED_DIETS"),
                is_template = dto.IsTemplate,
                created_at = DateTime.UtcNow
            };

            if (dto.Days != null)
            {
                foreach (var dayDto in dto.Days)
                {
                    var day = new diet_days { day_index = dayDto.DayIndex };
                    foreach (var mealDto in dayDto.Meals)
                    {
                        var meal = new meals { name = mealDto.Name, meal_index = mealDto.MealIndex };
                        foreach (var itemDto in mealDto.Items)
                        {
                            if (itemDto.FoodId.HasValue && !await CanUseFoodAsync(itemDto.FoodId.Value, userId.Value, tenantId))
                                return BadRequest($"El alimento con ID {itemDto.FoodId.Value} no está disponible para esta cuenta.");

                            meal.meal_items.Add(new meal_items
                            {
                                food_id = itemDto.FoodId,
                                grams = itemDto.Grams,
                                kcal = itemDto.Kcal,
                                protein = itemDto.Protein,
                                carbs = itemDto.Carbs,
                                fat = itemDto.Fat,
                                exchange_group_id = itemDto.ExchangeGroupId,
                                exchange_count = itemDto.ExchangeCount
                            });
                        }
                        day.meals.Add(meal);
                    }
                    diet.diet_days.Add(day);
                }
            }

            _context.diets.Add(diet);
            await _context.SaveChangesAsync();

            if (dto.ClientId.HasValue)
            {
                // Mantener exactamente las mismas reglas que la asignación existente:
                // una sola dieta activa y conservar el historial.
                var activeDiets = await _context.client_diets
                    .Where(cd => cd.client_id == dto.ClientId.Value && cd.is_active == true &&
                                cd.diet != null && tenantId.HasValue && cd.diet.tenant_id == tenantId.Value)
                    .ToListAsync();

                var startDate = DateOnly.FromDateTime(DateTime.Today);

                foreach (var activeDiet in activeDiets)
                {
                    activeDiet.is_active = false;
                    activeDiet.end_date = startDate;
                }

                _context.client_diets.Add(new client_diets
                {
                    client_id = dto.ClientId.Value,
                    diet_id = diet.id,
                    start_date = startDate,
                    is_active = true,
                    notes = string.Empty,
                    assigned_at = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
            }

            await transaction.CommitAsync();

            return CreatedAtAction(nameof(GetDiet), new { id = diet.id }, new DietListDto
            {
                Id = diet.id,
                Name = diet.name,
                TargetKcal = diet.target_kcal,
                TargetProtein = diet.target_protein,
                TargetCarbs = diet.target_carbs,
                TargetFat = diet.target_fat,
                Notes = diet.notes,
                CreatedAt = diet.created_at,
                IsShared = diet.is_shared,
                IsTemplate = diet.is_template,
                IsMine = true
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateDiet(int id, [FromBody] UpdateDietDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var tenantId = AuthHelpers.GetTenantId(User);

        // Solo el autor original puede editar su dieta
        var diet = await _context.diets
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
            .FirstOrDefaultAsync(d => d.id == id && d.archived_at == null && tenantId.HasValue && d.tenant_id == tenantId.Value && d.user_id == userId.Value);

        if (diet == null) return NotFound();

        diet.name = dto.Name;
        diet.target_kcal = dto.TargetKcal;
        diet.target_protein = dto.TargetProtein;
        diet.target_carbs = dto.TargetCarbs;
        diet.target_fat = dto.TargetFat;
        diet.notes = dto.Notes ?? diet.notes ?? "";
        if (dto.IsShared && !await _licenseService.CanUseFeatureAsync(tenantId, "SHARED_DIETS"))
            return Forbid();
        if (dto.IsTemplate && !await _licenseService.CanUseFeatureAsync(tenantId, "DIET_TEMPLATES"))
            return Forbid();

        diet.is_shared = dto.IsShared;
        diet.is_template = dto.IsTemplate;

        // Remover todo el árbol anterior
        _context.diet_days.RemoveRange(diet.diet_days);
        diet.diet_days.Clear();

        // Construir nuevo árbol
        if (dto.Days != null)
        {
            foreach (var dayDto in dto.Days)
            {
                var day = new diet_days { day_index = dayDto.DayIndex };
                foreach (var mealDto in dayDto.Meals)
                {
                    var meal = new meals { name = mealDto.Name, meal_index = mealDto.MealIndex };
                    foreach (var itemDto in mealDto.Items)
                    {
                        if (itemDto.FoodId.HasValue && !await CanUseFoodAsync(itemDto.FoodId.Value, userId.Value, tenantId))
                            return BadRequest($"El alimento con ID {itemDto.FoodId.Value} no está disponible para esta cuenta.");

                        meal.meal_items.Add(new meal_items
                        {
                            food_id = itemDto.FoodId,
                            grams = itemDto.Grams,
                            kcal = itemDto.Kcal,
                            protein = itemDto.Protein,
                            carbs = itemDto.Carbs,
                            fat = itemDto.Fat,
                            exchange_group_id = itemDto.ExchangeGroupId,
                            exchange_count = itemDto.ExchangeCount
                        });
                    }
                    day.meals.Add(meal);
                }
                diet.diet_days.Add(day);
            }
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteDiet(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var tenantId = AuthHelpers.GetTenantId(User);
        var diet = await _context.diets
            .Include(d => d.diet_days)
            .FirstOrDefaultAsync(d => d.id == id && d.archived_at == null && tenantId.HasValue && d.tenant_id == tenantId.Value && d.user_id == userId.Value);
        if (diet == null) return NotFound();

        diet.archived_at = DateTime.UtcNow;
        diet.is_shared = false;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // POST: api/dietas/generate
    // Genera un plan de dieta estructurado de forma determinista
    [HttpPost("generate")]
    [EnableRateLimiting("expensive")]
    public async Task<ActionResult<DietDetailDto>> GenerateAutomatedDiet([FromBody] GenerateDietRequestDto request)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        // Límites defensivos: esta operación es costosa y no debe poder amplificarse
        // mediante un payload arbitrariamente grande aunque el endpoint tenga rate limiting.
        if (request.NumberOfDays < 1 || request.NumberOfDays > 14)
            return BadRequest("El número de días debe estar entre 1 y 14.");
        if (request.MealsPerDay < 3 || request.MealsPerDay > 5)
            return BadRequest("El número de comidas por día debe estar entre 3 y 5.");
        if (double.IsNaN(request.TargetKcal) || double.IsInfinity(request.TargetKcal) ||
            request.TargetKcal < 500 || request.TargetKcal > 10000)
            return BadRequest("Las calorías objetivo deben estar entre 500 y 10000.");
        if ((request.TargetProtein.HasValue && (double.IsNaN(request.TargetProtein.Value) || double.IsInfinity(request.TargetProtein.Value) || request.TargetProtein.Value < 0)) ||
            (request.TargetCarbs.HasValue && (double.IsNaN(request.TargetCarbs.Value) || double.IsInfinity(request.TargetCarbs.Value) || request.TargetCarbs.Value < 0)) ||
            (request.TargetFat.HasValue && (double.IsNaN(request.TargetFat.Value) || double.IsInfinity(request.TargetFat.Value) || request.TargetFat.Value < 0)))
            return BadRequest("Los macronutrientes objetivo deben ser valores finitos no negativos.");
        if (request.ExcludedFoodKeywords.Count > 100 || request.ExcludedFoodKeywords.Any(k => k == null || k.Length > 100))
            return BadRequest("La lista de exclusiones no puede superar 100 términos de hasta 100 caracteres.");

        if (request.ClientId.HasValue)
        {
            var tenantId = AuthHelpers.GetTenantId(User);
            var isSuperAdmin = User.IsInRole("superadmin");

            var clientExists = await _context.clients.AnyAsync(c =>
                c.id == request.ClientId.Value &&
                c.archived_at == null &&
                (isSuperAdmin ||
                 (tenantId.HasValue && c.tenant_id == tenantId.Value &&
                  (c.user_id == userId.Value ||
                   _context.client_nutritionist_assignments.Any(a => a.client_id == c.id && a.nutritionist_id == userId.Value && a.is_active) ||
                   User.IsInRole("clinic_admin")))));

            if (!clientExists)
                return NotFound("Cliente no encontrado.");
        }

        try
        {
            var diet = await _generatorService.GenerateDietAsync(request, AuthHelpers.GetTenantId(User), HttpContext.RequestAborted);
            return Ok(diet);
        }
        catch (Exception ex)
        {
            _logServ.LogError($"Error al generar el plan de dieta: {ex.Message}");
            return BadRequest("Error al generar el plan de dieta estructurado.");
        }
    }

    // POST: api/dietas/validate  y  POST: api/diets/validate
    // Compara los meal_items con las alergias, intolerancias (food_preferences, digestive_health)
    // y medicación habitual (medical_history / IFAF) del paciente.
    [HttpPost("validate")]
    [HttpPost("/api/diets/validate")]
    [EnableRateLimiting("expensive")]
    public async Task<ActionResult<List<DietValidationResultDto>>> ValidateDiet([FromBody] ValidateDietRequestDto request)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (request.ClientId <= 0)
        {
            return BadRequest("El identificador del cliente es obligatorio y debe ser válido.");
        }

        var tenantId = AuthHelpers.GetTenantId(User);
        var clientExists = await _context.clients.AnyAsync(c => c.id == request.ClientId && c.archived_at == null &&
             (User.IsInRole("superadmin") ||
              (tenantId.HasValue && c.tenant_id == tenantId.Value &&
               (c.user_id == userId.Value ||
                _context.client_nutritionist_assignments.Any(a => a.client_id == c.id && a.nutritionist_id == userId.Value && a.is_active) ||
                User.IsInRole("clinic_admin")))));
        if (!clientExists) return NotFound("Cliente no encontrado.");

        var warnings = await _validationService.ValidateDietDraftCompatibilityAsync(request.ClientId, request.Diet, _context, tenantId);
        return Ok(warnings);
    }

    // GET: api/dietas/{id}/shopping-list  y  GET: api/diets/{id}/shopping-list
    // Suma los gramos de los alimentos de la dieta, elimina duplicados,
    // redondea a cantidades comerciales lógicas y clasifica por categorías (Verduras, Carnes, Lácteos, etc.)
    [HttpGet("{id:int}/shopping-list")]
    [HttpGet("/api/diets/{id:int}/shopping-list")]
    [EnableRateLimiting("expensive")]
    public async Task<ActionResult<List<ShoppingCategoryDto>>> GetDietShoppingList(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var tenantId = AuthHelpers.GetTenantId(User);
        var sharedAllowed = await _licenseService.CanUseFeatureAsync(tenantId, "SHARED_DIETS");

        var diet = await _context.diets
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
                        .ThenInclude(i => i.food)
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
                        .ThenInclude(i => i.exchange_group)
            .FirstOrDefaultAsync(d => d.id == id && ((tenantId.HasValue && d.tenant_id == tenantId.Value && d.user_id == userId.Value) || (sharedAllowed && tenantId.HasValue && d.tenant_id == tenantId.Value && d.is_shared)));

        if (diet == null) return NotFound("Dieta no encontrada.");

        var items = new List<ShoppingItemDto>();

        if (diet.diet_days != null)
        {
            var allMealItems = diet.diet_days
                .Where(dd => dd.meals != null)
                .SelectMany(dd => dd.meals)
                .Where(m => m.meal_items != null)
                .SelectMany(m => m.meal_items)
                .ToList();

            // 1. Agrupar alimentos concretos
            var foodGrouped = allMealItems
                .Where(i => i.food != null && i.grams.HasValue && i.grams.Value > 0)
                .GroupBy(i => new
                {
                    FoodId = i.food!.id,
                    FoodName = i.food!.name ?? "Alimento",
                    Category = NormalizeCategory(i.food!.category, i.food!.name)
                });

            foreach (var g in foodGrouped)
            {
                double totalGrams = Math.Round((double)g.Sum(i => i.grams!.Value), 1);
                var (roundedGrams, commercialDesc) = CalculateCommercialRounding(totalGrams, g.Key.FoodName, g.Key.Category);

                items.Add(new ShoppingItemDto
                {
                    FoodId = g.Key.FoodId,
                    FoodName = g.Key.FoodName,
                    Category = g.Key.Category,
                    TotalGrams = totalGrams,
                    RoundedGrams = roundedGrams,
                    CommercialDescription = commercialDesc
                });
            }

            // 2. Agrupar intercambios si existen
            var exchangeGrouped = allMealItems
                .Where(i => i.food == null && i.exchange_group != null && i.exchange_count.HasValue && i.exchange_count.Value > 0)
                .GroupBy(i => new
                {
                    GroupId = i.exchange_group!.id,
                    GroupName = i.exchange_group!.name ?? "Grupo de Intercambio",
                    Category = "Opciones Equivalentes (Intercambios)"
                });

            foreach (var eg in exchangeGrouped)
            {
                double totalCount = Math.Round((double)eg.Sum(i => i.exchange_count!.Value), 1);
                items.Add(new ShoppingItemDto
                {
                    FoodId = -eg.Key.GroupId,
                    FoodName = eg.Key.GroupName,
                    Category = eg.Key.Category,
                    TotalGrams = totalCount,
                    RoundedGrams = Math.Ceiling(totalCount),
                    CommercialDescription = $"{totalCount} intercambio(s) semanal(es) (seleccionar alimento equivalente)"
                });
            }
        }

        var result = items
            .GroupBy(i => i.Category)
            .OrderBy(catGroup => GetCategorySortOrder(catGroup.Key))
            .ThenBy(catGroup => catGroup.Key)
            .Select(catGroup => new ShoppingCategoryDto
            {
                Category = catGroup.Key,
                Items = catGroup.OrderBy(i => i.FoodName).ToList()
            })
            .ToList();

        return Ok(result);
    }

    private static string NormalizeCategory(string? rawCategory, string? foodName)
    {
        var cat = (rawCategory ?? "").Trim();
        var name = (foodName ?? "").ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(cat) || cat.Equals("otros", StringComparison.OrdinalIgnoreCase))
        {
            if (name.Contains("pollo") || name.Contains("ternera") || name.Contains("cerdo") || name.Contains("pavo") || name.Contains("lomo") || name.Contains("conejo"))
                return "Carnes y Aves";
            if (name.Contains("atún") || name.Contains("merluza") || name.Contains("salmon") || name.Contains("salmón") || name.Contains("bacalao") || name.Contains("lubina") || name.Contains("dorada") || name.Contains("gamba") || name.Contains("langostino"))
                return "Pescados y Mariscos";
            if (name.Contains("leche") || name.Contains("queso") || name.Contains("yogur") || name.Contains("cuajada") || name.Contains("kéfir"))
                return "Lácteos y Derivados";
            if (name.Contains("huevo") || name.Contains("clara"))
                return "Huevos";
            if (name.Contains("manzana") || name.Contains("plátano") || name.Contains("platano") || name.Contains("naranja") || name.Contains("pera") || name.Contains("fresa") || name.Contains("kiwi") || name.Contains("mandarina"))
                return "Frutas";
            if (name.Contains("tomate") || name.Contains("lechuga") || name.Contains("espinaca") || name.Contains("calabacín") || name.Contains("cebolla") || name.Contains("zanahoria") || name.Contains("brócoli") || name.Contains("pepino"))
                return "Verduras y Hortalizas";
            if (name.Contains("arroz") || name.Contains("pasta") || name.Contains("pan") || name.Contains("avena") || name.Contains("quinoa") || name.Contains("macarrones") || name.Contains("espaguetis"))
                return "Cereales y Harinas";
            if (name.Contains("lenteja") || name.Contains("garbanzo") || name.Contains("alubia") || name.Contains("judía"))
                return "Legumbres";
            if (name.Contains("aceite") || name.Contains("oliva") || name.Contains("mantequilla"))
                return "Aceites y Grasas";
            if (name.Contains("nuez") || name.Contains("almendra") || name.Contains("avellana") || name.Contains("anacardo") || name.Contains("cacahuete") || name.Contains("pistacho"))
                return "Frutos Secos y Semillas";

            return "Otros Alimentos";
        }

        var lower = cat.ToLowerInvariant();
        if (lower.Contains("verdura") || lower.Contains("hortaliz")) return "Verduras y Hortalizas";
        if (lower.Contains("fruta")) return "Frutas";
        if (lower.Contains("carne") || lower.Contains("ave")) return "Carnes y Aves";
        if (lower.Contains("pescado") || lower.Contains("marisco")) return "Pescados y Mariscos";
        if (lower.Contains("lácteo") || lower.Contains("lacteo") || lower.Contains("queso") || lower.Contains("leche")) return "Lácteos y Derivados";
        if (lower.Contains("huevo")) return "Huevos";
        if (lower.Contains("legumbre")) return "Legumbres";
        if (lower.Contains("cereal") || lower.Contains("pasta") || lower.Contains("pan") || lower.Contains("harina")) return "Cereales y Harinas";
        if (lower.Contains("aceite") || lower.Contains("grasa")) return "Aceites y Grasas";
        if (lower.Contains("fruto seco") || lower.Contains("semilla")) return "Frutos Secos y Semillas";

        return cat;
    }

    private static (double roundedGrams, string commercialDesc) CalculateCommercialRounding(double grams, string foodName, string category)
    {
        var lower = foodName.ToLowerInvariant();

        // 1. Huevos (50-60g por huevo)
        if (lower.Contains("huevo") && !lower.Contains("clara"))
        {
            int eggs = (int)Math.Max(1, Math.Ceiling(grams / 55.0));
            return (eggs * 55.0, $"{eggs} unidad(es) ({eggs * 55} g aprox.)");
        }

        // 2. Latas de atún / conservas (~80g o 160g)
        if (lower.Contains("atún") || lower.Contains("atun") || lower.Contains("bonito"))
        {
            int cans = (int)Math.Max(1, Math.Ceiling(grams / 80.0));
            double rounded = cans * 80.0;
            return (rounded, $"{cans} lata(s) de 80g ({rounded} g)");
        }

        // 3. Yogures (~125g)
        if (lower.Contains("yogur"))
        {
            int pots = (int)Math.Max(1, Math.Ceiling(grams / 125.0));
            double rounded = pots * 125.0;
            return (rounded, $"{pots} envase(s) de 125g ({rounded} g)");
        }

        // 4. Frutas por piezas promedio (~150-180g pieza media)
        if (category == "Frutas")
        {
            int units = (int)Math.Max(1, Math.Round(grams / 160.0));
            double rounded = units * 160.0;
            if (grams >= 1000)
            {
                double kg = Math.Ceiling(grams / 500.0) * 0.5;
                return (kg * 1000.0, $"{kg:0.#} kg (aprox. {units} piezas)");
            }
            return (rounded, $"aprox. {units} pieza(s) ({grams:0} g)");
        }

        // 5. Carnes y pescados frescos: redondeo a múltiplos comerciales (100g, 250g o 500g)
        if (category == "Carnes y Aves" || category == "Pescados y Mariscos")
        {
            if (grams < 250)
            {
                double rounded = Math.Ceiling(grams / 50.0) * 50.0;
                return (rounded, $"{rounded:0} g");
            }
            if (grams < 1000)
            {
                double rounded = Math.Ceiling(grams / 100.0) * 100.0;
                return (rounded, $"{rounded:0} g");
            }
            double kgRounded = Math.Ceiling(grams / 250.0) * 0.25;
            return (kgRounded * 1000.0, $"{kgRounded:0.##} kg");
        }

        // 6. Aceites y condimentos (redondeo a 50ml/g o botellas estándar)
        if (category == "Aceites y Grasas")
        {
            if (grams <= 250) return (250, "1 envase / botella (250 ml/g)");
            if (grams <= 500) return (500, "1 botella (500 ml)");
            return (1000, "1 botella (1 Litro)");
        }

        // 7. Cereales, arroces, pastas y legumbres secas: paquetes de 500g o 1kg
        if (category == "Cereales y Harinas" || category == "Legumbres")
        {
            if (grams <= 500) return (500, $"1 paquete de 500g (usará {grams:0} g)");
            if (grams <= 1000) return (1000, $"1 paquete de 1 kg (usará {grams:0} g)");
            int packs = (int)Math.Ceiling(grams / 1000.0);
            return (packs * 1000, $"{packs} paquetes de 1 kg ({packs * 1000} g)");
        }

        // 8. Regla general para verduras y otros: redondeo a múltiplos de 50g o 100g
        if (grams >= 1000)
        {
            double kg = Math.Ceiling(grams / 250.0) * 0.25;
            return (kg * 1000.0, $"{kg:0.##} kg");
        }
        else
        {
            double rounded = Math.Ceiling(grams / 25.0) * 25.0;
            return (rounded, $"{rounded:0} g");
        }
    }

    private static int GetCategorySortOrder(string category)
    {
        return category switch
        {
            "Verduras y Hortalizas" => 1,
            "Frutas" => 2,
            "Carnes y Aves" => 3,
            "Pescados y Mariscos" => 4,
            "Huevos" => 5,
            "Lácteos y Derivados" => 6,
            "Cereales y Harinas" => 7,
            "Legumbres" => 8,
            "Frutos Secos y Semillas" => 9,
            "Aceites y Grasas" => 10,
            "Opciones Equivalentes (Intercambios)" => 11,
            _ => 12
        };
    }
}
