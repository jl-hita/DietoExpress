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

[Route("api/clients/{clientId:int}/[controller]")]
[ApiController]
[Authorize]
public class BiometricsController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly AnthropometryCalculatorService _calculatorService;

    public BiometricsController(angulosodbContext context, AnthropometryCalculatorService calculatorService)
    {
        _context = context;
        _calculatorService = calculatorService;
    }

    // GET: api/clients/{clientId}/biometrics
    [HttpGet]
    public async Task<ActionResult<List<BiometricsDto>>> GetAll(int clientId)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var client = await _context.clients.AsNoTracking().FirstOrDefaultAsync(c => c.id == clientId && c.user_id == userId.Value);
        if (client == null) return NotFound();

        int? age = null;
        if (client.birth_date.HasValue)
        {
            age = DateTime.Today.Year - client.birth_date.Value.Year;
            if (client.birth_date.Value > DateOnly.FromDateTime(DateTime.Today.AddYears(-age.Value))) age--;
        }

        var biometricsList = await _context.biometrics
            .Where(b => b.client_id == clientId)
            .OrderByDescending(b => b.measurement_date)
            .ToListAsync();

        var list = biometricsList.Select(b => MapToDto(b, client.gender, age)).ToList();

        return Ok(list);
    }

    // GET: api/clients/{clientId}/biometrics/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<BiometricsDto>> Get(int clientId, int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var b = await _context.biometrics
            .Include(x => x.client)
            .FirstOrDefaultAsync(x => x.id == id && x.client_id == clientId && x.client.user_id == userId.Value);

        if (b == null) return NotFound();

        int? age = null;
        if (b.client.birth_date.HasValue)
        {
            age = DateTime.Today.Year - b.client.birth_date.Value.Year;
            if (b.client.birth_date.Value > DateOnly.FromDateTime(DateTime.Today.AddYears(-age.Value))) age--;
        }

        var dto = MapToDto(b, b.client.gender, age);

        return Ok(dto);
    }

    // POST: api/clients/{clientId}/biometrics
    [HttpPost]
    public async Task<ActionResult> Create(int clientId, [FromBody] CreateBiometricDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var client = await _context.clients.FirstOrDefaultAsync(c => c.id == clientId && c.user_id == userId.Value);
        if (client == null) return NotFound();

        var b = new biometrics
        {
            client_id = clientId,
            measurement_date = DateOnly.FromDateTime(dto.MeasurementDate),
            weight = dto.Weight,
            height = dto.Height,
            body_fat = dto.BodyFat,
            muscle_mass = dto.MuscleMass,
            visceral_fat = dto.VisceralFat,
            waist = dto.Waist,
            hip = dto.Hip,
            neck = dto.Neck,
            triceps = dto.Triceps,
            abdomen = dto.Abdomen,
            thigh = dto.Thigh,
            subscapular = dto.Subscapular,
            suprailiac = dto.Suprailiac,
            biceps = dto.Biceps,
            chest = dto.Chest,
            axilla = dto.Axilla,
            calf_skinfold = dto.CalfSkinfold,
            arm_perimeter = dto.ArmPerimeter,
            calf_perimeter = dto.CalfPerimeter,
            wrist_diameter = dto.WristDiameter,
            femur_diameter = dto.FemurDiameter,
            humerus_diameter = dto.HumerusDiameter,
            notes = dto.Notes ?? ""
        };

        _context.biometrics.Add(b);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { clientId = clientId, id = b.id }, new { id = b.id });
    }

    // PUT: api/clients/{clientId}/biometrics/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int clientId, int id, [FromBody] UpdateBiometricDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var b = await _context.biometrics.Include(x => x.client)
            .FirstOrDefaultAsync(x => x.id == id && x.client_id == clientId && x.client.user_id == userId.Value);

        if (b == null) return NotFound();

        b.measurement_date = DateOnly.FromDateTime(dto.MeasurementDate);
        b.weight = dto.Weight;
        b.height = dto.Height;
        b.body_fat = dto.BodyFat;
        b.muscle_mass = dto.MuscleMass;
        b.visceral_fat = dto.VisceralFat;
        b.waist = dto.Waist;
        b.hip = dto.Hip;
        b.neck = dto.Neck;
        b.triceps = dto.Triceps;
        b.abdomen = dto.Abdomen;
        b.thigh = dto.Thigh;
        b.subscapular = dto.Subscapular;
        b.suprailiac = dto.Suprailiac;
        b.biceps = dto.Biceps;
        b.chest = dto.Chest;
        b.axilla = dto.Axilla;
        b.calf_skinfold = dto.CalfSkinfold;
        b.arm_perimeter = dto.ArmPerimeter;
        b.calf_perimeter = dto.CalfPerimeter;
        b.wrist_diameter = dto.WristDiameter;
        b.femur_diameter = dto.FemurDiameter;
        b.humerus_diameter = dto.HumerusDiameter;
        b.notes = dto.Notes ?? b.notes;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    // DELETE: api/clients/{clientId}/biometrics/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int clientId, int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var b = await _context.biometrics.Include(x => x.client)
            .FirstOrDefaultAsync(x => x.id == id && x.client_id == clientId && x.client.user_id == userId.Value);

        if (b == null) return NotFound();

        _context.biometrics.Remove(b);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // GET: api/clients/{clientId}/evolution
    [HttpGet("/api/clients/{clientId:int}/evolution")]
    public async Task<ActionResult<List<BiometricsDto>>> GetEvolution(int clientId)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var client = await _context.clients.AsNoTracking().FirstOrDefaultAsync(c => c.id == clientId && c.user_id == userId.Value);
        if (client == null) return NotFound();

        int? age = null;
        if (client.birth_date.HasValue)
        {
            age = DateTime.Today.Year - client.birth_date.Value.Year;
            if (client.birth_date.Value > DateOnly.FromDateTime(DateTime.Today.AddYears(-age.Value))) age--;
        }

        var biometricsList = await _context.biometrics
            .Where(b => b.client_id == clientId)
            .OrderBy(b => b.measurement_date) // Orden cronológico ascendente para gráficos
            .ToListAsync();

        var evolution = biometricsList.Select(b => MapToDto(b, client.gender, age)).ToList();

        return Ok(evolution);
    }

    private BiometricsDto MapToDto(biometrics b, string gender, int? age)
    {
        var dto = new BiometricsDto
        {
            Id = b.id,
            MeasurementDate = b.measurement_date.ToDateTime(TimeOnly.MinValue),
            Weight = b.weight,
            Height = b.height,
            BodyFat = b.body_fat,
            MuscleMass = b.muscle_mass,
            VisceralFat = b.visceral_fat,
            Waist = b.waist,
            Hip = b.hip,
            Neck = b.neck,
            Triceps = b.triceps,
            Abdomen = b.abdomen,
            Thigh = b.thigh,
            Subscapular = b.subscapular,
            Suprailiac = b.suprailiac,
            Biceps = b.biceps,
            Chest = b.chest,
            Axilla = b.axilla,
            CalfSkinfold = b.calf_skinfold,
            ArmPerimeter = b.arm_perimeter,
            CalfPerimeter = b.calf_perimeter,
            WristDiameter = b.wrist_diameter,
            FemurDiameter = b.femur_diameter,
            HumerusDiameter = b.humerus_diameter,
            Notes = b.notes,
            Bmi = b.weight.HasValue && b.height.HasValue && b.height.Value > 0
                ? Math.Round(b.weight.Value / Math.Pow(b.height.Value / 100.0, 2), 2)
                : null
        };

        dto.Analysis = _calculatorService.Calculate(b, gender, age);
        return dto;
    }
}