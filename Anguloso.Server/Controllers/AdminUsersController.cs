using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[Route("api/admin")]
[ApiController]
[Authorize(Roles = "superadmin")]
public class AdminUsersController : ControllerBase
{
    private readonly angulosodbContext _context;

    public AdminUsersController(angulosodbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Métricas globales del sistema para el dashboard del superadministrador.
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var totalUsers = await _context.users.CountAsync(u => u.role != "superadmin");
        var totalClients = await _context.clients.CountAsync();
        var totalDiets = await _context.diets.CountAsync();
        
        var now = DateTime.UtcNow;
        var in7Days = now.AddDays(7);
        var licensesExpiringSoon = await _context.users
            .CountAsync(u => u.role != "superadmin" && 
                             u.license_expires_at != null && 
                             u.license_expires_at <= in7Days && 
                             u.license_expires_at >= now);

        var activeSubscriptions = await _context.users
            .CountAsync(u => u.role != "superadmin" && u.subscription_status == "active");

        return Ok(new
        {
            totalUsers,
            totalClients,
            totalDiets,
            licensesExpiringSoon,
            activeSubscriptions
        });
    }

    /// <summary>
    /// Devuelve todas las líneas de la tabla de configuración para el panel del superadministrador.
    /// </summary>
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig()
    {
        var config = await _context.config
            .AsNoTracking()
            .OrderBy(c => c.id)
            .Select(c => new AdminConfigDto
            {
                Id = c.id,
                Nombre = c.nombre_config,
                Valor = c.valor_config
            })
            .ToListAsync();

        return Ok(config);
    }

    /// <summary>
    /// Actualiza el valor de una línea de configuración.
    /// </summary>
    [HttpPut("config/{id}")]
    public async Task<IActionResult> UpdateConfig(int id, [FromBody] UpdateConfigRequest request)
    {
        if (request == null)
            return BadRequest("Datos de configuración no válidos.");

        var config = await _context.config.FindAsync(id);
        if (config == null)
            return NotFound("Configuración no encontrada.");

        config.valor_config = request.Valor ?? string.Empty;
        await _context.SaveChangesAsync();

        return Ok(new AdminConfigDto
        {
            Id = config.id,
            Nombre = config.nombre_config,
            Valor = config.valor_config
        });
    }

    /// <summary>
    /// Lista paginada y filtrable de todos los usuarios/nutricionistas con sus licencias.
    /// </summary>
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? plan = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.users.AsNoTracking().Where(u => u.role != "superadmin");

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u => u.username.ToLower().Contains(s) ||
                                     (u.email != null && u.email.ToLower().Contains(s)) ||
                                     (u.full_name != null && u.full_name.ToLower().Contains(s)) ||
                                     (u.clinic_name != null && u.clinic_name.ToLower().Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(u => u.subscription_status == status);
        }

        if (!string.IsNullOrWhiteSpace(plan))
        {
            query = query.Where(u => u.subscription_plan == plan);
        }

        var totalCount = await query.CountAsync();

        var usersList = await query
            .OrderByDescending(u => u.created_at)
            .ThenByDescending(u => u.id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserDto
            {
                Id = u.id,
                Username = u.username,
                FullName = u.full_name,
                Email = u.email,
                EmailConfirmed = u.email_confirmed ?? false,
                Role = u.role,
                ClinicName = u.clinic_name,
                CreatedAt = u.created_at,
                LastLogin = u.last_login,
                SubscriptionPlan = u.subscription_plan ?? "free",
                SubscriptionStatus = u.subscription_status ?? "active",
                LicenseExpiresAt = u.license_expires_at,
                MaxClientsAllowed = u.max_clients_allowed ?? 10,
                ClientCount = u.clients.Count()
            })
            .ToListAsync();

        return Ok(new AdminUsersPageDto
        {
            Items = usersList,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// Actualiza la licencia y suscripción de un nutricionista.
    /// </summary>
    [HttpPut("users/{id}/license")]
    public async Task<IActionResult> UpdateLicense(int id, [FromBody] UpdateLicenseRequest request)
    {
        var user = await _context.users.FindAsync(id);
        if (user == null || user.role == "superadmin")
            return NotFound("Usuario no encontrado o no modificable.");

        user.subscription_plan = request.SubscriptionPlan;
        user.subscription_status = request.SubscriptionStatus;
        user.license_expires_at = request.LicenseExpiresAt;
        user.max_clients_allowed = request.MaxClientsAllowed;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Licencia actualizada con éxito." });
    }

    /// <summary>
    /// Suspende la cuenta o suscripción de un usuario.
    /// </summary>
    [HttpPut("users/{id}/suspend")]
    public async Task<IActionResult> SuspendUser(int id)
    {
        var user = await _context.users.FindAsync(id);
        if (user == null || user.role == "superadmin")
            return NotFound("Usuario no encontrado.");

        user.subscription_status = "suspended";
        await _context.SaveChangesAsync();

        return Ok(new { message = "Usuario suspendido correctamente." });
    }

    /// <summary>
    /// Activa la cuenta o suscripción de un usuario.
    /// </summary>
    [HttpPut("users/{id}/activate")]
    public async Task<IActionResult> ActivateUser(int id)
    {
        var user = await _context.users.FindAsync(id);
        if (user == null || user.role == "superadmin")
            return NotFound("Usuario no encontrado.");

        user.subscription_status = "active";
        await _context.SaveChangesAsync();

        return Ok(new { message = "Usuario activado correctamente." });
    }

    /// <summary>
    /// Resetea la contraseña de un usuario directamente desde el panel de administración.
    /// </summary>
    [HttpPut("users/{id}/reset-password")]
    public async Task<IActionResult> ResetUserPassword(int id, [FromBody] ResetPasswordAdminRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            return BadRequest("La nueva contraseña debe tener al menos 6 caracteres.");

        var user = await _context.users.FindAsync(id);
        if (user == null || user.role == "superadmin")
            return NotFound("Usuario no encontrado.");

        user.password_hash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Contraseña restablecida con éxito." });
    }
}

public class AdminConfigDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
}

public class UpdateConfigRequest
{
    public string? Valor { get; set; }
}

public class AdminUsersPageDto
{
    public List<AdminUserDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class AdminUserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public bool EmailConfirmed { get; set; }
    public string? Role { get; set; }
    public string? ClinicName { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? LastLogin { get; set; }
    public string SubscriptionPlan { get; set; } = "free";
    public string SubscriptionStatus { get; set; } = "active";
    public DateTime? LicenseExpiresAt { get; set; }
    public int MaxClientsAllowed { get; set; } = 10;
    public int ClientCount { get; set; }
}

public class UpdateLicenseRequest
{
    public string SubscriptionPlan { get; set; } = "free";
    public string SubscriptionStatus { get; set; } = "active";
    public DateTime? LicenseExpiresAt { get; set; }
    public int MaxClientsAllowed { get; set; } = 10;
}

public class ResetPasswordAdminRequest
{
    public string NewPassword { get; set; } = string.Empty;
}
