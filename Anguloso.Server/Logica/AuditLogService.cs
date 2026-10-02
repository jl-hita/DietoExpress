using System;
using System.Threading.Tasks;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Http;

namespace Anguloso.Server.Logica;

public interface IAuditLogService
{
    Task LogAccessAsync(string action, string entityName, string? entityId = null, int? clientId = null, string? details = null);
}

// El audit log registra acciones relevantes junto con su contexto para poder reconstruir quién actuó sobre qué recurso y desde qué tenant.
public class AuditLogService : IAuditLogService
{
    private readonly angulosodbContext _context;
    private readonly ITenantContextService _tenantContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly LogServ _logServ;

    public AuditLogService(
        angulosodbContext context,
        ITenantContextService tenantContext,
        IHttpContextAccessor httpContextAccessor,
        LogServ logServ)
    {
        _context = context;
        _tenantContext = tenantContext;
        _httpContextAccessor = httpContextAccessor;
        _logServ = logServ;
    }

    // La auditoría captura el contexto HTTP y del tenant en el momento de la operación para mantener trazabilidad RGPD.
    public async Task LogAccessAsync(string action, string entityName, string? entityId = null, int? clientId = null, string? details = null)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;
            var ip = httpContext?.Connection?.RemoteIpAddress?.ToString();
            var userAgent = httpContext?.Request?.Headers["User-Agent"].ToString();

            if (userAgent != null && userAgent.Length > 290)
            {
                userAgent = userAgent.Substring(0, 290);
            }

            var logEntry = new audit_logs
            {
                tenant_id = _tenantContext.TenantId,
                user_id = _tenantContext.UserId,
                user_role = _tenantContext.UserRole,
                action = action,
                entity_name = entityName,
                entity_id = entityId,
                client_id = clientId,
                ip_address = ip,
                user_agent = userAgent,
                details = details,
                created_at = DateTime.UtcNow
            };

            _context.audit_logs.Add(logEntry);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // La auditoría no debe romper el flujo asistencial principal pero sí alertar en logs
            _logServ.LogError($"Error guardando registro de auditoría RGPD: {ex.Message}");
        }
    }
}
