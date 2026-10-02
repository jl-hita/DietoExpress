using System.Security.Claims;

namespace Anguloso.Server.Logica.Utils;

// Documentación: este componente encapsula lógica compartida para mantener las reglas y transformaciones fuera de los puntos de entrada del frontend.
public static class AuthHelpers
{
    public static int? GetUserId(ClaimsPrincipal user)
    {
        if (IsPatient(user)) return null;
        if (user?.FindFirst(ClaimTypes.NameIdentifier)?.Value is string idStr
            && int.TryParse(idStr, out var id))
            return id;
        return null;
    }

    public static int? GetClientId(ClaimsPrincipal user)
    {
        if (user?.FindFirst("clientId")?.Value is string idStr
            && int.TryParse(idStr, out var id))
            return id;
        return null;
    }

    public static int? GetTenantId(ClaimsPrincipal user)
    {
        if (user?.FindFirst("tenantId")?.Value is string idStr
            && int.TryParse(idStr, out var id))
            return id;
        return null;
    }

    public static bool IsPatient(ClaimsPrincipal user)
    {
        return user?.IsInRole("patient") ?? false;
    }
}
