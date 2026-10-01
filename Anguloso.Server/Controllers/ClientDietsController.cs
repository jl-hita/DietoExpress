using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Anguloso.Server.Controllers;

[ApiController]
[Authorize(Policy = "Professional")]
[Route("api/clients/{clientId:int}/diets")]
public class ClientDietsController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly DietPdfService _pdfService;
    private readonly DietValidationService _validationService;
    private readonly ILicenseService _licenseService;

    public ClientDietsController(angulosodbContext context, DietPdfService pdfService, DietValidationService validationService, ILicenseService licenseService)
    {
        _context = context;
        _pdfService = pdfService;
        _validationService = validationService;
        _licenseService = licenseService;
    }

    private async Task<bool> UserOwnsClientAsync(int clientId, int userId)
    {
        var tenantId = AuthHelpers.GetTenantId(User);

        return await _context.clients.AnyAsync(c =>
            c.id == clientId &&
            c.archived_at == null &&
            (User.IsInRole("superadmin") ||
             (tenantId.HasValue && c.tenant_id == tenantId.Value &&
              (c.user_id == userId ||
               _context.client_nutritionist_assignments.Any(a => a.client_id == c.id && a.nutritionist_id == userId && a.is_active) ||
               User.IsInRole("clinic_admin")))));
    }

    private async Task<bool> UserCanAccessDietAsync(int dietId, int userId)
    {
        var tenantId = AuthHelpers.GetTenantId(User);
        var sharedAllowed = await _licenseService.CanUseFeatureAsync(tenantId, "SHARED_DIETS");

        return await _context.diets.AnyAsync(d =>
            d.id == dietId &&
            d.archived_at == null &&
            (User.IsInRole("superadmin") ||
             (tenantId.HasValue && d.tenant_id == tenantId.Value &&
              (d.user_id == userId ||
               (sharedAllowed && d.is_shared)))));
    }

    private async Task SanitizeDietFoodScopeAsync(diets diet, int userId, int? tenantId)
    {
        var foodIds = diet.diet_days.SelectMany(dd => dd.meals).SelectMany(m => m.meal_items)
            .Where(i => i.food_id.HasValue).Select(i => i.food_id!.Value).Distinct().ToList();
        if (foodIds.Count == 0) return;

        var accessibleIds = await _context.foods
            .Where(f => foodIds.Contains(f.id) &&
                ((f.source == null || f.source.ToLower() != "local") ||
                 User.IsInRole("superadmin") ||
                 (tenantId.HasValue && f.tenant_id == tenantId.Value && f.created_by_user_id == userId) ||
                 (User.IsInRole("clinic_admin") && tenantId.HasValue && f.tenant_id == tenantId.Value)))
            .Select(f => f.id)
            .ToListAsync();

        var allowed = accessibleIds.ToHashSet();
        foreach (var item in diet.diet_days.SelectMany(dd => dd.meals).SelectMany(m => m.meal_items))
        {
            if (item.food_id.HasValue && !allowed.Contains(item.food_id.Value))
                item.food = null;
        }
    }

    // GET: api/clients/{clientId}/diets
    [HttpGet]
    public async Task<ActionResult<List<ClientDietListDto>>> GetHistory(int clientId)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

        var tenantId = AuthHelpers.GetTenantId(User);
        var sharedAllowed = await _licenseService.CanUseFeatureAsync(tenantId, "SHARED_DIETS");

        var history = await _context.client_diets
            .Include(cd => cd.diet)
            .Where(cd => cd.client_id == clientId)
            .Where(cd => _context.diets.Any(d =>
                d.id == cd.diet_id &&
                d.archived_at == null &&
                (User.IsInRole("superadmin") ||
                 (tenantId.HasValue && d.tenant_id == tenantId.Value &&
                  (d.user_id == userId.Value ||
                   (sharedAllowed && d.is_shared))))))
            .OrderByDescending(cd => cd.start_date)
            .Select(cd => new ClientDietListDto
            {
                Id = cd.id,
                ClientId = cd.client_id,
                DietId = cd.diet_id,
                DietName = cd.diet != null ? cd.diet.name : string.Empty,
                AssignedAt = cd.assigned_at,
                StartDate = cd.start_date.ToDateTime(TimeOnly.MinValue),
                EndDate = cd.end_date.HasValue ? cd.end_date.Value.ToDateTime(TimeOnly.MinValue) : null,
                IsActive = cd.is_active ?? false,
                Notes = cd.notes
            })
            .ToListAsync();

        return Ok(history);
    }

    // GET: api/clients/{clientId}/diets/active
    [HttpGet("active")]
    public async Task<ActionResult<DietDetailDto>> GetActiveDiet(int clientId)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

        var tenantId = AuthHelpers.GetTenantId(User);
        var activeAssignment = await _context.client_diets
            .Include(cd => cd.diet)
            .Where(cd => cd.client_id == clientId && cd.is_active == true && tenantId.HasValue &&
                         cd.diet != null && cd.diet.tenant_id == tenantId.Value)
            .FirstOrDefaultAsync();

        if (activeAssignment == null)
            return NotFound("No active diet assignment found for this patient.");

        if (!await UserCanAccessDietAsync(activeAssignment.diet_id, userId.Value))
            return NotFound("The active diet is not available to this user.");

        var d = await _context.diets
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
                        .ThenInclude(i => i.exchange_group)
            .FirstOrDefaultAsync(d => d.id == activeAssignment.diet_id && tenantId.HasValue && d.tenant_id == tenantId.Value);

        if (d == null)
            return NotFound("The active diet definition was not found.");

        var foodIds = d.diet_days.SelectMany(dd => dd.meals).SelectMany(m => m.meal_items)
            .Where(i => i.food_id.HasValue).Select(i => i.food_id!.Value).Distinct().ToList();
        var accessibleFoods = await _context.foods
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

    // POST: api/clients/{clientId}/diets
    [HttpPost]
    public async Task<ActionResult<ClientDietListDto>> AssignDiet(int clientId, [FromBody] AssignDietDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

        var dietExists = await UserCanAccessDietAsync(dto.DietId, userId.Value);
        if (!dietExists) return BadRequest("The selected diet does not exist.");

        // La mutación de asignaciones activas debe serializarse por tenant.
        var clientTenantId = await _context.clients
            .Where(c => c.id == clientId && c.archived_at == null)
            .Select(c => c.tenant_id)
            .FirstOrDefaultAsync();

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            if (_context.Database.IsRelational())
            {
                transaction = await _context.Database.BeginTransactionAsync();
                if (clientTenantId.HasValue)
                    await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", clientTenantId.Value);
            }

            if (!await UserOwnsClientAsync(clientId, userId.Value))
                return NotFound("Client not found or does not belong to the user.");
            if (!await UserCanAccessDietAsync(dto.DietId, userId.Value))
                return BadRequest("The selected diet does not exist.");

            var activeDiets = await _context.client_diets
            .Include(cd => cd.diet)
            .Where(cd => cd.client_id == clientId && cd.is_active == true &&
                         (!clientTenantId.HasValue || (cd.diet != null && cd.diet.tenant_id == clientTenantId.Value)))
            .ToListAsync();

        foreach (var activeDiet in activeDiets)
         {
            activeDiet.is_active = false;
            activeDiet.end_date = DateOnly.FromDateTime(dto.StartDate);
        }

        var newAssignment = new client_diets
        {
            client_id = clientId,
            diet_id = dto.DietId,
            start_date = DateOnly.FromDateTime(dto.StartDate),
            is_active = true,
            notes = dto.Notes ?? string.Empty,
            assigned_at = DateTime.UtcNow
        };

        _context.client_diets.Add(newAssignment);
        await _context.SaveChangesAsync();

        var dietName = await _context.diets
            .Where(d => d.id == newAssignment.diet_id)
            .Select(d => d.name)
            .FirstOrDefaultAsync() ?? string.Empty;

            if (transaction != null)
                await transaction.CommitAsync();

        return Ok(new ClientDietListDto
        {
            Id = newAssignment.id,
            ClientId = newAssignment.client_id,
            DietId = newAssignment.diet_id,
            DietName = dietName,
            AssignedAt = newAssignment.assigned_at,
            StartDate = newAssignment.start_date.ToDateTime(TimeOnly.MinValue),
            EndDate = null,
            IsActive = newAssignment.is_active ?? true,
            Notes = newAssignment.notes
        });
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    // PUT: api/clients/{clientId}/diets/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAssignment(int clientId, int id, [FromBody] UpdateClientDietDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

        var clientTenantId = await _context.clients
            .Where(c => c.id == clientId && c.archived_at == null)
            .Select(c => c.tenant_id)
            .FirstOrDefaultAsync();

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            if (_context.Database.IsRelational())
            {
                transaction = await _context.Database.BeginTransactionAsync();
                if (clientTenantId.HasValue)
                    await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", clientTenantId.Value);
            }

            if (!await UserOwnsClientAsync(clientId, userId.Value))
                return NotFound("Client not found or does not belong to the user.");

            var assignment = await _context.client_diets
            .Include(cd => cd.diet)
            .FirstOrDefaultAsync(cd => cd.id == id && cd.client_id == clientId &&
                (User.IsInRole("superadmin") ||
                 (clientTenantId.HasValue && cd.diet != null && cd.diet.tenant_id == clientTenantId.Value)));
        if (assignment == null) return NotFound("Diet assignment not found.");

        if (!await UserCanAccessDietAsync(assignment.diet_id, userId.Value))
            return NotFound("Diet assignment not found.");

        // Si se está activando, desactivar otras dietas del mismo cliente para evitar violación de la restricción UNIQUE
        if (dto.IsActive)
        {
            var otherActiveDiets = await _context.client_diets
                .Include(cd => cd.diet)
                .Where(cd => cd.client_id == clientId && cd.is_active == true && cd.id != id &&
                             (!clientTenantId.HasValue || (cd.diet != null && cd.diet.tenant_id == clientTenantId.Value)))
                .ToListAsync();

            foreach (var activeDiet in otherActiveDiets)
            {
                activeDiet.is_active = false;
                if (activeDiet.end_date == null)
                {
                    activeDiet.end_date = DateOnly.FromDateTime(dto.StartDate);
                }
            }
        }

        assignment.start_date = DateOnly.FromDateTime(dto.StartDate);
        assignment.end_date = dto.EndDate.HasValue ? DateOnly.FromDateTime(dto.EndDate.Value) : null;
        assignment.is_active = dto.IsActive;
        assignment.notes = dto.Notes ?? string.Empty;

            await _context.SaveChangesAsync();
            if (transaction != null)
                await transaction.CommitAsync();
            return NoContent();
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    // POST: api/clients/{clientId}/diets/{id}/deactivate
    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> DeactivateAssignment(int clientId, int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

        var clientTenantId = await _context.clients
            .Where(c => c.id == clientId && c.archived_at == null)
            .Select(c => c.tenant_id)
            .FirstOrDefaultAsync();

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            if (_context.Database.IsRelational())
            {
                transaction = await _context.Database.BeginTransactionAsync();
                if (clientTenantId.HasValue)
                    await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", clientTenantId.Value);
            }

            var assignment = await _context.client_diets
                .Include(cd => cd.diet)
                .FirstOrDefaultAsync(cd => cd.id == id && cd.client_id == clientId &&
                    (clientTenantId.HasValue && cd.diet != null && cd.diet.tenant_id == clientTenantId.Value));

            if (assignment == null) return NotFound("Diet assignment not found.");

            if (!await UserCanAccessDietAsync(assignment.diet_id, userId.Value))
                return NotFound("Diet assignment not found.");

            assignment.is_active = false;
            assignment.end_date = DateOnly.FromDateTime(DateTime.Today);

            await _context.SaveChangesAsync();
            if (transaction != null)
                await transaction.CommitAsync();
            return NoContent();
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    // DELETE: api/clients/{clientId}/diets/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAssignment(int clientId, int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

        var clientTenantId = await _context.clients
            .Where(c => c.id == clientId && c.archived_at == null)
            .Select(c => c.tenant_id)
            .FirstOrDefaultAsync();

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            if (_context.Database.IsRelational())
            {
                transaction = await _context.Database.BeginTransactionAsync();
                if (clientTenantId.HasValue)
                    await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", clientTenantId.Value);
            }

            var assignment = await _context.client_diets
                .Include(cd => cd.diet)
                .FirstOrDefaultAsync(cd => cd.id == id && cd.client_id == clientId &&
                    (clientTenantId.HasValue && cd.diet != null && cd.diet.tenant_id == clientTenantId.Value));

            if (assignment == null) return NotFound("Diet assignment not found.");

            if (!await UserCanAccessDietAsync(assignment.diet_id, userId.Value))
                return NotFound("Diet assignment not found.");

            _context.client_diets.Remove(assignment);
            await _context.SaveChangesAsync();

            if (transaction != null)
                await transaction.CommitAsync();
            return NoContent();
        }
        catch
        {
            if (transaction != null)
                await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            if (transaction != null)
                await transaction.DisposeAsync();
        }
    }

    // GET: api/clients/{clientId}/diets/consultation-pdf?date=yyyy-MM-dd
    [HttpGet("consultation-pdf")]
    [EnableRateLimiting("expensive")]
    public async Task<IActionResult> GetConsultationPdf(int clientId, [FromQuery] DateOnly? date = null)
    {
        if (!User.IsInRole("superadmin") && !await _licenseService.CanUseFeatureAsync(AuthHelpers.GetTenantId(User), "PDF_EXPORT"))
            return Forbid();

        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

        var consultationDate = date ?? DateOnly.FromDateTime(DateTime.Today);

        var tenantId = AuthHelpers.GetTenantId(User);
        var client = await _context.clients
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.id == clientId && c.archived_at == null &&
                (User.IsInRole("superadmin") || (tenantId.HasValue && c.tenant_id == tenantId.Value)));

        if (client == null)
            return NotFound("Client not found.");

        try
        {
            var pdfBytes = new ConsultationPdfService().GenerateConsultationPdf(
                client,
                consultationDate,
                _context);

            var clientName = string.IsNullOrWhiteSpace(client.full_name)
                ? $"Cliente_{clientId}"
                : client.full_name;

            var fileName = $"Informe_Consulta_{SanitizeFileName(clientName)}_{consultationDate:yyyy-MM-dd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error generando informe de consulta para cliente {clientId} ({consultationDate}): {ex.GetType().Name}");
            return Problem(
                title: "Error al generar el informe",
                detail: "No se ha podido generar el informe de consulta.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // GET: api/clients/{clientId}/diets/{id}/pdf
    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> GetDietPdf(int clientId, int id)
    {
        if (!User.IsInRole("superadmin") && !await _licenseService.CanUseFeatureAsync(AuthHelpers.GetTenantId(User), "PDF_EXPORT")) return Forbid();
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

        var clientTenantId = await _context.clients.Where(c => c.id == clientId).Select(c => c.tenant_id).FirstOrDefaultAsync();
        var tenantId = AuthHelpers.GetTenantId(User);
        var assignment = await _context.client_diets
            .Include(cd => cd.client)
            .Include(cd => cd.diet)
                .ThenInclude(d => d.diet_days)
                    .ThenInclude(dd => dd.meals)
                        .ThenInclude(m => m.meal_items)
            .Include(cd => cd.diet)
                .ThenInclude(d => d.diet_days)
                    .ThenInclude(dd => dd.meals)
                        .ThenInclude(m => m.meal_items)
                            .ThenInclude(i => i.exchange_group)
            .FirstOrDefaultAsync(cd => cd.id == id && cd.client_id == clientId &&
                (User.IsInRole("superadmin") || (clientTenantId.HasValue && cd.diet != null && cd.diet.tenant_id == clientTenantId.Value)));

        if (assignment == null)
            return NotFound("Diet assignment not found.");

        if (!await UserCanAccessDietAsync(assignment.diet_id, userId.Value))
            return NotFound("Diet assignment not found.");

        if (assignment.diet == null)
            return NotFound("Diet definition not found.");

        await SanitizeDietFoodScopeAsync(assignment.diet, userId.Value, tenantId);

        try
        {
            var pdfBytes = _pdfService.GenerateDietPdf(assignment.client, assignment.diet, assignment, _context, userId.Value, User.IsInRole("clinic_admin") || User.IsInRole("superadmin"));

            var clientName = string.IsNullOrWhiteSpace(assignment.client.full_name) ? $"Cliente_{clientId}" : assignment.client.full_name;
            var dietName = string.IsNullOrWhiteSpace(assignment.diet.name) ? $"Dieta_{id}" : assignment.diet.name;
            var fileName = $"Dieta_{SanitizeFileName(clientName)}_{SanitizeFileName(dietName)}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error generando PDF de dieta {id} para cliente {clientId}: {ex.GetType().Name}");
            return Problem(title: "Error al generar el PDF", detail: "No se ha podido generar el PDF de la dieta.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // GET: api/clients/{clientId}/diets/active/pdf
    [HttpGet("active/pdf")]
    public async Task<IActionResult> GetActiveDietPdf(int clientId)
    {
        if (!User.IsInRole("superadmin") && !await _licenseService.CanUseFeatureAsync(AuthHelpers.GetTenantId(User), "PDF_EXPORT")) return Forbid();
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

        var tenantId = AuthHelpers.GetTenantId(User);
        var assignment = await _context.client_diets
            .Include(cd => cd.client)
            .Include(cd => cd.diet)
                .ThenInclude(d => d.diet_days)
                    .ThenInclude(dd => dd.meals)
                        .ThenInclude(m => m.meal_items)
                            .ThenInclude(i => i.food)
            .Include(cd => cd.diet)
                .ThenInclude(d => d.diet_days)
                    .ThenInclude(dd => dd.meals)
                        .ThenInclude(m => m.meal_items)
                            .ThenInclude(i => i.exchange_group)
            .FirstOrDefaultAsync(cd => cd.client_id == clientId && cd.is_active == true &&
                (User.IsInRole("superadmin") || (tenantId.HasValue && cd.diet != null && cd.diet.tenant_id == tenantId.Value)));

        if (assignment == null)
            return NotFound("No active diet assignment found for this patient.");

        if (!await UserCanAccessDietAsync(assignment.diet_id, userId.Value))
            return NotFound("No active diet assignment found for this patient.");

        if (assignment.diet == null)
            return NotFound("Diet definition not found.");

        await SanitizeDietFoodScopeAsync(assignment.diet, userId.Value, tenantId);

        try
        {
            var pdfBytes = _pdfService.GenerateDietPdf(assignment.client, assignment.diet, assignment, _context, userId.Value, User.IsInRole("clinic_admin") || User.IsInRole("superadmin"));

            var clientName = string.IsNullOrWhiteSpace(assignment.client.full_name) ? $"Cliente_{clientId}" : assignment.client.full_name;
            var dietName = string.IsNullOrWhiteSpace(assignment.diet.name) ? $"Dieta_{assignment.diet.id}" : assignment.diet.name;
            var fileName = $"Dieta_Activa_{SanitizeFileName(clientName)}_{SanitizeFileName(dietName)}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error generando PDF de dieta activa para cliente {clientId}: {ex}");
            return Problem(title: "Error al generar el PDF", detail: "No se ha podido generar el PDF de la dieta activa.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Replace(' ', '_');
    }

    // POST: api/clients/{clientId}/diets/validate-draft
    [HttpPost("validate-draft")]
    public async Task<ActionResult<List<DietValidationResultDto>>> ValidateDraft(int clientId, [FromBody] DietDetailDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

        var warnings = await _validationService.ValidateDietDraftCompatibilityAsync(
            clientId,
            dto,
            _context,
            AuthHelpers.GetTenantId(User),
            userId.Value,
            User.IsInRole("clinic_admin") || User.IsInRole("superadmin"));
        return Ok(warnings);
    }

    // GET: api/clients/{clientId}/diets/{dietId}/validate
    [HttpGet("{dietId:int}/validate")]
    public async Task<ActionResult<List<DietValidationResultDto>>> ValidateSavedDiet(int clientId, int dietId)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

        var tenantId = AuthHelpers.GetTenantId(User);
        var diet = await _context.diets
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
            .FirstOrDefaultAsync(d => d.id == dietId &&
                (User.IsInRole("superadmin") || (tenantId.HasValue && d.tenant_id == tenantId.Value)));

        if (diet == null) return NotFound("Diet not found.");

        if (!await UserCanAccessDietAsync(dietId, userId.Value))
            return NotFound("Diet not found.");

        var warnings = await _validationService.ValidateDietCompatibilityAsync(
            clientId,
            diet,
            _context,
            AuthHelpers.GetTenantId(User),
            userId.Value,
            User.IsInRole("clinic_admin") || User.IsInRole("superadmin"));
        return Ok(warnings);
    }

    // GET: api/clients/{clientId}/diets/active/shopping-list
    // Devuelve la lista de la compra de la dieta activa como JSON (por categorías)
    [HttpGet("active/shopping-list")]
    public async Task<ActionResult<List<ShoppingCategoryDto>>> GetActiveShoppingList(int clientId)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

        var tenantId = AuthHelpers.GetTenantId(User);
        var activeAssignment = await _context.client_diets
            .Include(cd => cd.diet)
            .Where(cd => cd.client_id == clientId && cd.is_active == true && tenantId.HasValue &&
                         cd.diet != null && cd.diet.tenant_id == tenantId.Value)
            .FirstOrDefaultAsync();

        if (activeAssignment == null)
            return NotFound("No active diet assignment found.");

        var diet = await _context.diets
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
            .FirstOrDefaultAsync(d => d.id == activeAssignment.diet_id && tenantId.HasValue && d.tenant_id == tenantId.Value);

        if (diet == null) return NotFound("Diet definition not found.");

        if (!await UserCanAccessDietAsync(activeAssignment.diet_id, userId.Value))
            return NotFound("Diet definition not found.");

        var foodIds = diet.diet_days.SelectMany(dd => dd.meals).SelectMany(m => m.meal_items)
            .Where(i => i.food_id.HasValue).Select(i => i.food_id!.Value).Distinct().ToList();
        var accessibleFoods = await _context.foods
            .Where(f => foodIds.Contains(f.id) &&
                ((f.source == null || f.source.ToLower() != "local") ||
                 User.IsInRole("superadmin") ||
                 (tenantId.HasValue && f.tenant_id == tenantId.Value && f.created_by_user_id == userId.Value) ||
                 (User.IsInRole("clinic_admin") && tenantId.HasValue && f.tenant_id == tenantId.Value)))
            .ToDictionaryAsync(f => f.id);

        // Consolidar todos los meal_items con alimento concreto
        var grouped = diet.diet_days
            .SelectMany(dd => dd.meals)
            .SelectMany(m => m.meal_items)
            .Where(i => i.food_id.HasValue && accessibleFoods.ContainsKey(i.food_id.Value) && i.grams.HasValue)
            .GroupBy(i => new { FoodId = i.food_id!.Value, FoodName = accessibleFoods[i.food_id.Value].name ?? "Desconocido", Category = accessibleFoods[i.food_id.Value].category ?? "Otros" })
            .Select(g => new ShoppingItemDto
            {
                FoodId = g.Key.FoodId,
                FoodName = g.Key.FoodName,
                Category = g.Key.Category,
                TotalGrams = Math.Round((double)g.Sum(i => i.grams!.Value), 0)
            })
            .GroupBy(s => s.Category)
            .Select(catGroup => new ShoppingCategoryDto
            {
                Category = catGroup.Key,
                Items = catGroup.OrderBy(i => i.FoodName).ToList()
            })
            .OrderBy(c => c.Category)
            .ToList();

        return Ok(grouped);
    }
}
