using System.Text.RegularExpressions;
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
    /// Crea una cuenta completa desde el panel de SuperAdmin, incluyendo tenant y licencia.
    /// </summary>
    [HttpPost("users/create-account")]
    public async Task<IActionResult> CreateAccount([FromBody] CreateAdminAccountRequest request)
    {
        if (request == null)
            return BadRequest("Datos de cuenta no válidos.");

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Usuario, nombre, email y contraseña son obligatorios.");

        if (request.Password.Length < 6)
            return BadRequest("La contraseña debe tener al menos 6 caracteres.");

        var accountType = (request.AccountType ?? "nutritionist").Trim().ToLowerInvariant();
        if (accountType != "nutritionist" && accountType != "clinic")
            return BadRequest("Tipo de cuenta no válido.");

        if (accountType == "clinic" && string.IsNullOrWhiteSpace(request.ClinicName))
            return BadRequest("El nombre de la clínica es obligatorio para una cuenta de clínica.");

        var username = request.Username.Trim();
        var email = request.Email.Trim();

        if (await _context.users.AnyAsync(u => u.username == username))
            return Conflict("El nombre de usuario ya existe.");
        if (await _context.users.AnyAsync(u => u.email == email))
            return Conflict("El email ya está registrado.");

        var planCode = string.IsNullOrWhiteSpace(request.SubscriptionPlan) ? "free" : request.SubscriptionPlan.Trim();
        var plan = await _context.subscription_plans.FirstOrDefaultAsync(p => p.code == planCode && p.active);
        if (plan == null)
            return BadRequest("El plan seleccionado no existe o no está activo.");

        if (accountType == "clinic" && plan.code != "clinic_full")
            return BadRequest("Una cuenta de clínica debe utilizar el plan Clínica Full.");
        if (accountType == "nutritionist" && plan.code == "clinic_full")
            return BadRequest("Una cuenta de nutricionista no puede utilizar el plan Clínica Full.");

        var subscriptionStatus = (request.SubscriptionStatus ?? "active").Trim().ToLowerInvariant();
        if (subscriptionStatus != "active" && subscriptionStatus != "past_due" && subscriptionStatus != "suspended")
            return BadRequest("Estado de suscripción no válido.");

        if (request.MaxClientsAllowed.HasValue && request.MaxClientsAllowed.Value < 1)
            return BadRequest("El máximo de pacientes debe ser al menos 1.");

        var now = DateTime.UtcNow;
        DateTime? expiresAt = request.LicenseExpiresAt;
        if (!expiresAt.HasValue && plan.trial_days.HasValue)
            expiresAt = now.AddDays(plan.trial_days.Value);

        var legalName = accountType == "clinic"
            ? (string.IsNullOrWhiteSpace(request.LegalName) ? request.ClinicName! : request.LegalName)
            : request.FullName;
        var tradeName = accountType == "clinic"
            ? (request.ClinicName ?? legalName)
            : request.FullName;

        var slugBase = Regex.Replace((accountType == "clinic" ? tradeName : username).ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slugBase)) slugBase = "tenant";
        var slug = $"{slugBase}-{Guid.NewGuid():N}";

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var tenant = new tenants
            {
                legal_name = legalName,
                trade_name = tradeName,
                cif_nif = request.CifNif?.Trim() ?? string.Empty,
                slug = slug,
                status = "active",
                contact_email = email,
                contact_phone = request.ClinicPhone?.Trim(),
                address = request.ClinicAddress?.Trim()
            };
            _context.tenants.Add(tenant);
            await _context.SaveChangesAsync();

            var user = new users
            {
                username = username,
                full_name = request.FullName.Trim(),
                password_hash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                email = email,
                email_confirmed = true,
                role = accountType == "clinic" ? "clinic_admin" : "nutritionist",
                created_at = now,
                tenant_id = tenant.id,
                clinic_name = request.ClinicName?.Trim(),
                clinic_address = request.ClinicAddress?.Trim(),
                clinic_phone = request.ClinicPhone?.Trim(),
                subscription_plan = plan.code,
                subscription_status = subscriptionStatus,
                license_expires_at = expiresAt,
                max_clients_allowed = request.MaxClientsAllowed ?? plan.max_clients_per_nutritionist ?? 10
            };
            _context.users.Add(user);
            await _context.SaveChangesAsync();

            var subscription = new subscriptions
            {
                tenant_id = tenant.id,
                plan_id = plan.id,
                status = user.subscription_status,
                started_at = now,
                expires_at = expiresAt
            };
            _context.subscriptions.Add(subscription);
            await _context.SaveChangesAsync();

            _context.subscription_events.Add(new subscription_events
            {
                subscription_id = subscription.id,
                event_type = "ACCOUNT_CREATED_BY_SUPERADMIN",
                new_plan_id = plan.id,
                details = $"Cuenta creada por SuperAdmin: {user.username}"
            });
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                message = "Cuenta creada correctamente.",
                userId = user.id,
                tenantId = tenant.id,
                username = user.username,
                role = user.role,
                subscriptionPlan = plan.code,
                licenseExpiresAt = expiresAt
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
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
    /// Elimina permanentemente una cuenta de usuario y sus datos asociados.
    /// Solo puede ejecutarlo un SuperAdmin y nunca se permite eliminar otro SuperAdmin.
    /// </summary>
    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await _context.users.FirstOrDefaultAsync(u => u.id == id);
        if (user == null || user.role == "superadmin")
            return NotFound("Usuario no encontrado o no eliminable.");

        var clientIds = await _context.clients
            .Where(c => c.user_id == id)
            .Select(c => c.id)
            .ToListAsync();

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Las asignaciones tienen FK restrictiva hacia el nutricionista.
            await _context.client_nutritionist_assignments
                .Where(a => a.nutritionist_id == id)
                .ExecuteDeleteAsync();

            // Conservamos la trazabilidad de auditoría sin referencias al usuario eliminado.
            await _context.audit_logs
                .Where(a => a.user_id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.user_id, (int?)null));

            if (clientIds.Count > 0)
            {
                // Los pacientes se eliminan junto con la cuenta, pero sus registros de auditoría se conservan.
                await _context.audit_logs
                    .Where(a => a.client_id.HasValue && clientIds.Contains(a.client_id.Value))
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.client_id, (int?)null));
            }

            // La BBDD elimina en cascada los datos dependientes del usuario (pacientes,
            // dietas, recetas y sus elementos relacionados).
            _context.users.Remove(user);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { message = "Cuenta eliminada permanentemente." });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
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

public class CreateAdminAccountRequest
{
    public string AccountType { get; set; } = "nutritionist";
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? ClinicName { get; set; }
    public string? LegalName { get; set; }
    public string? CifNif { get; set; }
    public string? ClinicAddress { get; set; }
    public string? ClinicPhone { get; set; }
    public string SubscriptionPlan { get; set; } = "free";
    public string SubscriptionStatus { get; set; } = "active";
    public DateTime? LicenseExpiresAt { get; set; }
    public int? MaxClientsAllowed { get; set; }
}
