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
        var totalUsers = await _context.users.CountAsync(u => u.role == "nutritionist" || u.role == "user");
        var totalClients = await _context.clients.CountAsync();
        var totalDiets = await _context.diets.CountAsync();
        
        var now = DateTime.UtcNow;
        var in7Days = now.AddDays(7);
        var licensesExpiringSoon = await _context.subscriptions
            .CountAsync(s => s.expires_at != null && s.expires_at <= in7Days && s.expires_at >= now && s.status == "active");

        var activeSubscriptions = await _context.subscriptions
            .CountAsync(s => s.status == "active" && (s.expires_at == null || s.expires_at > now));

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

    [HttpGet("logs")]
    public async Task<IActionResult> GetLog([FromQuery] string? date = null)
    {
        DateTime requestedDate;

        if (string.IsNullOrWhiteSpace(date))
            requestedDate = DateTime.Today;
        else if (!DateTime.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out requestedDate))
            return BadRequest("La fecha debe tener el formato yyyy-MM-dd.");

        requestedDate = requestedDate.Date;

        var logsPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
        Directory.CreateDirectory(logsPath);

        var logFiles = Directory.GetFiles(logsPath, "log-*.txt")
            .Select(path => new { Path = path, Date = TryGetLogFileDate(path) })
            .Where(x => x.Date.HasValue)
            .Select(x => new { x.Path, Date = x.Date!.Value })
            .OrderBy(x => x.Date)
            .ToList();

        var selected = logFiles.FirstOrDefault(x => x.Date == requestedDate);
        var content = string.Empty;

        if (selected != null)
        {
            await using var stream = new FileStream(selected.Path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
            using var reader = new StreamReader(stream);
            content = await reader.ReadToEndAsync();
        }

        var previousDate = logFiles.Where(x => x.Date < requestedDate).Select(x => (DateTime?)x.Date).LastOrDefault();
        var nextDate = logFiles.Where(x => x.Date > requestedDate).Select(x => (DateTime?)x.Date).FirstOrDefault();

        return Ok(new
        {
            date = requestedDate.ToString("yyyy-MM-dd"),
            exists = selected != null,
            content,
            previousDate = previousDate?.ToString("yyyy-MM-dd"),
            nextDate = nextDate?.ToString("yyyy-MM-dd")
        });
    }

    private static DateTime? TryGetLogFileDate(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (!fileName.StartsWith("log-", StringComparison.OrdinalIgnoreCase))
            return null;

        var datePart = fileName["log-".Length..];
        return DateTime.TryParseExact(datePart, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date) ? date.Date : null;
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

        if (!user.tenant_id.HasValue)
        {
            var tenant = new tenants
            {
                legal_name = string.IsNullOrWhiteSpace(user.full_name) ? user.username : user.full_name,
                trade_name = string.IsNullOrWhiteSpace(user.clinic_name) ? user.full_name : user.clinic_name,
                slug = $"tenant-{user.id}-{Guid.NewGuid():N}",
                contact_email = user.email,
                status = "active"
            };
            _context.tenants.Add(tenant);
            await _context.SaveChangesAsync();
            user.tenant_id = tenant.id;
        }

        var plan = await _context.subscription_plans.FirstOrDefaultAsync(p => p.code == request.SubscriptionPlan);
        if (plan == null) return BadRequest("Plan SaaS no encontrado.");

        var subscription = await _context.subscriptions.FirstOrDefaultAsync(s => s.tenant_id == user.tenant_id);
        var oldPlanId = subscription?.plan_id;
        if (subscription == null)
        {
            subscription = new subscriptions { tenant_id = user.tenant_id.Value, plan_id = plan.id };
            _context.subscriptions.Add(subscription);
            await _context.SaveChangesAsync();
        }
        subscription.plan_id = plan.id;
        subscription.status = request.SubscriptionStatus;
        subscription.expires_at = request.LicenseExpiresAt;
        _context.subscription_events.Add(new subscription_events
        {
            subscription_id = subscription.id,
            event_type = oldPlanId == plan.id ? "LICENSE_UPDATED" : "PLAN_CHANGED",
            old_plan_id = oldPlanId,
            new_plan_id = plan.id,
            details = $"SuperAdmin actualizó licencia del usuario {user.username}"
        });

        if (request.SubscriptionPlan == "clinic_full")
        {
            user.role = "clinic_admin";
        }
        else if (user.role == "clinic_admin")
        {
            user.role = "nutritionist";
        }

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
