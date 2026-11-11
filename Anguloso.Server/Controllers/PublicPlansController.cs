using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Planes y precios para la página pública. Se leen de la misma tabla que el cobro (editable desde SuperAdmin), de modo que
/// la landing nunca muestra un precio distinto del que se cobra. Solo expone datos comerciales, ningún identificador de Stripe.
/// </summary>
[ApiController]
[Route("api/public/plans")]
[AllowAnonymous]
public class PublicPlansController : ControllerBase
{
    // Planes internos o de prueba que no se venden desde la web.
    private static readonly string[] HiddenPlanCodes = ["free", "demo_nutri", "trial_nutri"];

    private readonly angulosodbContext _context;
    private readonly PublicResponseCache _cache;

    public PublicPlansController(angulosodbContext context, PublicResponseCache cache)
    {
        _context = context;
        _cache = cache;
    }

    [HttpGet]
    [EnableRateLimiting("public-read")]
    public async Task<ActionResult<PublicPlansResponse>> Get(CancellationToken cancellationToken)
    {
        var response = await _cache.GetOrCreateAsync("public:plans", TimeSpan.FromMinutes(5), () => LoadAsync(cancellationToken));
        return Ok(response);
    }

    private async Task<PublicPlansResponse?> LoadAsync(CancellationToken cancellationToken)
    {
        // Un plan solo se anuncia si realmente se puede comprar: tiene precio y ambos precios existen en Stripe.
        var plans = await _context.subscription_plans.AsNoTracking()
            .Where(p => p.active && !HiddenPlanCodes.Contains(p.code) && p.monthly_price > 0 && p.yearly_price > 0 &&
                        p.stripe_monthly_price_id != null && p.stripe_monthly_price_id != "" &&
                        p.stripe_yearly_price_id != null && p.stripe_yearly_price_id != "")
            .OrderBy(p => p.monthly_price)
            .Select(p => new
            {
                p.code, p.name, p.description, p.monthly_price, p.yearly_price,
                p.max_nutritionists, p.max_clients_per_nutritionist, p.max_total_clients,
                Features = p.features.Where(f => f.enabled).Select(f => f.feature_code).ToList()
            })
            .ToListAsync(cancellationToken);

        var trialDays = await _context.subscription_plans.AsNoTracking()
            .Where(p => p.code == "free" && p.active)
            .Select(p => p.trial_days)
            .FirstOrDefaultAsync(cancellationToken);

        return new PublicPlansResponse(
            trialDays is > 0 ? trialDays : null,
            plans.Select(p => new PublicPlanDto(
                p.code, p.name, p.description, p.monthly_price, p.yearly_price,
                p.max_nutritionists, p.max_clients_per_nutritionist, p.max_total_clients,
                p.Features.OrderBy(f => f, StringComparer.Ordinal).ToList())).ToList());
    }
}

public sealed record PublicPlansResponse(int? TrialDays, IReadOnlyList<PublicPlanDto> Plans);

public sealed record PublicPlanDto(
    string Code,
    string Name,
    string? Description,
    decimal MonthlyPrice,
    decimal YearlyPrice,
    int? MaxNutritionists,
    int? MaxClientsPerNutritionist,
    int? MaxTotalClients,
    IReadOnlyList<string> Features);
