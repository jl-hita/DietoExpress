using System.Security.Claims;

namespace Anguloso.Server.Logica.Utils;

public static class AuthHelpers
{
    public static int? GetUserId(ClaimsPrincipal user)
    {
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

    public static bool IsPatient(ClaimsPrincipal user)
    {
        return user?.IsInRole("patient") ?? false;
    }
}
