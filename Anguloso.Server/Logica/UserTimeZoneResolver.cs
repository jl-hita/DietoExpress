using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

public static class UserTimeZoneResolver
{
    public static async Task<TimeZoneInfo> ForUserAsync(angulosodbContext context, int userId)
    {
        var zoneId = await context.users.AsNoTracking().Where(user => user.id == userId).Select(user => user.time_zone_id).FirstOrDefaultAsync();
        return Resolve(zoneId);
    }

    public static TimeZoneInfo Resolve(string? zoneId)
    {
        if (!string.IsNullOrWhiteSpace(zoneId))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(zoneId); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time"); }
    }
}
