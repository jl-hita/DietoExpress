using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Serializa las operaciones de reserva por profesional y clínica.
/// PostgreSQL mantiene el lock hasta terminar la transacción actual.
/// Se comparte entre reservas públicas y reservas autenticadas para que ambas
/// vías utilicen la misma barrera contra reservas simultáneas del mismo hueco.
/// </summary>
public sealed class AppointmentConcurrencyService
{
    private readonly angulosodbContext _context;

    public AppointmentConcurrencyService(angulosodbContext context) => _context = context;

    public Task LockAsync(int tenantId, int nutritionistId, CancellationToken cancellationToken = default) =>
        _context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0}, {1})",
            new object[] { tenantId, nutritionistId },
            cancellationToken);
}
