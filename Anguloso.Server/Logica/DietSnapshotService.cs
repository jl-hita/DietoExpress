using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Copias archivadas de dietas para que el historial de un paciente siga mostrando lo que realmente siguió, aunque la
/// dieta original se edite después o el paciente cambie de clínica.
/// </summary>
public static class DietSnapshots
{
    /// <summary>Crea una copia archivada de la dieta y devuelve su identificador, o null si no existe.</summary>
    public static async Task<int?> CreateArchivedCopyAsync(
        angulosodbContext context, int dietId, int? ownerUserId, int? tenantId, bool keepSharing = false,
        CancellationToken cancellationToken = default)
    {
        var copy = await context.diets.AsNoTracking()
            .Include(d => d.diet_days).ThenInclude(day => day.meals).ThenInclude(meal => meal.meal_items)
            .FirstOrDefaultAsync(d => d.id == dietId, cancellationToken);
        if (copy == null) return null;

        // La copia se leyó sin seguimiento: basta con vaciar las claves para que EF la inserte como un árbol nuevo.
        copy.id = 0;
        copy.user_id = ownerUserId;
        copy.tenant_id = tenantId;
        // Quien veía el plan por estar compartido debe seguir viéndolo en el historial; una dieta archivada no aparece
        // en la biblioteca.
        copy.is_shared = keepSharing && copy.is_shared;
        copy.is_template = false;
        copy.archived_at = DateTime.UtcNow;
        foreach (var day in copy.diet_days)
        {
            day.id = 0;
            day.diet_id = 0;
            foreach (var meal in day.meals)
            {
                meal.id = 0;
                meal.diet_day_id = 0;
                foreach (var item in meal.meal_items)
                {
                    item.id = 0;
                    item.meal_id = 0;
                }
            }
        }

        context.diets.Add(copy);
        await context.SaveChangesAsync(cancellationToken);
        return copy.id;
    }

    /// <summary>
    /// Traslada el historial de dietas de un paciente que ha cambiado de clínica: cada dieta de la clínica de origen se
    /// copia archivada en la de destino y las asignaciones del paciente pasan a la copia, ya cerradas. Es repetible.
    /// </summary>
    public static async Task MoveHistoryAsync(
        angulosodbContext context, int clientId, int sourceTenantId, int targetTenantId, int targetUserId,
        CancellationToken cancellationToken = default)
    {
        var assignments = await context.client_diets
            .Where(cd => cd.client_id == clientId && cd.diet != null && cd.diet.tenant_id == sourceTenantId)
            .ToListAsync(cancellationToken);

        foreach (var group in assignments.GroupBy(cd => cd.diet_id))
        {
            var copyId = await CreateArchivedCopyAsync(context, group.Key, targetUserId, targetTenantId, cancellationToken: cancellationToken);
            if (copyId == null) continue;
            foreach (var assignment in group)
            {
                assignment.diet_id = copyId.Value;
                if (assignment.is_active == true)
                {
                    assignment.is_active = false;
                    assignment.end_date ??= DateOnly.FromDateTime(DateTime.UtcNow);
                }
            }
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Repara los cambios de clínica confirmados antes de que el historial se trasladara.</summary>
    public static async Task RepairTransferredHistoriesAsync(angulosodbContext context, CancellationToken cancellationToken = default)
    {
        var pending = await context.Database.SqlQueryRaw<TransferredHistory>(
            @"SELECT DISTINCT w.client_id AS ""ClientId"", w.source_tenant_id AS ""SourceTenantId"",
                     w.target_tenant_id AS ""TargetTenantId"", w.target_nutritionist_id AS ""TargetNutritionistId""
              FROM patient_nutritionist_transfer_workflows w
              JOIN clients c ON c.id = w.client_id AND c.tenant_id = w.target_tenant_id
              JOIN client_diets cd ON cd.client_id = w.client_id
              JOIN diets d ON d.id = cd.diet_id AND d.tenant_id = w.source_tenant_id
              WHERE w.status = 'confirmed' AND w.source_tenant_id <> w.target_tenant_id").ToListAsync(cancellationToken);

        foreach (var item in pending)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await MoveHistoryAsync(context, item.ClientId, item.SourceTenantId, item.TargetTenantId, item.TargetNutritionistId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private sealed class TransferredHistory
    {
        public int ClientId { get; set; }
        public int SourceTenantId { get; set; }
        public int TargetTenantId { get; set; }
        public int TargetNutritionistId { get; set; }
    }
}
