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
    private readonly angulosodbContext _context; private readonly ILicenseService _license; private readonly IAuditLogService _audit;
    public ClinicController(angulosodbContext context, ILicenseService license, IAuditLogService audit) { _context=context; _license=license; _audit=audit; }
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var tenantId=AuthHelpers.GetTenantId(User); if(!tenantId.HasValue) return BadRequest("El usuario no pertenece a una clínica.");
        if(!await _license.CanUseFeatureAsync(tenantId,"CLINIC_DASHBOARD")) return Forbid();
        var license=await _license.GetLicenseAsync(tenantId); var users=await _context.users.AsNoTracking().Where(u=>u.tenant_id==tenantId && u.archived_at==null && (u.role=="nutritionist"||u.role=="user")).Select(u=>new { u.id,u.full_name,u.username,u.email,u.role,u.last_login,clientCount=_context.clients.Count(c=>c.tenant_id==tenantId&&c.archived_at==null&&c.user_id==u.id)}).ToListAsync();
        var clients=await _context.clients.AsNoTracking().Where(c=>c.tenant_id==tenantId&&c.archived_at==null).OrderBy(c=>c.full_name).Select(c=>new {c.id,c.full_name,c.email,c.phone,nutritionistId=c.user_id,nutritionistName=_context.users.Where(u=>u.id==c.user_id).Select(u=>u.full_name).FirstOrDefault()}).ToListAsync();
        return Ok(new { license, nutritionists=users, clients });
    }
    [HttpGet("nutritionists")]
    public async Task<IActionResult> Nutritionists(){ var tenantId=AuthHelpers.GetTenantId(User); if(!tenantId.HasValue)return BadRequest(); return Ok(await _context.users.AsNoTracking().Where(u=>u.tenant_id==tenantId&&u.archived_at==null&&(u.role=="nutritionist"||u.role=="user")).OrderBy(u=>u.full_name).Select(u=>new {u.id,u.full_name,u.username,u.email,u.role,u.last_login,clientCount=_context.clients.Count(c=>c.tenant_id==tenantId&&c.user_id==u.id)}).ToListAsync()); }
    [HttpPost("nutritionists")]
    [Authorize(Roles="clinic_admin")]
    public async Task<IActionResult> CreateNutritionist([FromBody] CreateNutritionistRequest req)
    {
        var tenantId=AuthHelpers.GetTenantId(User); if(!tenantId.HasValue)return BadRequest("Sin clínica.");
        if(!await _license.CanUseFeatureAsync(tenantId,"MULTI_NUTRITIONIST")) return Forbid();
        var allowed=await _license.CanCreateNutritionistAsync(tenantId); if(!allowed.Allowed)return BadRequest(allowed.Reason);
        if(string.IsNullOrWhiteSpace(req.Username)||string.IsNullOrWhiteSpace(req.Email)||string.IsNullOrWhiteSpace(req.Password))return BadRequest("Usuario, email y contraseña son obligatorios.");
        if(await _context.users.AnyAsync(u=>u.username==req.Username||u.email==req.Email))return Conflict("El usuario o email ya existe.");
        var user=new users{username=req.Username,full_name=req.FullName??req.Username,email=req.Email,password_hash=BCrypt.Net.BCrypt.HashPassword(req.Password),role="nutritionist",tenant_id=tenantId,created_at=DateTime.UtcNow,email_confirmed=true,subscription_plan="clinic_full",subscription_status="active"};
        _context.users.Add(user); await _context.SaveChangesAsync(); await _audit.LogAccessAsync("CREATE_NUTRITIONIST","users",user.id.ToString(),null,$"Alta de nutricionista {user.username}");
        return Ok(new {id=user.id,message="Nutricionista creado correctamente."});
    }
    [HttpPut("nutritionists/{id:int}/disable")]
    [Authorize(Roles="clinic_admin")]
    public async Task<IActionResult> DisableNutritionist(int id)
    {
        var tenantId=AuthHelpers.GetTenantId(User); var user=await _context.users.FirstOrDefaultAsync(u=>u.id==id&&u.tenant_id==tenantId);
        if(user==null||user.role=="clinic_admin")return NotFound();
        if(await _context.clients.AnyAsync(c=>c.tenant_id==tenantId&&c.user_id==id))return BadRequest("Reasigna todos sus clientes antes de desactivar al nutricionista.");
        user.role="disabled_nutritionist"; user.subscription_status="suspended"; await _context.SaveChangesAsync(); return Ok();
    }
    [HttpPut("clients/{clientId:int}/assign")]
    [Authorize(Roles="clinic_admin")]
    public async Task<IActionResult> AssignClient(int clientId,[FromBody] AssignClientRequest req)
    {
        var tenantId=AuthHelpers.GetTenantId(User); if(!tenantId.HasValue)return BadRequest();
        if(!await _license.CanUseFeatureAsync(tenantId,"CLIENT_ASSIGNMENT"))return Forbid();
        var client=await _context.clients.FirstOrDefaultAsync(c=>c.id==clientId&&c.tenant_id==tenantId&&c.archived_at==null); if(client==null)return NotFound("Cliente no encontrado.");
        var nutritionist=await _context.users.FirstOrDefaultAsync(u=>u.id==req.NutritionistId&&u.tenant_id==tenantId&&u.archived_at==null&&(u.role=="nutritionist"||u.role=="user")); if(nutritionist==null)return BadRequest("Nutricionista no válido.");
        var old=client.user_id; if(old==req.NutritionistId)return Ok();
        var active=await _context.client_nutritionist_assignments.FirstOrDefaultAsync(a=>a.client_id==clientId&&a.is_active); if(active!=null){active.is_active=false;active.unassigned_at=DateTime.UtcNow;}
        _context.client_nutritionist_assignments.Add(new client_nutritionist_assignments{client_id=clientId,nutritionist_id=req.NutritionistId,assigned_by_user_id=AuthHelpers.GetUserId(User),assigned_at=DateTime.UtcNow,is_active=true});
        client.user_id=req.NutritionistId; await _context.SaveChangesAsync();
        await _audit.LogAccessAsync("ASSIGN_CLIENT","clients",clientId.ToString(),clientId,$"Cambio de nutricionista {old} -> {req.NutritionistId}"); return Ok();
    }
    [HttpGet("license")]
    public async Task<IActionResult> License(){var tenantId=AuthHelpers.GetTenantId(User);return Ok(await _license.GetLicenseAsync(tenantId));}
}
public record CreateNutritionistRequest(string Username,string Email,string Password,string? FullName);
public record AssignClientRequest(int NutritionistId);