using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly ILicenseService _licenseService;

    public PatientPortalController(angulosodbContext context, IConfiguration config, ILicenseService licenseService)
    {
        _context = context;
        _config = config;
        _licenseService = licenseService;
    }

    /// <summary>
    /// Autentica al paciente mediante enlace mágico (token) o credenciales (email/teléfono + passcode).
    /// Genera y retorna un JWT exclusivo con rol "patient".
    /// </summary>
[EnableRateLimiting("auth")]
    [HttpPost("auth")]
    public async Task<ActionResult<PatientAuthResponseDto>> Authenticate([FromBody] PatientAuthRequestDto request)
    {
        clients? client = null;
        if (!string.IsNullOrWhiteSpace(request.Token) && request.Token.Length <= 256)
        {
            client = await _context.clients
                .Include(c => c.user)
                .FirstOrDefaultAsync(c => c.archived_at == null && c.access_token == HashAccessToken(request.Token) &&
                    c.access_token_expires_at.HasValue && c.access_token_expires_at > DateTime.UtcNow);
            if (client == null) return Unauthorized("Enlace de acceso no válido o caducado.");
        }
        else if (!string.IsNullOrWhiteSpace(request.EmailOrPhone) && request.EmailOrPhone.Length <= 320 && !string.IsNullOrWhiteSpace(request.Passcode) && request.Passcode.Length <= 128)
        {
            var clean = request.EmailOrPhone.Trim().ToLower();
            var candidates = await _context.clients
                .Include(c => c.user)
                .Where(c => c.archived_at == null &&
                    ((c.email != null && c.email.ToLower() == clean) || (c.phone != null && c.phone == clean)))
                .Take(2)
                .ToListAsync();
            if (candidates.Count != 1) return Unauthorized("Los datos de acceso no son válidos.");
            client = candidates[0];
            if (string.IsNullOrWhiteSpace(client.passcode_hash) || !BCrypt.Net.BCrypt.Verify(request.Passcode, client.passcode_hash))
                return Unauthorized("Los datos de acceso no son válidos.");
        }
        else return BadRequest("Debes proporcionar un enlace de acceso o tus credenciales.");

        var portalTenantId = client.tenant_id ?? client.user?.tenant_id;
        if (!await _licenseService.CanUseFeatureAsync(portalTenantId, "CLIENT_PORTAL")) return Forbid();

        // Consumo atómico del enlace mágico: dos peticiones concurrentes no pueden
        // reutilizar el mismo token para crear dos sesiones.
        if (!string.IsNullOrWhiteSpace(request.Token))
        {
            var consumed = await _context.clients
                .Where(c => c.id == client.id &&
                            c.access_token == request.Token &&
                            c.access_token_expires_at.HasValue &&
                            c.access_token_expires_at > DateTime.UtcNow)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.access_token, (string?)null)
                    .SetProperty(c => c.access_token_expires_at, (DateTime?)null));

            if (consumed != 1)
                return Unauthorized("Enlace de acceso no válido o ya utilizado.");

            // Sincronizamos la entidad EF rastreada para que SaveChanges no
            // vuelva a escribir el token consumido en la base de datos.
            client.access_token = null;
            client.access_token_expires_at = null;
        }

        client.last_portal_access = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        var jwt = GeneratePatientJwt(client);
        SetPatientSessionCookie(jwt);

        return Ok(new PatientAuthResponseDto
        {
            Token = null, ClientId = client.id, FullName = client.full_name,
            ClinicName = client.user?.clinic_name, ClinicLogo = client.user?.clinic_logo
        });
    }

    [HttpGet("profile")]
    [Authorize]
    public async Task<ActionResult<PatientProfileDto>> GetMyProfile()
    {
        var clientId = ResolveAuthorizedClientId();
        if (clientId == null) return Unauthorized();
        if (!await PortalFeatureAllowedAsync(clientId.Value)) return Forbid();
        var client = await _context.clients.Include(c => c.user).Include(c => c.biometrics).Include(c => c.client_diets)
            .FirstOrDefaultAsync(c => c.id == clientId.Value && c.archived_at == null);
        if (client == null) return NotFound();
        var latestBio = client.biometrics.OrderByDescending(b => b.measurement_date).FirstOrDefault();
        var activeDietAssignment = await _context.client_diets
            .Include(cd => cd.diet)
            .Where(cd => cd.client_id == clientId.Value && cd.is_active == true && cd.diet != null &&
                         cd.diet.tenant_id == _context.clients.Where(c => c.id == clientId.Value).Select(c => c.tenant_id).FirstOrDefault())
            .FirstOrDefaultAsync();
        int? age = null;
        if (client.birth_date.HasValue)
        {
            age = DateTime.Today.Year - client.birth_date.Value.Year;
            if (client.birth_date.Value > DateOnly.FromDateTime(DateTime.Today.AddYears(-age.Value))) age--;
        }
        var weightHistory = client.biometrics.Where(b => b.weight.HasValue).OrderByDescending(b => b.measurement_date).Take(12).OrderBy(b => b.measurement_date)
            .Select(b => new WeightEntryDto { Date = b.measurement_date.ToDateTime(TimeOnly.MinValue), Weight = (double)(b.weight ?? 0) }).ToList();
        return Ok(new PatientProfileDto
        {
            ClientId = client.id, FullName = client.full_name ?? string.Empty, Age = age, Gender = client.gender,
            CurrentWeight = latestBio?.weight.HasValue == true ? (double?)latestBio.weight.Value : null,
            CurrentHeight = latestBio?.height.HasValue == true ? (double?)latestBio.height.Value : null,
            HasActiveDiet = activeDietAssignment != null, ActiveDietAssignmentId = activeDietAssignment?.id,
            ActiveDietStartDate = activeDietAssignment?.start_date.ToDateTime(TimeOnly.MinValue), WeightHistory = weightHistory,
            ClinicName = client.user?.clinic_name, ClinicPhone = client.user?.clinic_phone, ClinicLogo = client.user?.clinic_logo,
            NutritionistName = client.user?.full_name
        });
    }

    [HttpGet("diet")]
    [Authorize]
    public async Task<ActionResult<DietDetailDto>> GetMyActiveDiet()
    {
        var clientId = ResolveAuthorizedClientId();
        if (clientId == null) return Unauthorized();
        if (!await PortalFeatureAllowedAsync(clientId.Value)) return Forbid();
        var activeAssignment = await _context.client_diets
            .Include(cd => cd.diet)
            .Where(cd => cd.client_id == clientId.Value && cd.is_active == true && cd.diet != null &&
                         cd.diet.tenant_id == _context.clients.Where(c => c.id == clientId.Value).Select(c => c.tenant_id).FirstOrDefault())
            .FirstOrDefaultAsync();
        if (activeAssignment == null) return NotFound("No tienes ningún plan nutricional activo asignado en este momento.");
        var d = await _context.diets.Include(d => d.diet_days).ThenInclude(dd => dd.meals).ThenInclude(m => m.meal_items)
            .Include(d => d.diet_days).ThenInclude(dd => dd.meals).ThenInclude(m => m.meal_items).ThenInclude(i => i.exchange_group)
            .FirstOrDefaultAsync(d => d.id == activeAssignment.diet_id &&
                             d.tenant_id == _context.clients.Where(c => c.id == clientId.Value).Select(c => c.tenant_id).FirstOrDefault());
        if (d == null) return NotFound("Plan nutricional no encontrado.");

        var foodIds = d.diet_days.SelectMany(dd => dd.meals).SelectMany(m => m.meal_items)
            .Where(i => i.food_id.HasValue).Select(i => i.food_id!.Value).Distinct().ToList();
        var accessibleFoods = await _context.foods
            .Where(f => foodIds.Contains(f.id) &&
                ((f.source == null || f.source.ToLower() != "local") || f.tenant_id == d.tenant_id))
            .ToDictionaryAsync(f => f.id);

        return Ok(new DietDetailDto
        {
            Id = d.id, Name = d.name, TargetKcal = d.target_kcal, TargetProtein = d.target_protein, TargetCarbs = d.target_carbs, TargetFat = d.target_fat,
            Days = d.diet_days.OrderBy(dd => dd.day_index).Select(dd => new DietDayDto
            {
                Id = dd.id, DayIndex = dd.day_index,
                Meals = dd.meals.OrderBy(m => m.meal_index).Select(m => new MealDto
                {
                    Id = m.id, MealIndex = m.meal_index, Name = m.name,
                    Items = m.meal_items.Select(i => new MealItemDto
                    {
                        Id = i.id, FoodId = i.food_id, FoodName = i.food_id.HasValue && accessibleFoods.TryGetValue(i.food_id.Value, out var accessibleFood) ? accessibleFood.name : null,
                        ExchangeGroupId = i.exchange_group_id, ExchangeGroupName = i.exchange_group != null ? i.exchange_group.name : null,
                        ExchangeCount = i.exchange_count, Grams = i.grams, Kcal = i.kcal, Protein = i.protein, Carbs = i.carbs, Fat = i.fat
                    }).ToList()
                }).ToList()
            }).ToList()
        });
    }

    [HttpGet("shopping-list")]
    [Authorize]
    public async Task<ActionResult<List<ShoppingCategoryDto>>> GetMyShoppingList()
    {
        var clientId = ResolveAuthorizedClientId();
        if (clientId == null) return Unauthorized();
        if (!await PortalFeatureAllowedAsync(clientId.Value)) return Forbid();
        var activeAssignment = await _context.client_diets
            .Include(cd => cd.diet)
            .Where(cd => cd.client_id == clientId.Value && cd.is_active == true && cd.diet != null &&
                         cd.diet.tenant_id == _context.clients.Where(c => c.id == clientId.Value).Select(c => c.tenant_id).FirstOrDefault())
            .FirstOrDefaultAsync();
        if (activeAssignment == null) return NotFound("No hay plan activo para generar la lista de la compra.");
        var diet = await _context.diets.Include(d => d.diet_days).ThenInclude(dd => dd.meals).ThenInclude(m => m.meal_items)
            .FirstOrDefaultAsync(d => d.id == activeAssignment.diet_id &&
                             d.tenant_id == _context.clients.Where(c => c.id == clientId.Value).Select(c => c.tenant_id).FirstOrDefault());
        if (diet == null) return NotFound("Plan no encontrado.");

        var shoppingFoodIds = diet.diet_days.SelectMany(dd => dd.meals).SelectMany(m => m.meal_items)
            .Where(i => i.food_id.HasValue).Select(i => i.food_id!.Value).Distinct().ToList();
        var shoppingFoods = await _context.foods
            .Where(f => shoppingFoodIds.Contains(f.id) &&
                ((f.source == null || f.source.ToLower() != "local") || f.tenant_id == diet.tenant_id))
            .ToDictionaryAsync(f => f.id);

        var grouped = diet.diet_days.SelectMany(dd => dd.meals).SelectMany(m => m.meal_items).Where(i => i.food_id.HasValue && shoppingFoods.ContainsKey(i.food_id.Value) && i.grams.HasValue)
            .GroupBy(i => new { FoodId = i.food_id!.Value, FoodName = shoppingFoods[i.food_id.Value].name ?? "Desconocido", Category = shoppingFoods[i.food_id.Value].category ?? "Otros" })
            .Select(g => new ShoppingItemDto { FoodId = g.Key.FoodId, FoodName = g.Key.FoodName, Category = g.Key.Category, TotalGrams = Math.Round((double)g.Sum(i => i.grams!.Value), 0) })
            .GroupBy(s => s.Category).Select(catGroup => new ShoppingCategoryDto { Category = catGroup.Key, Items = catGroup.OrderBy(i => i.FoodName).ToList() }).OrderBy(c => c.Category).ToList();
        return Ok(grouped);
    }

    [HttpGet("~/api/clients/{clientId:int}/portal-access")]
    [Authorize(Policy = "Professional")]
    public async Task<ActionResult<ClientPortalAccessDto>> GetClientPortalAccess(int clientId)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        var client = await _context.clients.FirstOrDefaultAsync(c => c.id == clientId && c.archived_at == null &&
            (User.IsInRole("superadmin") ||
             (AuthHelpers.GetTenantId(User).HasValue && c.tenant_id == AuthHelpers.GetTenantId(User)!.Value &&
              (c.user_id == userId.Value ||
               _context.client_nutritionist_assignments.Any(a => a.client_id == c.id && a.nutritionist_id == userId.Value && a.is_active)))));
        if (client == null) return NotFound("Cliente no encontrado.");
        // El token se almacena únicamente como hash y, por tanto, no puede recuperarse.
        // Si ya existe uno, no debemos devolver el hash como si fuera un bearer token:
        // el paciente lo volvería a hashear y la autenticación fallaría.
        if (!string.IsNullOrWhiteSpace(client.access_token))
        {
            return Ok(new ClientPortalAccessDto
            {
                ClientId = client.id,
                AccessToken = string.Empty,
                MagicLink = string.Empty,
                HasPasscode = !string.IsNullOrWhiteSpace(client.passcode_hash),
                LastPortalAccess = client.last_portal_access
            });
        }

        var rawToken = GenerateUrlSafeToken();
        client.access_token = HashAccessToken(rawToken);
        client.access_token_expires_at = DateTime.UtcNow.AddHours(24);
        await _context.SaveChangesAsync();

        return Ok(new ClientPortalAccessDto
        {
            ClientId = client.id,
            AccessToken = rawToken,
            MagicLink = $"/patient?token={rawToken}",
            HasPasscode = !string.IsNullOrWhiteSpace(client.passcode_hash),
            LastPortalAccess = client.last_portal_access
        });
    }

    [HttpPost("~/api/clients/{clientId:int}/portal-access/regenerate-token")]
    [Authorize(Policy = "Professional")]
    public async Task<ActionResult<ClientPortalAccessDto>> RegenerateToken(int clientId)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        var client = await _context.clients.FirstOrDefaultAsync(c => c.id == clientId && c.archived_at == null && (User.IsInRole("superadmin") ||
            (AuthHelpers.GetTenantId(User).HasValue && c.tenant_id == AuthHelpers.GetTenantId(User)!.Value &&
             (c.user_id == userId.Value ||
              _context.client_nutritionist_assignments.Any(a => a.client_id == c.id && a.nutritionist_id == userId.Value && a.is_active)))));
        if (client == null) return NotFound("Cliente no encontrado.");
        var rawToken = GenerateUrlSafeToken();
        client.access_token = HashAccessToken(rawToken);
        client.access_token_expires_at = DateTime.UtcNow.AddHours(24);
        client.portal_token_version++;
        await _context.SaveChangesAsync();
        return Ok(new ClientPortalAccessDto { ClientId = client.id, AccessToken = rawToken, MagicLink = $"/patient?token={rawToken}", HasPasscode = !string.IsNullOrWhiteSpace(client.passcode_hash), LastPortalAccess = client.last_portal_access });
    }

    [HttpPost("~/api/clients/{clientId:int}/portal-access/passcode")]
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> SetPasscode(int clientId, [FromBody] SetClientPasscodeDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(dto.Passcode) || !System.Text.RegularExpressions.Regex.IsMatch(dto.Passcode, @"^\\d{6}$")) return BadRequest("El PIN debe tener exactamente 6 dígitos.");
        var client = await _context.clients.FirstOrDefaultAsync(c => c.id == clientId && AuthHelpers.GetTenantId(User).HasValue && c.tenant_id == AuthHelpers.GetTenantId(User)!.Value && c.user_id == userId.Value);
        if (client == null) return NotFound("Cliente no encontrado.");
        client.passcode_hash = BCrypt.Net.BCrypt.HashPassword(dto.Passcode);
        client.portal_token_version++;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Código de acceso asignado con éxito." });
    }

    private async Task<bool> PortalFeatureAllowedAsync(int clientId)
    {
        var tenantId = await _context.clients.AsNoTracking().Where(c => c.id == clientId && c.archived_at == null).Select(c => c.tenant_id ?? c.user!.tenant_id).FirstOrDefaultAsync();
        return await _licenseService.CanUseFeatureAsync(tenantId, "CLIENT_PORTAL");
    }

    private int? ResolveAuthorizedClientId()
    {
        if (AuthHelpers.IsPatient(User)) return AuthHelpers.GetClientId(User);
        if (Request.Query.TryGetValue("clientId", out var cidStr) && int.TryParse(cidStr, out var cid))
        {
            var userId = AuthHelpers.GetUserId(User);
            if (userId.HasValue && _context.clients.Any(c => c.id == cid && c.archived_at == null &&
                (AuthHelpers.GetTenantId(User).HasValue && c.tenant_id == AuthHelpers.GetTenantId(User)!.Value &&
                 (c.user_id == userId.Value ||
                  _context.client_nutritionist_assignments.Any(a => a.client_id == c.id && a.nutritionist_id == userId.Value && a.is_active))))) return cid;
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
            new Claim(ClaimTypes.Role, "patient"),
            new Claim("portalTokenVersion", client.portal_token_version.ToString())
        };
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = _config["Jwt:Issuer"],
            Audience = _config["Jwt:Audience"],
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(8),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    [HttpPost("logout")]
    [Authorize(Roles = "patient")]
    public async Task<IActionResult> Logout()
    {
        var clientId = AuthHelpers.GetClientId(User);
        if (clientId.HasValue)
        {
            var client = await _context.clients.FirstOrDefaultAsync(c => c.id == clientId.Value && c.archived_at == null);
            if (client != null)
            {
                client.portal_token_version++;
                await _context.SaveChangesAsync();
            }
        }

        Response.Cookies.Delete("dietoexpress_patient_session", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
        return NoContent();
    }

    private void SetPatientSessionCookie(string jwt)
    {
        Response.Cookies.Append("dietoexpress_patient_session", jwt, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddHours(8),
            MaxAge = TimeSpan.FromHours(8),
            Path = "/"
        });
    }

    private static string HashAccessToken(string token) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string GenerateUrlSafeToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }
}
