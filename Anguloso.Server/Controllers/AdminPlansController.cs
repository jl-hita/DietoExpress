using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/admin/plans")]
[Authorize(Roles = "superadmin")]
public class AdminPlansController : ControllerBase
{
    private readonly angulosodbContext _context;

    public AdminPlansController(angulosodbContext context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var plans = await _context.subscription_plans
            .AsNoTracking()
            .Include(p => p.features)
            .OrderBy(p => p.id)
            .ToListAsync();

        // No devolver las entidades EF directamente: subscription_plans -> features -> plan
        // forma un ciclo de navegación que System.Text.Json intenta serializar.
        return Ok(plans.Select(ToResponse).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PlanRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Code) || string.IsNullOrWhiteSpace(r.Name))
            return BadRequest();

        var code = r.Code.Trim().ToLowerInvariant();
        if (code.Length > 50 || r.Name.Trim().Length > 100 || r.Description?.Length > 2000)
            return BadRequest("Los campos del plan superan los límites permitidos.");

        if (!IsValidPlanLimits(r))
            return BadRequest("Los valores económicos o límites del plan no son válidos.");

        if (await _context.subscription_plans.AnyAsync(p => p.code == code))
            return Conflict("El código ya existe.");

        var p = new subscription_plans
        {
            code = code,
            name = r.Name,
            description = r.Description,
            monthly_price = r.MonthlyPrice,
            yearly_price = r.YearlyPrice,
            max_nutritionists = r.MaxNutritionists,
            max_clients_per_nutritionist = r.MaxClientsPerNutritionist,
            max_total_clients = r.MaxTotalClients,
            trial_days = r.TrialDays,
            stripe_additional_monthly_price_id = r.StripeAdditionalMonthlyPriceId,
            stripe_additional_yearly_price_id = r.StripeAdditionalYearlyPriceId,
            active = r.Active
        };

        _context.subscription_plans.Add(p);
        await _context.SaveChangesAsync();

        return Ok(ToResponse(p));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] PlanRequest r)
    {
        if (!IsValidPlanLimits(r) || string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 100 || r.Description?.Length > 2000)
            return BadRequest("Los valores del plan no son válidos.");

        var p = await _context.subscription_plans.FindAsync(id);

        if (p == null)
            return NotFound();

        p.name = r.Name;
        p.description = r.Description;
        p.monthly_price = r.MonthlyPrice;
        p.yearly_price = r.YearlyPrice;
        p.max_nutritionists = r.MaxNutritionists;
        p.max_clients_per_nutritionist = r.MaxClientsPerNutritionist;
        p.max_total_clients = r.MaxTotalClients;
        p.trial_days = r.TrialDays;
        p.stripe_additional_monthly_price_id = r.StripeAdditionalMonthlyPriceId;
        p.stripe_additional_yearly_price_id = r.StripeAdditionalYearlyPriceId;
        p.active = r.Active;

        await _context.SaveChangesAsync();

        return Ok(ToResponse(p));
    }

    [HttpPut("{id:int}/features")]
    public async Task<IActionResult> Features(int id, [FromBody] List<FeatureRequest> features)
    {
        if (features == null || features.Count > 100)
            return BadRequest("El número de funcionalidades no es válido.");

        if (features.Any(f => string.IsNullOrWhiteSpace(f.FeatureCode) || f.FeatureCode.Trim().Length > 100))
            return BadRequest("Los códigos de funcionalidad no son válidos.");

        var normalizedFeatureCodes = features
            .Select(f => f.FeatureCode.Trim().ToLowerInvariant())
            .ToList();

        if (normalizedFeatureCodes.Distinct(StringComparer.Ordinal).Count() != normalizedFeatureCodes.Count)
            return BadRequest("No puede haber funcionalidades duplicadas.");

        var p = await _context.subscription_plans.FindAsync(id);

        if (p == null)
            return NotFound();

        var old = await _context.subscription_plan_features
            .Where(f => f.plan_id == id)
            .ToListAsync();

        _context.subscription_plan_features.RemoveRange(old);

        _context.subscription_plan_features.AddRange(
            features.Select(f => new subscription_plan_features
            {
                plan_id = id,
                feature_code = f.FeatureCode.Trim().ToLowerInvariant(),
                enabled = f.Enabled
            }));

        await _context.SaveChangesAsync();

        return Ok();
    }

    private static bool IsValidPlanLimits(PlanRequest r)
    {
        return r.MonthlyPrice >= 0m && r.MonthlyPrice <= 1_000_000m &&
               r.YearlyPrice >= 0m && r.YearlyPrice <= 1_000_000m &&
               (!r.MaxNutritionists.HasValue || r.MaxNutritionists.Value >= 0) &&
               (!r.MaxClientsPerNutritionist.HasValue || r.MaxClientsPerNutritionist.Value >= 0) &&
               (!r.MaxTotalClients.HasValue || r.MaxTotalClients.Value >= 0) &&
               (!r.TrialDays.HasValue || r.TrialDays.Value is >= 0 and <= 3650);
    }

    private static PlanResponse ToResponse(subscription_plans p)
    {
        return new PlanResponse(
            p.id,
            p.code,
            p.name,
            p.description,
            p.monthly_price,
            p.yearly_price,
            p.max_nutritionists,
            p.max_clients_per_nutritionist,
            p.max_total_clients,
            p.trial_days,
            p.active,
            p.stripe_additional_monthly_price_id,
            p.stripe_additional_yearly_price_id,
            p.created_at,
            p.features
                .Select(f => new FeatureResponse(
                    f.id,
                    f.plan_id,
                    f.feature_code,
                    f.enabled))
                .ToList());
    }
}

public record PlanRequest(
    string? Code,
    string Name,
    string? Description,
    decimal MonthlyPrice,
    decimal YearlyPrice,
    int? MaxNutritionists,
    int? MaxClientsPerNutritionist,
    int? MaxTotalClients,
    int? TrialDays,
    bool Active,
    string? StripeAdditionalMonthlyPriceId,
    string? StripeAdditionalYearlyPriceId);

public record FeatureRequest(
    [property: JsonPropertyName("feature_code")] string FeatureCode,
    [property: JsonPropertyName("enabled")] bool Enabled);

public record FeatureResponse(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("plan_id")] int PlanId,
    [property: JsonPropertyName("feature_code")] string FeatureCode,
    [property: JsonPropertyName("enabled")] bool Enabled);

public record PlanResponse(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("monthly_price")] decimal MonthlyPrice,
    [property: JsonPropertyName("yearly_price")] decimal YearlyPrice,
    [property: JsonPropertyName("max_nutritionists")] int? MaxNutritionists,
    [property: JsonPropertyName("max_clients_per_nutritionist")] int? MaxClientsPerNutritionist,
    [property: JsonPropertyName("max_total_clients")] int? MaxTotalClients,
    [property: JsonPropertyName("trial_days")] int? TrialDays,
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("features")] List<FeatureResponse> Features);
