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
}
