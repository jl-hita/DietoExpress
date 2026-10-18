using Anguloso.Server.Models;
using Anguloso.Server.Logica;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/billing/coupons")]
[Authorize(Policy = "Professional")]
public sealed class CouponValidationController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly ITenantContextService _tenantContext;

    public CouponValidationController(angulosodbContext context, ITenantContextService tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] ValidateCouponRequest request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.TenantId.HasValue)
            return BadRequest("La cuenta no tiene una organización asociada.");

        var code = request.Code?.Trim().ToUpperInvariant();
        var planCode = request.PlanCode?.Trim().ToLowerInvariant();
        var interval = request.BillingInterval?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 64 ||
            planCode is not ("nutri_full" or "clinic_full") ||
            interval is not ("monthly" or "yearly"))
            return BadRequest("El código, plan o periodo no son válidos.");

        await _context.Database.ExecuteSqlRawAsync("""
            UPDATE billing_coupon_redemptions
            SET status = 'released'
            WHERE status = 'reserved' AND reserved_until IS NOT NULL AND reserved_until < NOW()
            """, cancellationToken);

        var coupon = await _context.Database.SqlQueryRaw<CouponValidationRow>("""
            SELECT c.id AS "Id", c.discount_percent AS "DiscountPercent",
                   c.allowed_plan_codes AS "AllowedPlanCodes", c.allowed_intervals AS "AllowedIntervals",
                   c.starts_at AS "StartsAt", c.expires_at AS "ExpiresAt", c.is_active AS "IsActive",
                   EXISTS (
                       SELECT 1 FROM billing_coupon_redemptions r
                       WHERE r.coupon_id = c.id AND r.status IN ('reserved','redeemed')
                   ) AS "AlreadyUsed"
            FROM billing_coupons c
            WHERE UPPER(BTRIM(c.code)) = {0}
            LIMIT 1
            """, code).SingleOrDefaultAsync(cancellationToken);

        if (coupon == null || !coupon.IsActive)
            return NotFound(new { valid = false, message = "El código no existe o no está activo." });
        var now = DateTimeOffset.UtcNow;
        if ((coupon.StartsAt.HasValue && coupon.StartsAt > now) ||
            (coupon.ExpiresAt.HasValue && coupon.ExpiresAt <= now))
            return BadRequest(new { valid = false, message = "El código todavía no es válido o ha caducado." });
        if (!coupon.AllowedPlanCodes.Contains(planCode) || !coupon.AllowedIntervals.Contains(interval))
            return BadRequest(new { valid = false, message = "El código no es válido para el plan o periodo seleccionado." });
        if (coupon.AlreadyUsed)
            return Conflict(new { valid = false, message = "El código ya se ha utilizado o está reservado en otra contratación." });

        var price = await _context.subscription_plans.AsNoTracking()
            .Where(p => p.code == planCode && p.active)
            .Select(p => interval == "yearly" ? p.yearly_price : p.monthly_price)
            .SingleOrDefaultAsync(cancellationToken);
        var discount = Math.Round(price * coupon.DiscountPercent / 100m, 2, MidpointRounding.AwayFromZero);
        return Ok(new
        {
            valid = true,
            code,
            discountPercent = coupon.DiscountPercent,
            originalAmount = price,
            discountAmount = discount,
            initialAmount = Math.Max(0m, price - discount),
            appliesToFirstInvoiceOnly = true
        });
    }

    public sealed record ValidateCouponRequest(string? Code, string? PlanCode, string? BillingInterval);
    public sealed record CouponValidationRow(long Id, int DiscountPercent, string[] AllowedPlanCodes,
        string[] AllowedIntervals, DateTimeOffset? StartsAt, DateTimeOffset? ExpiresAt, bool IsActive,
        bool AlreadyUsed);
}
