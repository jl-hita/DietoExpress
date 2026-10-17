using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using System;

namespace Anguloso.Server.Logica;

public static class UserTimeZoneResolver
{
    public static async Task<TimeZoneInfo> ForUserAsync(angulosodbContext context, int userId)
    {
        var zoneId = await context.users.AsNoTracking().Where(user => user.id == userId).Select(user => user.time_zone_id).FirstOrDefaultAsync();
        return Resolve(zoneId);
    }

    public static bool TryConvertLocalToUtc(DateTime localDateTime, TimeZoneInfo zone, out DateTime utc)
    {
        var local = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            utc = default;
            return false;
        }

        if (zone.IsAmbiguousTime(local))
        {
            // En el retroceso otoñal una hora local aparece dos veces. Elegimos el primer instante
            // cronológico (offset mayor) para generar un único slot estable.
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            utc = new DateTimeOffset(local, offset).UtcDateTime;
            return true;
        }

        utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
        return true;
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
