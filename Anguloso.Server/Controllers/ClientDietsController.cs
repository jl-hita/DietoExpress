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
[Authorize]
[Route("api/clients/{clientId:int}/diets")]
public class ClientDietsController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly DietPdfService _pdfService;

    public ClientDietsController(angulosodbContext context, DietPdfService pdfService)
    {
        _context = context;
        _pdfService = pdfService;
    }

    private async Task<bool> UserOwnsClientAsync(int clientId, int userId)
    {
        return await _context.clients.AnyAsync(c => c.id == clientId && c.user_id == userId);
    }

    // GET: api/clients/{clientId}/diets
    [HttpGet]
    public async Task<ActionResult<List<ClientDietListDto>>> GetHistory(int clientId)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

        var history = await _context.client_diets
            .Include(cd => cd.diet)
            .Where(cd => cd.client_id == clientId)
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

        var activeAssignment = await _context.client_diets
            .Where(cd => cd.client_id == clientId && cd.is_active == true)
            .FirstOrDefaultAsync();

        if (activeAssignment == null)
            return NotFound("No active diet assignment found for this patient.");

        var d = await _context.diets
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
                        .ThenInclude(i => i.food)
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
                        .ThenInclude(i => i.exchange_group)
            .FirstOrDefaultAsync(d => d.id == activeAssignment.diet_id);

        if (d == null)
            return NotFound("The active diet definition was not found.");

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
                        FoodName = i.food != null ? i.food.name : null,
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

        var dietExists = await _context.diets.AnyAsync(d => d.id == dto.DietId);
        if (!dietExists) return BadRequest("The selected diet does not exist.");

        // Desactivar dietas activas previas
        var activeDiets = await _context.client_diets
            .Where(cd => cd.client_id == clientId && cd.is_active == true)
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

    // PUT: api/clients/{clientId}/diets/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAssignment(int clientId, int id, [FromBody] UpdateClientDietDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

        var assignment = await _context.client_diets.FirstOrDefaultAsync(cd => cd.id == id && cd.client_id == clientId);
        if (assignment == null) return NotFound("Diet assignment not found.");

        // Si se está activando, desactivar otras dietas del mismo cliente para evitar violación de la restricción UNIQUE
        if (dto.IsActive)
        {
            var otherActiveDiets = await _context.client_diets
                .Where(cd => cd.client_id == clientId && cd.is_active == true && cd.id != id)
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
        return NoContent();
    }

    // POST: api/clients/{clientId}/diets/{id}/deactivate
    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> DeactivateAssignment(int clientId, int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

        var assignment = await _context.client_diets.FirstOrDefaultAsync(cd => cd.id == id && cd.client_id == clientId);
        if (assignment == null) return NotFound("Diet assignment not found.");

        assignment.is_active = false;
        assignment.end_date = DateOnly.FromDateTime(DateTime.Today);

        await _context.SaveChangesAsync();
        return NoContent();
    }

    // DELETE: api/clients/{clientId}/diets/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAssignment(int clientId, int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

        var assignment = await _context.client_diets.FirstOrDefaultAsync(cd => cd.id == id && cd.client_id == clientId);
        if (assignment == null) return NotFound("Diet assignment not found.");

        _context.client_diets.Remove(assignment);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // GET: api/clients/{clientId}/diets/{id}/pdf
    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> GetDietPdf(int clientId, int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

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
            .FirstOrDefaultAsync(cd => cd.id == id && cd.client_id == clientId);

        if (assignment == null)
            return NotFound("Diet assignment not found.");

        if (assignment.diet == null)
            return NotFound("Diet definition not found.");

        var pdfBytes = _pdfService.GenerateDietPdf(assignment.client, assignment.diet, assignment, _context);
        
        var fileName = $"Dieta_{assignment.client.full_name.Replace(" ", "_")}_{assignment.diet.name.Replace(" ", "_")}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    // GET: api/clients/{clientId}/diets/active/pdf
    [HttpGet("active/pdf")]
    public async Task<IActionResult> GetActiveDietPdf(int clientId)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (!await UserOwnsClientAsync(clientId, userId.Value))
            return NotFound("Client not found or does not belong to the user.");

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
            .FirstOrDefaultAsync(cd => cd.client_id == clientId && cd.is_active == true);

        if (assignment == null)
            return NotFound("No active diet assignment found for this patient.");

        if (assignment.diet == null)
            return NotFound("Diet definition not found.");

        var pdfBytes = _pdfService.GenerateDietPdf(assignment.client, assignment.diet, assignment, _context);
        
        var fileName = $"Dieta_Activa_{assignment.client.full_name.Replace(" ", "_")}_{assignment.diet.name.Replace(" ", "_")}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }
}
