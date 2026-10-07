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
    private readonly DietRegenerationService _regenerationService;

    public DietController(
        angulosodbContext context, 
        DietGeneratorService generatorService, 
        DietValidationService validationService,
        LogServ logServ,
        ILicenseService licenseService,
        DietRegenerationService regenerationService)
    {
        _context = context;
        _generatorService = generatorService;
        _validationService = validationService;
        _logServ = logServ;
        _licenseService = licenseService;
        _regenerationService = regenerationService;
    }


    // Los alimentos locales pertenecen al tenant; las fuentes externas pueden compartirse sin exponer datos privados.
    private async Task<bool> CanUseFoodAsync(int foodId, int userId, int? tenantId)
    {
        return await _context.foods.AnyAsync(f =>
            f.id == foodId &&
            ((f.source == null || f.source.ToLower() != "local") ||
             User.IsInRole("superadmin") ||
             (tenantId.HasValue && f.tenant_id == tenantId.Value && f.created_by_user_id == userId) ||
             (User.IsInRole("clinic_admin") && tenantId.HasValue && f.tenant_id == tenantId.Value)));
    }

    [HttpGet]
    // La consulta combina propiedad individual, dietas compartidas y SuperAdmin manteniendo el aislamiento por tenant.
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
    // Carga el árbol completo de la dieta y filtra los alimentos locales para no revelar referencias de otro tenant.
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
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
                        .ThenInclude(i => i.exchange_group)
            .FirstOrDefaultAsync(d => d.id == id && d.archived_at == null && ((tenantId.HasValue && d.tenant_id == tenantId.Value && d.user_id == userId.Value) || (sharedAllowed && tenantId.HasValue && d.tenant_id == tenantId.Value && d.is_shared)));

        if (d == null) return NotFound();

        // Primero se obtiene el conjunto de IDs realmente referenciados por la dieta; después se cargan solo los alimentos accesibles para el usuario, evitando exponer filas locales de otro tenant.
        var foodIds = d.diet_days
            .SelectMany(dd => dd.meals)
            .SelectMany(m => m.meal_items)
            .Where(i => i.food_id.HasValue)
            .Select(i => i.food_id!.Value)
            .Distinct()
            .ToList();

        var accessibleFoods = foodIds.Count == 0
            ? new Dictionary<int, foods>()
            : await _context.foods
                .Where(f => foodIds.Contains(f.id) &&
                    ((f.source == null || f.source.ToLower() != "local") ||
                     User.IsInRole("superadmin") ||
                     (tenantId.HasValue && f.tenant_id == tenantId.Value && f.created_by_user_id == userId.Value) ||
                     (User.IsInRole("clinic_admin") && tenantId.HasValue && f.tenant_id == tenantId.Value)))
                .ToDictionaryAsync(f => f.id);

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
                        FoodName = i.food_id.HasValue && accessibleFoods.TryGetValue(i.food_id.Value, out var accessibleFood) ? accessibleFood.name : null,
                        ExchangeGroupId = i.exchange_group_id,
                        ExchangeGroupName = i.exchange_group != null ? i.exchange_group.name : null,
                        ExchangeCount = i.exchange_count
                    }).ToList()
                }).ToList()
            }).ToList()
        });
    }

    // Los límites evitan cargas excesivas y mantienen acotado el tamaño de cada dieta recibida por API.
    private static string? ValidateDietPayload(string? name, string? notes, ICollection<DietDayDto>? days)
    {
        if (string.IsNullOrWhiteSpace(name)) return "El nombre de la dieta es obligatorio.";
        if (name.Length > 200) return "El nombre de la dieta no puede superar los 200 caracteres.";
        if (notes?.Length > 10000) return "Las notas no pueden superar los 10000 caracteres.";
        if (days == null || days.Count > 31) return "La dieta no puede contener más de 31 días.";
        if (days.Any(d => d == null || d.Meals == null || d.Meals.Count > 12)) return "Cada día no puede contener más de 12 comidas.";
        if (days.Any(d => d.DayIndex < 0 || d.DayIndex > 366)) return "El índice del día no es válido.";
        if (days.SelectMany(d => d.Meals).Any(m => m == null || string.IsNullOrWhiteSpace(m.Name) || m.Name.Length > 100 || m.Items == null || m.Items.Count > 100))
            return "Los datos de las comidas no son válidos o superan los límites permitidos.";
        if (days.SelectMany(d => d.Meals).SelectMany(m => m.Items).Count() > 2000)
            return "La dieta no puede contener más de 2000 alimentos/intercambios.";
        if (days.SelectMany(d => d.Meals).SelectMany(m => m.Items).Any(i => i.Grams.HasValue && (i.Grams.Value < 0 || i.Grams.Value > 100000)))
            return "La cantidad de gramos de un alimento no es válida.";
        if (days.SelectMany(d => d.Meals).SelectMany(m => m.Items).Any(i => i.ExchangeCount.HasValue && (i.ExchangeCount.Value < 0 || i.ExchangeCount.Value > 10000)))
            return "La cantidad de intercambios no es válida.";
        return null;
    }

    [HttpPost]
    // La dieta y su posible asignación al paciente se guardan en una única transacción.
    public async Task<ActionResult<DietListDto>> CreateDiet([FromBody] CreateDietDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var validationError = ValidateDietPayload(dto.Name, dto.Notes, dto.Days);
        if (validationError != null) return BadRequest(validationError);

        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var tenantId = AuthHelpers.GetTenantId(User);
        if (!tenantId.HasValue) return BadRequest("El usuario no pertenece a una clínica.");
        var tenantIdValue = tenantId.Value;

        // La creación y eventual asignación de la dieta forman una unidad: si falla cualquiera de las validaciones o escrituras posteriores, no queda una dieta huérfana ni una asignación parcial.
        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Serializamos la comprobación del límite de dietas por tenant.
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", tenantIdValue);

            // El bloqueo advisory se mantiene dentro de la transacción para que dos peticiones concurrentes no puedan consumir simultáneamente la misma plaza de licencia.
            var dietPermission = await _licenseService.CanCreateDietAsync(tenantIdValue, userId.Value);
            if (!dietPermission.Allowed)
                return BadRequest(dietPermission.Reason);

            if (dto.IsTemplate && !await _licenseService.CanUseFeatureAsync(tenantIdValue, "DIET_TEMPLATES"))
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
    // La edición reconstruye el árbol días/comidas/alimentos después de comprobar que el usuario es el propietario.
    public async Task<IActionResult> UpdateDiet(int id, [FromBody] UpdateDietDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var validationError = ValidateDietPayload(dto.Name, dto.Notes, dto.Days);
        if (validationError != null) return BadRequest(validationError);

        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var tenantId = AuthHelpers.GetTenantId(User);
        if (!tenantId.HasValue) return BadRequest("El usuario no pertenece a una clínica.");
        var tenantIdValue = tenantId.Value;

        // Solo el autor original puede editar su dieta
        var diet = await _context.diets
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
            .FirstOrDefaultAsync(d => d.id == id && d.archived_at == null && d.tenant_id == tenantIdValue && d.user_id == userId.Value);

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
    // El borrado es lógico para conservar el historial y evitar romper asignaciones históricas.
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

    [HttpPost("{id:int}/regenerate")]
    [EnableRateLimiting("expensive")]
    public async Task<ActionResult<DietRegenerationResponseDto>> RegenerateDietPart(
        int id,
        [FromBody] DietRegenerationRequestDto request)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (request == null) return BadRequest("La solicitud de regeneración es obligatoria.");
        if (request.Operation == "replace-food" && (!request.MealItemId.HasValue || !request.ReplacementFoodId.HasValue))
            return BadRequest("Para sustituir un alimento son obligatorios MealItemId y ReplacementFoodId.");
        if (request.Operation == "regenerate-meal" && !request.MealId.HasValue)
            return BadRequest("Para regenerar una comida es obligatorio MealId.");
        if (request.Operation == "regenerate-day" && !request.DayId.HasValue)
            return BadRequest("Para regenerar un día es obligatorio DayId.");

        try
        {
            var result = await _regenerationService.RegenerateAsync(
                id,
                request,
                userId.Value,
                AuthHelpers.GetTenantId(User),
                User.IsInRole("superadmin"),
                User.IsInRole("clinic_admin") || User.IsInRole("superadmin"),
                HttpContext.RequestAborted);

            return result.RolledBack ? Conflict(result) : Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { message = ex.Message });
        }
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
        if ((request.TargetProtein.HasValue && (double.IsNaN(request.TargetProtein.Value) || double.IsInfinity(request.TargetProtein.Value) || request.TargetProtein.Value < 0 || request.TargetProtein.Value > 2000)) ||
            (request.TargetCarbs.HasValue && (double.IsNaN(request.TargetCarbs.Value) || double.IsInfinity(request.TargetCarbs.Value) || request.TargetCarbs.Value < 0 || request.TargetCarbs.Value > 2000)) ||
            (request.TargetFat.HasValue && (double.IsNaN(request.TargetFat.Value) || double.IsInfinity(request.TargetFat.Value) || request.TargetFat.Value < 0 || request.TargetFat.Value > 2000)))
            return BadRequest("Los macronutrientes objetivo deben estar entre 0 y 2000 gramos.");
        if (string.IsNullOrWhiteSpace(request.DietType) || request.DietType.Length > 50)
            return BadRequest("El tipo de dieta no es válido.");
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
            var diet = await _generatorService.GenerateDietAsync(
                request,
                AuthHelpers.GetTenantId(User),
                userId.Value,
                User.IsInRole("clinic_admin") || User.IsInRole("superadmin"),
                HttpContext.RequestAborted);
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

        var warnings = await _validationService.ValidateDietDraftCompatibilityAsync(
            request.ClientId,
            request.Diet,
            _context,
            tenantId,
            userId.Value,
            User.IsInRole("clinic_admin") || User.IsInRole("superadmin"));
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

        // Revalidar el alcance de los alimentos antes de construir la lista de la compra.
        // Una dieta compartida puede ser visible dentro del tenant, pero un alimento local
        // sigue estando restringido a su creador/admin. No debemos filtrar el objeto food
        // directamente desde el Include porque eso permitiría revelar nombres de alimentos
        // locales a otros profesionales que solo tienen acceso a la dieta.
        var foodIds = diet.diet_days
            .SelectMany(dd => dd.meals)
            .SelectMany(m => m.meal_items)
            .Where(i => i.food_id.HasValue)
            .Select(i => i.food_id!.Value)
            .Distinct()
            .ToList();

        var accessibleFoods = await _context.foods
            .Where(f => foodIds.Contains(f.id) &&
                ((f.source == null || f.source.ToLower() != "local") ||
                 User.IsInRole("superadmin") ||
                 (tenantId.HasValue && f.tenant_id == tenantId.Value && f.created_by_user_id == userId.Value) ||
                 (User.IsInRole("clinic_admin") && tenantId.HasValue && f.tenant_id == tenantId.Value)))
            .ToDictionaryAsync(f => f.id);

        // Desvincular los alimentos no accesibles para que las agrupaciones de abajo
        // tampoco puedan exponerlos accidentalmente.
        foreach (var item in diet.diet_days.SelectMany(dd => dd.meals).SelectMany(m => m.meal_items))
        {
            if (item.food_id.HasValue && !accessibleFoods.ContainsKey(item.food_id.Value))
                item.food = null;
        }

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
                    Category = ShoppingListRules.NormalizeCategory(i.food!.category, i.food!.name)
                });

            foreach (var g in foodGrouped)
            {
                double totalGrams = Math.Round((double)g.Sum(i => i.grams!.Value), 1);
                var (roundedGrams, commercialDesc) = ShoppingListRules.CalculateCommercialRounding(totalGrams, g.Key.FoodName, g.Key.Category);

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
