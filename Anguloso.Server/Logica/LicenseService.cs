using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
namespace Anguloso.Server.Logica;
public interface ILicenseService
{
    Task<LicenseInfo?> GetLicenseAsync(int? tenantId);
    Task<bool> CanUseFeatureAsync(int? tenantId, string featureCode);
    Task<(bool Allowed, string? Reason)> CanCreateClientAsync(int? tenantId, int nutritionistId);
    Task<(bool Allowed, string? Reason)> CanAssignClientAsync(int? tenantId, int nutritionistId, int clientId);
    Task<(bool Allowed, string? Reason)> CanCreateDietAsync(int? tenantId, int userId);
    Task<(bool Allowed, string? Reason)> CanCreateNutritionistAsync(int? tenantId, bool allowReactivation = false);

}
public sealed class LicenseInfo
{
    public int TenantId { get; init; }
    public string PlanCode { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime? ExpiresAt { get; init; }
    public DateTime? CurrentPeriodStart { get; init; }
    public DateTime? CurrentPeriodEnd { get; init; }
    public string? BillingInterval { get; init; }
    public bool CancelAtPeriodEnd { get; init; }
    public int Nutritionists { get; init; }
    public int Clients { get; init; }
    public int? MaxNutritionists { get; init; }
    public int? MaxClientsPerNutritionist { get; init; }
    public int? MaxTotalClients { get; init; }
    public DateTime? NutritionistReplacementAvailableAt { get; init; }
    public List<string> Features { get; init; } = new();
}
// Documentación: este componente encapsula lógica compartida para mantener las reglas y transformaciones fuera de los puntos de entrada del frontend.
// La licencia efectiva se resuelve combinando el contexto del tenant con su estado persistido; el consumidor no debe decidir el alcance por su cuenta.
public class LicenseService : ILicenseService
{
    // Una baja no libera inmediatamente una plaza para crear otra cuenta.
    // La misma cuenta desactivada sí puede reactivarse en cualquier momento.
    public static readonly TimeSpan NutritionistReplacementCooldown = TimeSpan.FromDays(30);

    private readonly angulosodbContext _context;
    public LicenseService(angulosodbContext context) => _context = context;
    public async Task<LicenseInfo?> GetLicenseAsync(int? tenantId)
    {
        if (!tenantId.HasValue) return null;
        var sub = await _context.subscriptions.AsNoTracking().Include(s => s.plan).Include(s => s.plan.features)
            .Where(s => s.tenant_id == tenantId.Value && s.status != "cancelled" && s.status != "canceled")
            .OrderByDescending(s => s.created_at).FirstOrDefaultAsync();
        if (sub == null) return null;
        var nutritionists = await _context.users.CountAsync(u => u.tenant_id == tenantId && u.archived_at == null && u.role == "nutritionist");
        var clients = await _context.clients.CountAsync(c => c.tenant_id == tenantId && c.archived_at == null);
        return new LicenseInfo { TenantId = tenantId.Value, PlanCode = sub.plan.code, PlanName = sub.plan.name, Status = sub.status, ExpiresAt = sub.expires_at,
            CurrentPeriodStart = sub.current_period_start, CurrentPeriodEnd = sub.current_period_end,
            BillingInterval = sub.billing_interval, CancelAtPeriodEnd = sub.cancel_at_period_end,
            Nutritionists = nutritionists, Clients = clients, MaxNutritionists = sub.plan.max_nutritionists,
            MaxClientsPerNutritionist = sub.plan.max_clients_per_nutritionist, MaxTotalClients = sub.plan.max_total_clients,
            Features = sub.plan.features.Where(f => f.enabled).Select(f => f.feature_code).ToList(),
            NutritionistReplacementAvailableAt = await GetNutritionistReplacementAvailableAtAsync(tenantId.Value) };
    }
    // La comprobación de una funcionalidad parte siempre de la licencia efectiva del tenant y además valida su vigencia temporal.
    public async Task<bool> CanUseFeatureAsync(int? tenantId, string featureCode)
    {
        var license = await GetLicenseAsync(tenantId);
        return license != null && license.Status == "active" && (!license.ExpiresAt.HasValue || license.ExpiresAt.Value > DateTime.UtcNow) && license.Features.Contains(featureCode, StringComparer.OrdinalIgnoreCase);
    }
    public async Task<(bool Allowed, string? Reason)> CanCreateClientAsync(int? tenantId, int nutritionistId)
    {
        var license = await GetLicenseAsync(tenantId);
        if (license == null || license.Status != "active") return (false, "La licencia no está activa.");
        if (license.ExpiresAt.HasValue && license.ExpiresAt.Value <= DateTime.UtcNow) return (false, "La licencia ha caducado.");
        if (!tenantId.HasValue) return (false, "La organización no es válida.");

        var nutritionist = await _context.users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.id == nutritionistId &&
                                      u.tenant_id == tenantId.Value &&
                                      u.archived_at == null &&
                                      u.role == "nutritionist");
        if (nutritionist == null) return (false, "El nutricionista no pertenece a la organización.");

        if (license.MaxTotalClients.HasValue && license.Clients >= license.MaxTotalClients.Value) return (false, "Se ha alcanzado el límite total de clientes de la licencia.");
        if (license.MaxClientsPerNutritionist.HasValue)
        {
            var count = await _context.clients.CountAsync(c => c.tenant_id == tenantId && c.archived_at == null && c.user_id == nutritionistId);
            if (count >= license.MaxClientsPerNutritionist.Value) return (false, "Este nutricionista ha alcanzado su límite de clientes.");
        }
        return (true, null);
    }
    public async Task<(bool Allowed, string? Reason)> CanAssignClientAsync(int? tenantId, int nutritionistId, int clientId)
    {
        var license = await GetLicenseAsync(tenantId);
        if (license == null || license.Status != "active") return (false, "La licencia no está activa.");
        if (license.ExpiresAt.HasValue && license.ExpiresAt.Value <= DateTime.UtcNow) return (false, "La licencia ha caducado.");
        if (!tenantId.HasValue) return (false, "La organización no es válida.");

        var nutritionist = await _context.users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.id == nutritionistId &&
                                      u.tenant_id == tenantId.Value &&
                                      u.archived_at == null &&
                                      u.role == "nutritionist");
        if (nutritionist == null) return (false, "El nutricionista no pertenece a la organización.");

        var client = await _context.clients.AsNoTracking()
            .FirstOrDefaultAsync(c => c.id == clientId &&
                                      c.tenant_id == tenantId.Value &&
                                      c.archived_at == null);
        if (client == null) return (false, "El cliente no pertenece a la organización.");

        if (license.MaxClientsPerNutritionist.HasValue)
        {
            var count = await _context.clients.CountAsync(c =>
                c.tenant_id == tenantId &&
                c.archived_at == null &&
                c.user_id == nutritionistId &&
                c.id != clientId);

            if (count >= license.MaxClientsPerNutritionist.Value)
                return (false, "Este nutricionista ha alcanzado su límite de clientes.");
        }

        return (true, null);
    }

    public async Task<(bool Allowed, string? Reason)> CanCreateDietAsync(int? tenantId, int userId)
    {
        var license = await GetLicenseAsync(tenantId);
        if (license == null || license.Status != "active") return (false, "La licencia no está activa.");
        if (license.ExpiresAt.HasValue && license.ExpiresAt.Value <= DateTime.UtcNow) return (false, "La licencia ha caducado.");

        if (string.Equals(license.PlanCode, "free", StringComparison.OrdinalIgnoreCase))
        {
            var count = await _context.diets.CountAsync(d => d.tenant_id == tenantId && d.user_id == userId);
            if (count >= 1) return (false, "La cuenta gratuita permite crear una sola dieta.");
        }

        return (true, null);
    }

    public async Task<(bool Allowed, string? Reason)> CanCreateNutritionistAsync(int? tenantId, bool allowReactivation = false)
    {
        if (!tenantId.HasValue) return (false, "La licencia no está activa.");

        var tenantIdValue = tenantId.Value;
        var license = await GetLicenseAsync(tenantIdValue);
        if (license == null || license.Status != "active") return (false, "La licencia no está activa.");
        if (license.ExpiresAt.HasValue && license.ExpiresAt.Value <= DateTime.UtcNow) return (false, "La licencia ha caducado.");
        if (license.MaxNutritionists.HasValue && license.Nutritionists >= license.MaxNutritionists.Value)
            return (false, "Se ha alcanzado el límite de nutricionistas activos de la licencia.");

        // Las plazas liberadas quedan temporalmente bloqueadas para evitar que una baja se convierta inmediatamente en una sustitución;
        // la excepción de reactivación permite recuperar la misma cuenta sin consumir una plaza nueva.
        var replacementAvailableAt = await GetNutritionistReplacementAvailableAtAsync(tenantIdValue);
        if (!allowReactivation && replacementAvailableAt.HasValue && replacementAvailableAt.Value > DateTime.UtcNow)
            return (false, $"Una plaza liberada recientemente está en periodo de sustitución hasta {replacementAvailableAt.Value:dd/MM/yyyy HH:mm} UTC.");

        return (true, null);
    }

    private async Task<DateTime?> GetNutritionistReplacementAvailableAtAsync(int tenantId)
    {
        var cutoff = DateTime.UtcNow.Subtract(NutritionistReplacementCooldown);
        var latestDeactivation = await _context.users
            .Where(u => u.tenant_id == tenantId
                        && u.archived_at != null
                        && u.archived_at > cutoff
                        && u.role == "nutritionist")
            .MaxAsync(u => (DateTime?)u.archived_at);

        return latestDeactivation?.Add(NutritionistReplacementCooldown);
    }

}