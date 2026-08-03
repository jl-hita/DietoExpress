using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize] // opcional: añade si usas auth
public class ClientsController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly EnergyCalculatorService _calculatorService;

    public ClientsController(angulosodbContext context, EnergyCalculatorService calculatorService)
    {
        _context = context;
        _calculatorService = calculatorService;
    }

    // GET: api/clients
    [HttpGet]
    public async Task<ActionResult<List<ClientListDto>>> GetClients()
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var list = await _context.clients
            .Where(c => c.user_id == userId.Value)
            .OrderByDescending(c => c.created_at)
            .Select(c => new ClientListDto
            {
                Id = c.id,
                FullName = c.full_name,
                Email = c.email,
                Phone = c.phone,
                Gender = c.gender,
                BirthDate = c.birth_date.HasValue ? new DateTime?(c.birth_date.Value.ToDateTime(TimeOnly.MinValue)) : null,
                CreatedAt = c.created_at
            })
            .ToListAsync();

        return Ok(list);
    }

    // GET: api/clients/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClientDetailDto>> GetClient(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var client = await _context.clients
            .Include(c => c.biometrics)
            .FirstOrDefaultAsync(c => c.id == id && c.user_id == userId.Value);

        if (client == null) return NotFound();

        var dto = new ClientDetailDto
        {
            Id = client.id,
            FullName = client.full_name,
            Email = client.email,
            Phone = client.phone,
            Gender = client.gender,
            BirthDate = client.birth_date.HasValue ? new DateTime?(client.birth_date.Value.ToDateTime(TimeOnly.MinValue)) : null,
            CreatedAt = client.created_at,
            Notes = client.notes
        };

        dto.Biometrics = client.biometrics
            .OrderByDescending(b => b.measurement_date)
            .Select(b => new BiometricsDto
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
                Notes = b.notes
            }).ToList();

        return Ok(dto);
    }

    // POST: api/clients
    [HttpPost]
    public async Task<ActionResult> CreateClient([FromBody] CreateClientDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var client = new clients
        {
            user_id = userId.Value,
            full_name = dto.FullName,
            email = dto.Email ?? "",
            phone = dto.Phone ?? "",
            gender = dto.Gender ?? "",
            notes = dto.Notes ?? "",
            created_at = DateTime.UtcNow
        };

        if (dto.BirthDate.HasValue)
            client.birth_date = DateOnly.FromDateTime(dto.BirthDate.Value);

        _context.clients.Add(client);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetClient), new { id = client.id }, new { id = client.id });
    }

    // PUT: api/clients/5
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateClient(int id, [FromBody] UpdateClientDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var client = await _context.clients.FirstOrDefaultAsync(c => c.id == id && c.user_id == userId.Value);
        if (client == null) return NotFound();

        client.full_name = dto.FullName;
        client.email = dto.Email ?? client.email;
        client.phone = dto.Phone ?? client.phone;
        client.gender = dto.Gender ?? client.gender;
        client.notes = dto.Notes ?? client.notes;
        if (dto.BirthDate.HasValue) client.birth_date = DateOnly.FromDateTime(dto.BirthDate.Value);

        await _context.SaveChangesAsync();
        return NoContent();
    }

    // DELETE: api/clients/5
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteClient(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var client = await _context.clients.Include(c => c.biometrics).FirstOrDefaultAsync(c => c.id == id && c.user_id == userId.Value);
        if (client == null) return NotFound();

        // Optionally: delete biometrics cascade if not configured
        _context.biometrics.RemoveRange(client.biometrics);
        _context.clients.Remove(client);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // GET: api/clients/{id}/energy-requirements
    [HttpGet("{id:int}/energy-requirements")]
    public async Task<ActionResult<EnergyRequirementsDto>> GetEnergyRequirements(int id)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var client = await _context.clients
            .Include(c => c.biometrics)
            .FirstOrDefaultAsync(c => c.id == id && c.user_id == userId.Value);

        if (client == null) return NotFound("Client not found.");

        if (!client.birth_date.HasValue)
        {
            return BadRequest("El paciente debe tener una fecha de nacimiento registrada para calcular sus necesidades calóricas.");
        }

        var latestBiometrics = client.biometrics
            .Where(b => b.weight.HasValue && b.height.HasValue)
            .OrderByDescending(b => b.measurement_date)
            .FirstOrDefault();

        if (latestBiometrics == null)
        {
            return BadRequest("El paciente debe tener al menos un registro biométrico con peso y altura para calcular sus necesidades calóricas.");
        }

        int age = DateTime.Today.Year - client.birth_date.Value.Year;
        if (client.birth_date.Value > DateOnly.FromDateTime(DateTime.Today.AddYears(-age))) age--;

        double weight = (double)latestBiometrics.weight.Value;
        double height = (double)latestBiometrics.height.Value;
        double? bodyFat = latestBiometrics.body_fat.HasValue ? (double?)latestBiometrics.body_fat.Value : null;

        var result = _calculatorService.CalculateEnergyRequirements(weight, height, age, client.gender, bodyFat);
        return Ok(result);
    }
}