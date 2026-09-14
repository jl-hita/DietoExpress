using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Anguloso.Server.Controllers;

[Route("api/portal")]
[ApiController]
public class PatientPortalController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly IConfiguration _config;

    public PatientPortalController(angulosodbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    /// <summary>
    /// Autentica al paciente mediante enlace mágico (token) o credenciales (email/teléfono + passcode).
    /// Genera y retorna un JWT exclusivo con rol "patient".
    /// </summary>
    [HttpPost("auth")]
    public async Task<ActionResult<PatientAuthResponseDto>> Authenticate([FromBody] PatientAuthRequestDto request)
    {
        clients? client = null;

        // Vía 1: Autenticación por token de enlace mágico
        if (!string.IsNullOrWhiteSpace(request.Token))
        {
            client = await _context.clients
                .Include(c => c.user)
                .FirstOrDefaultAsync(c => c.access_token == request.Token);

            if (client == null)
                return Unauthorized("Enlace de acceso no válido o caducado.");
        }
        // Vía 2: Autenticación por correo/teléfono + PIN o contraseña
        else if (!string.IsNullOrWhiteSpace(request.EmailOrPhone) && !string.IsNullOrWhiteSpace(request.Passcode))
        {
            var clean = request.EmailOrPhone.Trim().ToLower();
            client = await _context.clients
                .Include(c => c.user)
                .FirstOrDefaultAsync(c => (c.email != null && c.email.ToLower() == clean) || (c.phone != null && c.phone == clean));

            if (client == null)
                return Unauthorized("No se encontró ningún expediente con esos datos.");

            if (string.IsNullOrWhiteSpace(client.passcode_hash) || !BCrypt.Net.BCrypt.Verify(request.Passcode, client.passcode_hash))
            {
                return Unauthorized("Código de acceso o PIN incorrecto.");
            }
        }
        else
        {
            return BadRequest("Debes proporcionar un enlace de acceso o tus credenciales.");
        }

        // Registrar último acceso del paciente
        client.last_portal_access = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var jwt = GeneratePatientJwt(client);

        return Ok(new PatientAuthResponseDto
        {
            Token = jwt,
            ClientId = client.id,
            FullName = client.full_name,
            ClinicName = client.user?.clinic_name,
            ClinicLogo = client.user?.clinic_logo
        });
    }

    /// <summary>
    /// Devuelve el perfil clínico, peso y métricas del paciente autenticado.
    /// </summary>
    [HttpGet("profile")]
    [Authorize]
    public async Task<ActionResult<PatientProfileDto>> GetMyProfile()
    {
        var clientId = ResolveAuthorizedClientId();
        if (clientId == null) return Unauthorized();

        var client = await _context.clients
            .Include(c => c.user)
            .Include(c => c.biometrics)
            .Include(c => c.client_diets)
            .FirstOrDefaultAsync(c => c.id == clientId.Value);

        if (client == null) return NotFound();

        var latestBio = client.biometrics
            .OrderByDescending(b => b.measurement_date)
            .FirstOrDefault();

        var activeDietAssignment = await _context.client_diets
            .FirstOrDefaultAsync(cd => cd.client_id == clientId.Value && cd.is_active == true);

        int? age = null;
        if (client.birth_date.HasValue)
        {
            age = DateTime.Today.Year - client.birth_date.Value.Year;
            if (client.birth_date.Value > DateOnly.FromDateTime(DateTime.Today.AddYears(-age.Value))) age--;
        }

        var weightHistory = client.biometrics
            .Where(b => b.weight.HasValue)
            .OrderByDescending(b => b.measurement_date)
            .Take(12)
            .OrderBy(b => b.measurement_date)
            .Select(b => new WeightEntryDto
            {
                Date = b.measurement_date.ToDateTime(TimeOnly.MinValue),
                Weight = (double)(b.weight ?? 0)
            })
            .ToList();

        return Ok(new PatientProfileDto
        {
            ClientId = client.id,
            FullName = client.full_name ?? string.Empty,
            Age = age,
            Gender = client.gender,
            CurrentWeight = latestBio?.weight.HasValue == true ? (double?)latestBio.weight.Value : null,
            CurrentHeight = latestBio?.height.HasValue == true ? (double?)latestBio.height.Value : null,
            HasActiveDiet = activeDietAssignment != null,
            ActiveDietAssignmentId = activeDietAssignment?.id,
            ActiveDietStartDate = activeDietAssignment?.start_date.ToDateTime(TimeOnly.MinValue),
            WeightHistory = weightHistory,
            ClinicName = client.user?.clinic_name,
            ClinicPhone = client.user?.clinic_phone,
            ClinicLogo = client.user?.clinic_logo,
            NutritionistName = client.user?.full_name
        });
    }

    /// <summary>
    /// Devuelve el plan nutricional activo completo del paciente con sus días, comidas e intercambios.
    /// </summary>
    [HttpGet("diet")]
    [Authorize]
    public async Task<ActionResult<DietDetailDto>> GetMyActiveDiet()
    {
        var clientId = ResolveAuthorizedClientId();
        if (clientId == null) return Unauthorized();

        var activeAssignment = await _context.client_diets
            .FirstOrDefaultAsync(cd => cd.client_id == clientId.Value && cd.is_active == true);

        if (activeAssignment == null)
            return NotFound("No tienes ningún plan nutricional activo asignado en este momento.");

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

        if (d == null) return NotFound("Plan nutricional no encontrado.");

        return Ok(new DietDetailDto
        {
            Id = d.id,
            Name = d.name,
            TargetKcal = d.target_kcal,
            TargetProtein = d.target_protein,
            TargetCarbs = d.target_carbs,
            TargetFat = d.target_fat,
            Days = d.diet_days.OrderBy(dd => dd.day_index).Select(dd => new DietDayDto
            {
                Id = dd.id,
                DayIndex = dd.day_index,
                Meals = dd.meals.OrderBy(m => m.meal_index).Select(m => new MealDto
                {
                    Id = m.id,
                    MealIndex = m.meal_index,
                    Name = m.name,
                    Items = m.meal_items.Select(i => new MealItemDto
                    {
                        Id = i.id,
                        FoodId = i.food_id,
                        FoodName = i.food != null ? i.food.name : null,
                        ExchangeGroupId = i.exchange_group_id,
                        ExchangeGroupName = i.exchange_group != null ? i.exchange_group.name : null,
                        ExchangeCount = i.exchange_count,
                        Grams = i.grams,
                        Kcal = i.kcal,
                        Protein = i.protein,
                        Carbs = i.carbs,
                        Fat = i.fat
                    }).ToList()
                }).ToList()
            }).ToList()
        });
    }

    /// <summary>
    /// Devuelve la lista de la compra calculada a partir del plan activo del paciente.
    /// </summary>
    [HttpGet("shopping-list")]
    [Authorize]
    public async Task<ActionResult<List<ShoppingCategoryDto>>> GetMyShoppingList()
    {
        var clientId = ResolveAuthorizedClientId();
        if (clientId == null) return Unauthorized();

        var activeAssignment = await _context.client_diets
            .FirstOrDefaultAsync(cd => cd.client_id == clientId.Value && cd.is_active == true);

        if (activeAssignment == null)
            return NotFound("No hay plan activo para generar la lista de la compra.");

        var diet = await _context.diets
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
                        .ThenInclude(i => i.food)
            .FirstOrDefaultAsync(d => d.id == activeAssignment.diet_id);

        if (diet == null) return NotFound("Plan no encontrado.");

        var grouped = diet.diet_days
            .SelectMany(dd => dd.meals)
            .SelectMany(m => m.meal_items)
            .Where(i => i.food != null && i.grams.HasValue)
            .GroupBy(i => new { FoodId = i.food!.id, FoodName = i.food!.name ?? "Desconocido", Category = i.food!.category ?? "Otros" })
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

    // ─── ENDPOINTS PARA EL NUTRICIONISTA (Gestión de acceso de su paciente) ───

    /// <summary>
    /// Obtiene o genera el enlace de acceso y estado del portal para un paciente.
    /// </summary>
    [HttpGet("~/api/clients/{clientId:int}/portal-access")]
    [Authorize]
    public async Task<ActionResult<ClientPortalAccessDto>> GetClientPortalAccess(int clientId)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var client = await _context.clients.FirstOrDefaultAsync(c => c.id == clientId && c.user_id == userId.Value);
        if (client == null) return NotFound("Cliente no encontrado.");

        // Si aún no tiene token de acceso único, generar uno
        if (string.IsNullOrWhiteSpace(client.access_token))
        {
            client.access_token = GenerateUrlSafeToken();
            await _context.SaveChangesAsync();
        }

        var magicLink = $"/patient?token={client.access_token}";

        return Ok(new ClientPortalAccessDto
        {
            ClientId = client.id,
            AccessToken = client.access_token,
            MagicLink = magicLink,
            HasPasscode = !string.IsNullOrWhiteSpace(client.passcode_hash),
            LastPortalAccess = client.last_portal_access
        });
    }

    /// <summary>
    /// Regenera el token de acceso para revocar enlaces antiguos si es necesario.
    /// </summary>
    [HttpPost("~/api/clients/{clientId:int}/portal-access/regenerate-token")]
    [Authorize]
    public async Task<ActionResult<ClientPortalAccessDto>> RegenerateToken(int clientId)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var client = await _context.clients.FirstOrDefaultAsync(c => c.id == clientId && c.user_id == userId.Value);
        if (client == null) return NotFound("Cliente no encontrado.");

        client.access_token = GenerateUrlSafeToken();
        await _context.SaveChangesAsync();

        return Ok(new ClientPortalAccessDto
        {
            ClientId = client.id,
            AccessToken = client.access_token,
            MagicLink = $"/patient?token={client.access_token}",
            HasPasscode = !string.IsNullOrWhiteSpace(client.passcode_hash),
            LastPortalAccess = client.last_portal_access
        });
    }

    /// <summary>
    /// Asigna o cambia el código PIN de acceso del paciente.
    /// </summary>
    [HttpPost("~/api/clients/{clientId:int}/portal-access/passcode")]
    [Authorize]
    public async Task<IActionResult> SetPasscode(int clientId, [FromBody] SetClientPasscodeDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(dto.Passcode) || dto.Passcode.Length < 4)
            return BadRequest("El código de acceso debe tener al menos 4 caracteres.");

        var client = await _context.clients.FirstOrDefaultAsync(c => c.id == clientId && c.user_id == userId.Value);
        if (client == null) return NotFound("Cliente no encontrado.");

        client.passcode_hash = BCrypt.Net.BCrypt.HashPassword(dto.Passcode);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Código de acceso asignado con éxito." });
    }

    // ─── MÉTODOS AUXILIARES ───

    private int? ResolveAuthorizedClientId()
    {
        // Caso 1: Paciente autenticado con su propio JWT
        if (AuthHelpers.IsPatient(User))
        {
            return AuthHelpers.GetClientId(User);
        }

        // Caso 2: Nutricionista visualizando (requiere parámetro o contexto)
        if (Request.Query.TryGetValue("clientId", out var cidStr) && int.TryParse(cidStr, out var cid))
        {
            var userId = AuthHelpers.GetUserId(User);
            if (userId.HasValue && _context.clients.Any(c => c.id == cid && c.user_id == userId.Value))
            {
                return cid;
            }
        }

        return null;
    }

    private string GeneratePatientJwt(clients client)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"]!);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, client.id.ToString()),
            new Claim("clientId", client.id.ToString()),
            new Claim(ClaimTypes.Name, client.full_name ?? "Paciente"),
            new Claim(ClaimTypes.Role, "patient")
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(30), // El portal del paciente se mantiene activo 30 días
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private static string GenerateUrlSafeToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }
}
