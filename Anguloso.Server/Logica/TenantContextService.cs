using System.Security.Claims;
using Anguloso.Server.Logica.Utils;
using Microsoft.AspNetCore.Http;

namespace Anguloso.Server.Logica;

public interface ITenantContextService
{
    int? TenantId { get; }
    int? UserId { get; }
    string? UserRole { get; }
    bool HasTenant { get; }
}

public class TenantContextService : ITenantContextService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantContextService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public int? TenantId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null) return null;
            return AuthHelpers.GetTenantId(user);
        }
    }

    public int? UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null) return null;
            return AuthHelpers.GetUserId(user);
        }
    }

    public string? UserRole
    {
        get
        {
            return _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Role)?.Value;
        }
    }

    public bool HasTenant => TenantId.HasValue;
}
