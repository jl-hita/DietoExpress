namespace Anguloso.Server.Logica.Utils;

/// <summary>
/// Cálculo de huecos dentro de un horario de disponibilidad. Opera con <see cref="TimeSpan"/> porque
/// <see cref="TimeOnly.AddMinutes(double)"/> da la vuelta a medianoche: un horario que termina tarde
/// generaba huecos fuera de rango en un bucle sin fin.
/// </summary>
public static class AvailabilitySlots
{
    /// <summary>Indica si una cita que empieza en <paramref name="start"/> termina dentro del horario.</summary>
    public static bool Fits(TimeOnly start, int durationMinutes, TimeOnly end)
        => durationMinutes > 0 && start.ToTimeSpan() + TimeSpan.FromMinutes(durationMinutes) <= end.ToTimeSpan();

    /// <summary>Horas de inicio posibles, cada <paramref name="stepMinutes"/>, para citas de la duración indicada.</summary>
    public static IEnumerable<TimeOnly> StartTimes(TimeOnly start, TimeOnly end, int durationMinutes, int stepMinutes)
    {
        if (durationMinutes <= 0 || stepMinutes <= 0) yield break;

        var duration = TimeSpan.FromMinutes(durationMinutes);
        var step = TimeSpan.FromMinutes(stepMinutes);
        for (var time = start.ToTimeSpan(); time + duration <= end.ToTimeSpan(); time += step)
            yield return TimeOnly.FromTimeSpan(time);
    }
}
