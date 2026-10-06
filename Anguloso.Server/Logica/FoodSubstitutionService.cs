using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Busca alimentos alternativos manteniendo de forma aproximada un objetivo nutricional.
/// No sustituye el criterio profesional: devuelve candidatos ordenados por similitud.
/// </summary>
public sealed class FoodSubstitutionService
{
    private readonly angulosodbContext _context;

    public FoodSubstitutionService(angulosodbContext context) => _context = context;

    public async Task<IReadOnlyList<FoodSubstitutionCandidate>> SuggestAsync(
        int foodId,
        int? tenantId,
        bool isSuperAdmin,
        string target,
        IReadOnlyCollection<string>? excludedFlags,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var source = await _context.foods.AsNoTracking()
            .Where(f => f.id == foodId)
            .Select(f => new { f.id, f.kcal, f.protein, f.carbs, f.fat, f.category, f.exchange_group_id })
            .FirstOrDefaultAsync(cancellationToken);

        if (source == null) return Array.Empty<FoodSubstitutionCandidate>();

        target = target.Trim().ToLowerInvariant();
        if (target is not ("balanced" or "kcal" or "protein" or "carbs" or "fat"))
            throw new ArgumentException("El objetivo debe ser balanced, kcal, protein, carbs o fat.", nameof(target));

        var excluded = new HashSet<string>(
            excludedFlags ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        var candidates = await _context.foods.AsNoTracking()
            .Where(f =>
                f.id != source.id &&
                (f.source == null || f.source.ToLower() != "local" ||
                 isSuperAdmin ||
                 (tenantId.HasValue && f.tenant_id == tenantId.Value)))
            .Where(f => f.kcal != null || f.protein != null || f.carbs != null || f.fat != null)
            .Where(f => excluded.Count == 0 || !f.dietary_flags.Any(flag => excluded.Contains(flag)))
            .Select(f => new
            {
                f.id, f.name, f.brands, f.category, f.kcal, f.protein, f.carbs, f.fat,
                f.default_grams, f.exchange_group_id, f.dietary_flags
            })
            .Take(500)
            .ToListAsync(cancellationToken);

        static double Distance(double? a, double? b)
        {
            if (!a.HasValue || !b.HasValue) return 1.0;
            var denominator = Math.Max(Math.Abs(a.Value), 1.0);
            return Math.Min(Math.Abs(a.Value - b.Value) / denominator, 2.0);
        }

        var ranked = candidates
            .Select(f =>
            {
                var score = target switch
                {
                    "kcal" => Distance(f.kcal, source.kcal),
                    "protein" => Distance(f.protein, source.protein),
                    "carbs" => Distance(f.carbs, source.carbs),
                    "fat" => Distance(f.fat, source.fat),
                    _ => (Distance(f.kcal, source.kcal) +
                          Distance(f.protein, source.protein) +
                          Distance(f.carbs, source.carbs) +
                          Distance(f.fat, source.fat)) / 4.0
                };

                // Prefer the same exchange group/category because these candidates
                // are generally more useful as practical substitutions.
                if (source.exchange_group_id.HasValue && f.exchange_group_id == source.exchange_group_id)
                    score *= 0.75;
                else if (!string.IsNullOrWhiteSpace(source.category) &&
                         string.Equals(source.category, f.category, StringComparison.OrdinalIgnoreCase))
                    score *= 0.9;

                return new FoodSubstitutionCandidate(
                    f.id, f.name, f.brands, f.category, f.kcal, f.protein, f.carbs, f.fat,
                    f.default_grams, f.dietary_flags, Math.Round(score, 4));
            })
            .OrderBy(x => x.Score)
            .Take(Math.Clamp(limit, 1, 50))
            .ToList();

        return ranked;
    }
}

public sealed record FoodSubstitutionCandidate(
    int Id,
    string Name,
    string? Brands,
    string? Category,
    double? Kcal,
    double? Protein,
    double? Carbs,
    double? Fat,
    decimal? DefaultGrams,
    string[] DietaryFlags,
    double Score);
