using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace Anguloso.Server.Controllers;
[ApiController]
[Route("api/clinic")]
[Authorize(Roles = "clinic_admin")]
public class ClinicController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly ILicenseService _license;
    private readonly IAuditLogService _audit;
    private readonly EmailServ _emailServ;
    private readonly ConfigServ _configServ;
    private readonly IStripeBillingService _stripe;

    public ClinicController(angulosodbContext context, ILicenseService license, IAuditLogService audit, EmailServ emailServ, ConfigServ configServ, IStripeBillingService stripe)
    {
        _context = context;
        _license = license;
        _audit = audit;
        _emailServ = emailServ;
        _configServ = configServ;
        _stripe = stripe;
    }
    [HttpPost("nutritionist-seats")]
    [Authorize(Roles="clinic_admin")]
    [EnableRateLimiting("expensive")]
    public async Task<IActionResult> ChangeNutritionistSeats([FromBody] ChangeNutritionistSeatsRequest request)
    {
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!tenantId.HasValue) return BadRequest("Sin clínica.");
        if (request == null || !request.TargetSeats.HasValue)
            return BadRequest("Debes indicar el número de puestos contratados.");
        if (request.TargetSeats.Value > 1000)
            return BadRequest("La capacidad máxima configurada para una clínica es de 1000 nutricionistas.");

        var license = await _license.GetLicenseAsync(tenantId);
        if (license == null || !string.Equals(license.PlanCode, "clinic_full", StringComparison.OrdinalIgnoreCase))
            return BadRequest("La gestión de puestos adicionales solo está disponible para clínicas.");

        var included = license.IncludedNutritionists ?? license.MaxNutritionists ?? 0;
        if (request.TargetSeats.Value < included)
            return BadRequest($"La suscripción Clínica incluye {included} puestos y no puede reducirse por debajo de esa cantidad.");

        if (request.TargetSeats.Value < license.Nutritionists)
            return BadRequest($"No puedes reducir a {request.TargetSeats.Value} puestos mientras haya {license.Nutritionists} nutricionistas activos.");

        try
        {
            await _stripe.ChangeNutritionistSeatsAsync(tenantId.Value, request.TargetSeats.Value);
            var updated = await _license.GetLicenseAsync(tenantId);
            return Ok(new { message = "Capacidad profesional actualizada correctamente.", license = updated });
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet("dashboard")]
    [Authorize(Roles="clinic_admin")]
    // El dashboard agrega información del tenant autenticado para que las métricas nunca dependan de un tenant enviado por el cliente.
    public async Task<IActionResult> Dashboard()
    {
        var tenantId=AuthHelpers.GetTenantId(User); if(!tenantId.HasValue) return BadRequest("El usuario no pertenece a una clínica.");
        if(!await _license.CanUseFeatureAsync(tenantId,"CLINIC_DASHBOARD")) return Forbid();
        var license=await _license.GetLicenseAsync(tenantId); var users=await _context.users.AsNoTracking().Where(u=>u.tenant_id==tenantId && u.archived_at==null && u.role=="nutritionist").OrderBy(u=>u.id).Take(500).Select(u=>new { u.id,u.full_name,u.username,u.email,u.role,u.last_login,clientCount=_context.clients.Count(c=>c.tenant_id==tenantId&&c.archived_at==null&&c.user_id==u.id)}).ToListAsync();
        var clients=await _context.clients.AsNoTracking().Where(c=>c.tenant_id==tenantId&&c.archived_at==null).OrderBy(c=>c.full_name).Take(1000).Select(c=>new {c.id,c.full_name,c.email,c.phone,nutritionistId=c.user_id,nutritionistName=_context.users.Where(u=>u.id==c.user_id && u.tenant_id==tenantId).Select(u=>u.full_name).FirstOrDefault()}).ToListAsync();
        var unassignedClientCount=clients.Count(c=>c.nutritionistId==null);

        // Indicadores operativos compartidos conceptualmente con el dashboard profesional:
        // siempre se calculan dentro del tenant y con la misma fuente de datos de citas,
        // mensajes y documentación, pero agregados para toda la clínica.
        var madrid = GetMadridTimeZone();
        var localToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, madrid).Date;
        var todayStart = TimeZoneInfo.ConvertTimeToUtc(localToday, madrid);
        var tomorrowStart = todayStart.AddDays(1);

        // Estas tablas son módulos SQL adicionales y no forman parte del modelo EF generado.
        // Consultamos solo agregados para mantener el dashboard desacoplado del scaffold.
        var todayAppointments = await _context.Database.SqlQueryRaw<int>(
            @"SELECT COUNT(*)::int AS ""Value""
              FROM patient_appointments
              WHERE tenant_id = {0}
                AND starts_at >= {1}
                AND starts_at < {2}
                AND status IN ('requested', 'confirmed')",
            tenantId.Value, todayStart, tomorrowStart).SingleAsync();

        var unreadMessages = await _context.Database.SqlQueryRaw<int>(
            @"SELECT COUNT(*)::int AS ""Value""
              FROM patient_messages m
              INNER JOIN patient_conversations c ON c.id = m.conversation_id
              WHERE c.tenant_id = {0}
                AND m.sender_client_id IS NOT NULL
                AND m.read_at IS NULL",
            tenantId.Value).SingleAsync();

        var pendingDocuments = await _context.Database.SqlQueryRaw<int>(
            @"SELECT COUNT(*)::int AS ""Value""
              FROM patient_documents d
              INNER JOIN clients c ON c.id = d.client_id
              WHERE d.tenant_id = {0}
                AND c.tenant_id = {0}
                AND c.archived_at IS NULL
                AND d.revoked_at IS NULL
                AND d.requires_signature = TRUE
                AND d.status = 'pending'",
            tenantId.Value).SingleAsync();

        var documentProvisioning = await _context.Database.SqlQueryRaw<ClinicDocumentProvisioningSummary>(
            @"SELECT
                  COUNT(*) FILTER (WHERE j.status IN ('pending','processing'))::int AS ""RetryCount"",
                  COUNT(*) FILTER (WHERE j.status='failed')::int AS ""FailedCount"",
                  COUNT(*) FILTER (WHERE j.status='completed' AND EXISTS (
                      SELECT 1 FROM clients cl
                      WHERE cl.tenant_id=j.tenant_id
                        AND j.idempotency_key = CONCAT('documents:provision:', j.tenant_id, ':', cl.id, ':creation')
                        AND cl.archived_at IS NULL
                        AND EXISTS (
                            SELECT 1 FROM document_templates dt
                            WHERE dt.tenant_id=j.tenant_id
                              AND dt.is_active=true
                              AND dt.storage_key IS NOT NULL
                              AND dt.is_required_on_client_creation=true
                              AND NOT EXISTS (
                                  SELECT 1 FROM patient_documents pd
                                  WHERE pd.tenant_id=j.tenant_id AND pd.client_id=cl.id
                                    AND pd.document_template_id=dt.id AND pd.version=dt.version
                                    AND pd.revoked_at IS NULL
                              )
                        )
                  ))::int AS ""IncompleteCount""
              FROM automation_jobs j
              WHERE j.tenant_id = {0}
                AND j.action_type = 'provision_patient_documents'
                AND j.idempotency_key LIKE CONCAT('documents:provision:', {0}, ':%:creation')",
            tenantId.Value).SingleAsync();

        return Ok(new {
            license,
            nutritionists=users,
            clients,
            unassignedClientCount,
            todayAppointments,
            unreadMessageCount=unreadMessages,
            pendingDocumentCount=pendingDocuments,
            documentProvisioningRetryCount=documentProvisioning.RetryCount,
            documentProvisioningFailedCount=documentProvisioning.FailedCount,
            documentProvisioningIncompleteCount=documentProvisioning.IncompleteCount
        });
    }
    [HttpGet("nutritionists")]
    [Authorize(Roles="clinic_admin")]
    // La consulta se limita al tenant actual y expone el estado de las cuentas para gestionar la plantilla de la clínica.
    public async Task<IActionResult> Nutritionists()
    {
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!tenantId.HasValue) return BadRequest("Sin clínica.");

        var users = await _context.users.AsNoTracking()
            .Where(u => u.tenant_id == tenantId &&
                        u.role == "nutritionist" &&
                        true)
            .Take(500)
            .OrderByDescending(u => u.archived_at == null)
            .ThenBy(u => u.full_name)
            .Select(u => new
            {
                u.id, u.full_name, u.username, u.email, u.role, u.last_login,
                u.archived_at,
                active = u.archived_at == null,
                clientCount = _context.clients.Count(c => c.tenant_id == tenantId && c.archived_at == null && c.user_id == u.id)
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost("nutritionists")]
    [Authorize(Roles="clinic_admin")]
    // El alta comprueba el límite contratado antes de crear la cuenta y su relación con la clínica.
    public async Task<IActionResult> CreateNutritionist([FromBody] CreateNutritionistRequest req)
    {
        if (req == null) return BadRequest("Datos del nutricionista no válidos.");
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!tenantId.HasValue) return BadRequest("Sin clínica.");
        if (!await _license.CanUseFeatureAsync(tenantId, "MULTI_NUTRITIONIST")) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Email)) return BadRequest("El email es obligatorio.");

        var email = req.Email.Trim().ToLowerInvariant();
        var fullName = string.IsNullOrWhiteSpace(req.FullName) ? email : req.FullName.Trim();

        if (email.Length > 150 || fullName.Length > 100)
            return BadRequest("El email o el nombre superan la longitud permitida.");

        if (await _context.users.AnyAsync(u => u.email != null && u.email.ToLower() == email))
            return Conflict("El email ya está registrado.");

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0}, {1})", tenantId.Value, 748392616);

            // El chequeo previo al lock es solo una respuesta rápida. Revalidamos
            // dentro del mismo lock global que usan los demás flujos de alta de cuentas.
            // El username y el email son identificadores globales de acceso, no de tenant.
            if (await _context.users.AnyAsync(u => u.email != null && u.email.ToLower() == email.ToLower()))
                return Conflict("El email ya está registrado.");

            var allowed = await _license.CanCreateNutritionistAsync(tenantId);
            if (!allowed.Allowed) return BadRequest(allowed.Reason);

            var localPart = email.Split('@')[0].ToLowerInvariant();
            localPart = System.Text.RegularExpressions.Regex.Replace(localPart, @"[^a-z0-9._-]", "");
            if (string.IsNullOrWhiteSpace(localPart)) localPart = "nutricionista";
            var username = (localPart.Length > 40 ? localPart[..40] : localPart).ToLowerInvariant();
            var baseUsername = username;
            var suffix = 1;
            while (await _context.users.AnyAsync(u => u.username.ToLower() == username.ToLower()))
            {
                var suffixText = suffix.ToString();
                username = baseUsername[..Math.Min(baseUsername.Length, 50 - suffixText.Length - 1)] + "-" + suffixText;
                suffix++;
            }

            var randomPassword = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var resetToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var now = DateTime.UtcNow;

            var user = new users
            {
                username = username,
                full_name = fullName,
                email = email,
                password_hash = BCrypt.Net.BCrypt.HashPassword(randomPassword),
                role = "nutritionist",
                tenant_id = tenantId,
                created_at = now,
                email_confirmed = true,
                reset_password_token = HashSecurityToken(resetToken),
                reset_token_expiration = now.AddHours(24),
                token_version = 1,
                subscription_plan = "clinic_full",
                subscription_status = "active"
            };

            _context.users.Add(user);
            await _context.SaveChangesAsync();

            var frontendUrl = _configServ.GetConfigString("frontendUrl", "https://localhost:4200") ?? "https://localhost:4200";
            var resetUrl = $"{frontendUrl.TrimEnd('/')}/reset-pwd?token={Uri.EscapeDataString(resetToken)}";

            var emailResult = await _emailServ.SendEmailAsync(
                email,
                "Invitación a DietoExpress",
                $@"<h2>Has sido invitado a DietoExpress</h2>
                   <p>La clínica te ha creado una cuenta de nutricionista.</p>
                   <p><strong>Usuario de acceso:</strong> {System.Net.WebUtility.HtmlEncode(email)}</p>
                   <p>Para establecer tu contraseña y activar tu acceso, utiliza este enlace:</p>
                   <p><a href='{System.Net.WebUtility.HtmlEncode(resetUrl)}'>Establecer mi contraseña</a></p>
                   <p>El enlace caduca en 24 horas y solo puede utilizarse una vez.</p>");

            if (!emailResult.Exito)
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "No se pudo enviar la invitación por email. La cuenta no se ha creado.", detail = emailResult.Mensaje });

            await transaction.CommitAsync();

            await _audit.LogAccessAsync("CREATE_NUTRITIONIST", "users", user.id.ToString(), null, $"Alta de nutricionista {user.username} para {email}");
            return Ok(new { id = user.id, username = user.username, email = user.email, message = "Nutricionista creado y email de invitación enviado." });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static TimeZoneInfo GetMadridTimeZone() =>
        TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Madrid");

    private static string HashSecurityToken(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    [HttpPut("nutritionists/{id:int}/activate")]
    [Authorize(Roles="clinic_admin")]
    // La activación se realiza dentro del tenant y vuelve a contar la capacidad contratada antes de habilitar la cuenta.
    public async Task<IActionResult> ActivateNutritionist(int id)
    {
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!tenantId.HasValue) return BadRequest("Sin clínica.");
        if (!await _license.CanUseFeatureAsync(tenantId, "MULTI_NUTRITIONIST")) return Forbid();

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", tenantId.Value);

            var user = await _context.users.FirstOrDefaultAsync(u =>
                u.id == id && u.tenant_id == tenantId && u.archived_at != null &&
                u.role == "nutritionist");

            if (user == null) return NotFound("Nutricionista desactivado no encontrado.");

            // La reactivación de la misma cuenta no consume una nueva sustitución:
            // simplemente vuelve a ocupar la plaza que ya tenía.
            var allowed = await _license.CanCreateNutritionistAsync(tenantId, allowReactivation: true);
            if (!allowed.Allowed) return BadRequest(allowed.Reason);

            user.archived_at = null;
            user.subscription_status = "active";
            user.token_version++;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            await _audit.LogAccessAsync("ACTIVATE_NUTRITIONIST", "users", user.id.ToString(), null, $"Nutricionista {user.username} reactivado por la clínica");
            return Ok(new { message = "Nutricionista activado correctamente." });
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    [HttpGet("nutritionists/{id:int}/deactivation-preview")]
    [Authorize(Roles="clinic_admin")]
    // Este paso informa de pacientes y relaciones que quedarían sin nutricionista antes de confirmar la desactivación.
    public async Task<IActionResult> DeactivationPreview(int id)
    {
        var tenantId=AuthHelpers.GetTenantId(User);
        var user=await _context.users.AsNoTracking().FirstOrDefaultAsync(u=>u.id==id&&u.tenant_id==tenantId&&u.archived_at==null&&u.role=="nutritionist");
        if(user==null)return NotFound("Nutricionista no encontrado.");

        var clients=await _context.clients.AsNoTracking()
            .Where(c=>c.tenant_id==tenantId&&c.archived_at==null&&c.user_id==id)
            .OrderBy(c=>c.full_name)
            .Select(c=>new {clientId=c.id,fullName=c.full_name,email=c.email})
            .ToListAsync();

        var candidates=await _context.users.AsNoTracking()
            .Where(u=>u.tenant_id==tenantId&&u.id!=id&&u.archived_at==null&&u.role=="nutritionist")
            .OrderBy(u=>u.full_name)
            .Select(u=>new {id=u.id,fullName=u.full_name,username=u.username})
            .ToListAsync();

        return Ok(new {nutritionist=new {id=user.id,fullName=user.full_name,username=user.username},clients,candidates,requiresReassignment=clients.Count>0});
    }

    [HttpPut("nutritionists/{id:int}/disable")]
    [Authorize(Roles="clinic_admin")]
    // La desactivación es lógica para preservar historial y permite posteriormente reasignar pacientes activos.
    public async Task<IActionResult> DisableNutritionist(int id,[FromBody] DeactivateNutritionistRequest? req)
    {
        var tenantId=AuthHelpers.GetTenantId(User);
        if (!tenantId.HasValue) return BadRequest("El usuario no pertenece a una clínica.");
        var user=await _context.users.FirstOrDefaultAsync(u=>u.id==id&&u.tenant_id==tenantId&&u.archived_at==null&&u.role=="nutritionist");
        if(user==null)return NotFound("Nutricionista no encontrado.");

        var assignments=req?.Assignments??new List<ClientReassignment>();

        await using var transaction=await _context.Database.BeginTransactionAsync();
        try
        {
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", tenantId.Value);

            // Obtener y validar los pacientes después de adquirir el mismo lock que usa
            // AssignClient/CreateClient, evitando decisiones basadas en un snapshot obsoleto.
            var clients=await _context.clients.Where(c=>c.tenant_id==tenantId&&c.archived_at==null&&c.user_id==id).Select(c=>c.id).ToListAsync();
            var expected=clients.ToHashSet();
            if(assignments.Count!=expected.Count||!expected.SetEquals(assignments.Select(a=>a.ClientId)) )
                return Conflict(new {message="Debes decidir qué hacer con todos los pacientes activos del nutricionista.",clientIds=clients});

            foreach(var item in assignments)
            {
                var client=await _context.clients.FirstAsync(c=>c.id==item.ClientId&&c.tenant_id==tenantId&&c.archived_at==null&&c.user_id==id);
                var nutritionist = item.NutritionistId.HasValue
                    ? await _context.users.FirstOrDefaultAsync(u => u.id == item.NutritionistId.Value && u.tenant_id == tenantId && u.archived_at == null && u.role == "nutritionist")
                    : null;
                if(item.NutritionistId.HasValue && nutritionist == null)
                    return BadRequest("Uno de los nutricionistas seleccionados no pertenece a la clínica o está archivado.");

                var active=await _context.client_nutritionist_assignments.FirstOrDefaultAsync(a=>a.client_id==item.ClientId&&a.is_active);
                if(active!=null){active.is_active=false;active.unassigned_at=DateTime.UtcNow;}
                if (nutritionist != null)
                {
                    _context.client_nutritionist_assignments.Add(new client_nutritionist_assignments
                    {
                        client_id = client.id,
                        nutritionist_id = nutritionist.id,
                        assigned_by_user_id = AuthHelpers.GetUserId(User),
                        assigned_at = DateTime.UtcNow,
                        is_active = true
                    });
                    client.user_id = nutritionist.id;
                }
                else
                {
                    client.user_id = null;
                }
            }

            user.archived_at=DateTime.UtcNow;
            user.subscription_status="suspended";
            user.token_version++;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        await _audit.LogAccessAsync("ARCHIVE_NUTRITIONIST","users",user.id.ToString(),null,$"Nutricionista {user.username} archivado por clínica; pacientes reasignados: {assignments.Count}");
        return Ok(new {message="Nutricionista desactivado correctamente. La plaza queda bloqueada para nuevas altas durante el periodo de sustitución."});
    }

    [HttpPut("clients/{clientId:int}/assign")]
    [Authorize(Roles="clinic_admin")]
    // La asignación valida que paciente y nutricionista pertenezcan al mismo tenant y que la cuenta de destino esté activa.
    public async Task<IActionResult> AssignClient(int clientId,[FromBody] AssignClientRequest req)
    {
        if (req == null) return BadRequest("Datos de asignación no válidos.");
        var tenantId=AuthHelpers.GetTenantId(User);
        if(!tenantId.HasValue)return BadRequest();
        if(!await _license.CanUseFeatureAsync(tenantId,"CLIENT_ASSIGNMENT"))return Forbid();

        await using var transaction=await _context.Database.BeginTransactionAsync();
        try
        {
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", tenantId.Value);

            var client=await _context.clients.FirstOrDefaultAsync(c=>c.id==clientId&&c.tenant_id==tenantId&&c.archived_at==null);
            if(client==null)return NotFound("Cliente no encontrado.");

            var old=client.user_id;
            if(old==req.NutritionistId)
            {
                await transaction.CommitAsync();
                return Ok();
            }

            if(req.NutritionistId.HasValue)
            {
                var nutritionist=await _context.users.FirstOrDefaultAsync(u=>u.id==req.NutritionistId.Value&&u.tenant_id==tenantId&&u.archived_at==null&&u.role=="nutritionist");
                if(nutritionist==null)return BadRequest("Nutricionista no válido.");

                var allowed=await _license.CanAssignClientAsync(tenantId,nutritionist.id,clientId);
                if(!allowed.Allowed)return BadRequest(allowed.Reason);
            }

            var active=await _context.client_nutritionist_assignments.FirstOrDefaultAsync(a=>a.client_id==clientId&&a.is_active);
            if(active!=null){active.is_active=false;active.unassigned_at=DateTime.UtcNow;}

            if(req.NutritionistId.HasValue)
            {
                _context.client_nutritionist_assignments.Add(new client_nutritionist_assignments
                {
                    client_id=clientId,
                    nutritionist_id=req.NutritionistId.Value,
                    assigned_by_user_id=AuthHelpers.GetUserId(User),
                    assigned_at=DateTime.UtcNow,
                    is_active=true
                });
            }

            client.user_id=req.NutritionistId;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            await _audit.LogAccessAsync("ASSIGN_CLIENT","clients",clientId.ToString(),clientId,$"Cambio de nutricionista {old?.ToString() ?? "sin asignar"} -> {req.NutritionistId?.ToString() ?? "sin asignar"}");
            return Ok();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
    [HttpGet("license")]
    public async Task<IActionResult> License(){var tenantId=AuthHelpers.GetTenantId(User);return Ok(await _license.GetLicenseAsync(tenantId));}
}
public record CreateNutritionistRequest(string Email, string? FullName);
public record AssignClientRequest(int? NutritionistId);
public record ClientReassignment(int ClientId,int? NutritionistId);
public record DeactivateNutritionistRequest(List<ClientReassignment> Assignments);
public sealed record ChangeNutritionistSeatsRequest(int? TargetSeats);


public sealed class ClinicDocumentProvisioningSummary
{
    public int RetryCount { get; set; }
    public int FailedCount { get; set; }
    public int IncompleteCount { get; set; }
}
