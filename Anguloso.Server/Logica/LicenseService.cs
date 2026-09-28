using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
namespace Anguloso.Server.Logica;
public interface ILicenseService
{
    Task<LicenseInfo?> GetLicenseAsync(int? tenantId);
    Task<bool> CanUseFeatureAsync(int? tenantId, string featureCode);
    Task<(bool Allowed, string? Reason)> CanCreateClientAsync(int? tenantId, int nutritionistId);
    Task<(bool Allowed, string? Reason)> CanCreateDietAsync(int? tenantId, int userId);
    Task<(bool Allowed, string? Reason)> CanCreateNutritionistAsync(int? tenantId);

}
public sealed class LicenseInfo
{
    public int TenantId { get; init; }
    public string PlanCode { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime? ExpiresAt { get; init; }
    public int Nutritionists { get; init; }
    public int Clients { get; init; }
    public int? MaxNutritionists { get; init; }
    public int? MaxClientsPerNutritionist { get; init; }
    public int? MaxTotalClients { get; init; }
    public List<string> Features { get; init; } = new();
}
public class LicenseService : ILicenseService
{
    private readonly angulosodbContext _context;
    public LicenseService(angulosodbContext context) => _context = context;
    public async Task<LicenseInfo?> GetLicenseAsync(int? tenantId)
    {
        if (!tenantId.HasValue) return null;
        var sub = await _context.subscriptions.AsNoTracking().Include(s => s.plan).Include(s => s.plan.features)
            .Where(s => s.tenant_id == tenantId.Value && s.status != "cancelled" && s.status != "canceled")
            .OrderByDescending(s => s.created_at).FirstOrDefaultAsync();
        if (sub == null) return null;
        var nutritionists = await _context.users.CountAsync(u => u.tenant_id == tenantId && (u.role == "nutritionist" || u.role == "user"));
        var clients = await _context.clients.CountAsync(c => c.tenant_id == tenantId);
        return new LicenseInfo { TenantId = tenantId.Value, PlanCode = sub.plan.code, PlanName = sub.plan.name, Status = sub.status, ExpiresAt = sub.expires_at,
            Nutritionists = nutritionists, Clients = clients, MaxNutritionists = sub.plan.max_nutritionists,
            MaxClientsPerNutritionist = sub.plan.max_clients_per_nutritionist, MaxTotalClients = sub.plan.max_total_clients,
            Features = sub.plan.features.Where(f => f.enabled).Select(f => f.feature_code).ToList() };
    }
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
        if (license.MaxTotalClients.HasValue && license.Clients >= license.MaxTotalClients.Value) return (false, "Se ha alcanzado el límite total de clientes de la licencia.");
        if (license.MaxClientsPerNutritionist.HasValue)
        {
            var count = await _context.clients.CountAsync(c => c.tenant_id == tenantId && c.user_id == nutritionistId);
            if (count >= license.MaxClientsPerNutritionist.Value) return (false, "Este nutricionista ha alcanzado su límite de clientes.");
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

    public async Task<(bool Allowed, string? Reason)> CanCreateNutritionistAsync(int? tenantId)
    {
        var license = await GetLicenseAsync(tenantId);
        if (license == null || license.Status != "active") return (false, "La licencia no está activa.");
        if (license.MaxNutritionists.HasValue && license.Nutritionists >= license.MaxNutritionists.Value) return (false, "Se ha alcanzado el límite de nutricionistas de la licencia.");
        return (true, null);
    }
}