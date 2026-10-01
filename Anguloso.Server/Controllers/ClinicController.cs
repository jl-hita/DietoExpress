using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Anguloso.Server.Controllers;
[ApiController]
[Route("api/clinic")]
[Authorize(Roles = "clinic_admin,nutritionist,user")]
public class ClinicController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly ILicenseService _license;
    private readonly IAuditLogService _audit;
    private readonly EmailServ _emailServ;
    private readonly ConfigServ _configServ;

    public ClinicController(angulosodbContext context, ILicenseService license, IAuditLogService audit, EmailServ emailServ, ConfigServ configServ)
    {
        _context = context;
        _license = license;
        _audit = audit;
        _emailServ = emailServ;
        _configServ = configServ;
    }
    [HttpGet("dashboard")]
    [Authorize(Roles="clinic_admin")]
    public async Task<IActionResult> Dashboard()
    {
        var tenantId=AuthHelpers.GetTenantId(User); if(!tenantId.HasValue) return BadRequest("El usuario no pertenece a una clínica.");
        if(!await _license.CanUseFeatureAsync(tenantId,"CLINIC_DASHBOARD")) return Forbid();
        var license=await _license.GetLicenseAsync(tenantId); var users=await _context.users.AsNoTracking().Where(u=>u.tenant_id==tenantId && u.archived_at==null && (u.role=="nutritionist"||u.role=="user")).Select(u=>new { u.id,u.full_name,u.username,u.email,u.role,u.last_login,clientCount=_context.clients.Count(c=>c.tenant_id==tenantId&&c.archived_at==null&&c.user_id==u.id)}).ToListAsync();
        var clients=await _context.clients.AsNoTracking().Where(c=>c.tenant_id==tenantId&&c.archived_at==null).OrderBy(c=>c.full_name).Select(c=>new {c.id,c.full_name,c.email,c.phone,nutritionistId=c.user_id,nutritionistName=_context.users.Where(u=>u.id==c.user_id).Select(u=>u.full_name).FirstOrDefault()}).ToListAsync();
        var unassignedClientCount=clients.Count(c=>c.nutritionistId==null);
        return Ok(new { license, nutritionists=users, clients, unassignedClientCount });
    }
    [HttpGet("nutritionists")]
    [Authorize(Roles="clinic_admin")]
    public async Task<IActionResult> Nutritionists()
    {
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!tenantId.HasValue) return BadRequest("Sin clínica.");

        var users = await _context.users.AsNoTracking()
            .Where(u => u.tenant_id == tenantId &&
                        (u.role == "nutritionist" || u.role == "user") &&
                        true)
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
    public async Task<IActionResult> CreateNutritionist([FromBody] CreateNutritionistRequest req)
    {
        var tenantId = AuthHelpers.GetTenantId(User);
        if (!tenantId.HasValue) return BadRequest("Sin clínica.");
        if (!await _license.CanUseFeatureAsync(tenantId, "MULTI_NUTRITIONIST")) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Email)) return BadRequest("El email es obligatorio.");

        var email = req.Email.Trim();
        var fullName = string.IsNullOrWhiteSpace(req.FullName) ? email : req.FullName.Trim();

        if (await _context.users.AnyAsync(u => u.email != null && u.email.ToLower() == email.ToLower()))
            return Conflict("El email ya está registrado.");

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", tenantId.Value);

            var allowed = await _license.CanCreateNutritionistAsync(tenantId);
            if (!allowed.Allowed) return BadRequest(allowed.Reason);

            var localPart = email.Split('@')[0].ToLowerInvariant();
            localPart = System.Text.RegularExpressions.Regex.Replace(localPart, @"[^a-z0-9._-]", "");
            if (string.IsNullOrWhiteSpace(localPart)) localPart = "nutricionista";
            var username = localPart.Length > 40 ? localPart[..40] : localPart;
            var baseUsername = username;
            var suffix = 1;
            while (await _context.users.AnyAsync(u => u.username == username))
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

    private static string HashSecurityToken(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    [HttpPut("nutritionists/{id:int}/activate")]
    [Authorize(Roles="clinic_admin")]
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
                (u.role == "nutritionist" || u.role == "user"));

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
    public async Task<IActionResult> DeactivationPreview(int id)
    {
        var tenantId=AuthHelpers.GetTenantId(User);
        var user=await _context.users.AsNoTracking().FirstOrDefaultAsync(u=>u.id==id&&u.tenant_id==tenantId&&u.archived_at==null&&(u.role=="nutritionist"||u.role=="user"));
        if(user==null)return NotFound("Nutricionista no encontrado.");

        var clients=await _context.clients.AsNoTracking()
            .Where(c=>c.tenant_id==tenantId&&c.archived_at==null&&c.user_id==id)
            .OrderBy(c=>c.full_name)
            .Select(c=>new {clientId=c.id,fullName=c.full_name,email=c.email})
            .ToListAsync();

        var candidates=await _context.users.AsNoTracking()
            .Where(u=>u.tenant_id==tenantId&&u.id!=id&&u.archived_at==null&&(u.role=="nutritionist"||u.role=="user"))
            .OrderBy(u=>u.full_name)
            .Select(u=>new {id=u.id,fullName=u.full_name,username=u.username})
            .ToListAsync();

        return Ok(new {nutritionist=new {id=user.id,fullName=user.full_name,username=user.username},clients,candidates,requiresReassignment=clients.Count>0});
    }

    [HttpPut("nutritionists/{id:int}/disable")]
    [Authorize(Roles="clinic_admin")]
    public async Task<IActionResult> DisableNutritionist(int id,[FromBody] DeactivateNutritionistRequest? req)
    {
        var tenantId=AuthHelpers.GetTenantId(User);
        var user=await _context.users.FirstOrDefaultAsync(u=>u.id==id&&u.tenant_id==tenantId&&u.archived_at==null&&(u.role=="nutritionist"||u.role=="user"));
        if(user==null)return NotFound("Nutricionista no encontrado.");

        var clients=await _context.clients.Where(c=>c.tenant_id==tenantId&&c.archived_at==null&&c.user_id==id).Select(c=>c.id).ToListAsync();
        var assignments=req?.Assignments??new List<ClientReassignment>();
        var expected=clients.ToHashSet();
        if(assignments.Count!=expected.Count||!expected.SetEquals(assignments.Select(a=>a.ClientId)) )
            return Conflict(new {message="Debes decidir qué hacer con todos los pacientes activos del nutricionista.",clientIds=clients});

        

        await using var transaction=await _context.Database.BeginTransactionAsync();
        try
        {
            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", tenantId.Value);

            foreach(var item in assignments)
            {
                var client=await _context.clients.FirstAsync(c=>c.id==item.ClientId&&c.tenant_id==tenantId&&c.archived_at==null&&c.user_id==id);
                var nutritionist = item.NutritionistId.HasValue
                    ? await _context.users.FirstOrDefaultAsync(u => u.id == item.NutritionistId.Value && u.tenant_id == tenantId && u.archived_at == null && (u.role == "nutritionist" || u.role == "user"))
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
    public async Task<IActionResult> AssignClient(int clientId,[FromBody] AssignClientRequest req)
    {
        var tenantId=AuthHelpers.GetTenantId(User);
        if(!tenantId.HasValue)return BadRequest();
        if(!await _license.CanUseFeatureAsync(tenantId,"CLIENT_ASSIGNMENT"))return Forbid();

        var client=await _context.clients.FirstOrDefaultAsync(c=>c.id==clientId&&c.tenant_id==tenantId&&c.archived_at==null);
        if(client==null)return NotFound("Cliente no encontrado.");

        var old=client.user_id;
        if(old==req.NutritionistId)return Ok();

        if(req.NutritionistId.HasValue)
        {
            var nutritionist=await _context.users.FirstOrDefaultAsync(u=>u.id==req.NutritionistId.Value&&u.tenant_id==tenantId&&u.archived_at==null&&(u.role=="nutritionist"||u.role=="user"));
            if(nutritionist==null)return BadRequest("Nutricionista no válido.");
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
        await _audit.LogAccessAsync("ASSIGN_CLIENT","clients",clientId.ToString(),clientId,$"Cambio de nutricionista {old?.ToString() ?? "sin asignar"} -> {req.NutritionistId?.ToString() ?? "sin asignar"}");
        return Ok();
    }
    [HttpGet("license")]
    public async Task<IActionResult> License(){var tenantId=AuthHelpers.GetTenantId(User);return Ok(await _license.GetLicenseAsync(tenantId));}
}
public record CreateNutritionistRequest(string Email, string? FullName);
public record AssignClientRequest(int? NutritionistId);
public record ClientReassignment(int ClientId,int? NutritionistId);
public record DeactivateNutritionistRequest(List<ClientReassignment> Assignments);