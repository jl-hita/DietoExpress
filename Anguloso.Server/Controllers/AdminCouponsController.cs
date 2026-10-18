using System.Security.Claims;
using System.Security.Cryptography;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/admin/coupons")]
[Authorize(Roles = "superadmin")]
public sealed class AdminCouponsController : ControllerBase
{
    private readonly angulosodbContext _context;
    public AdminCouponsController(angulosodbContext context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var rows = await _context.Database.SqlQueryRaw<CouponAdminRow>("""
            SELECT c.id AS "Id", c.campaign_id AS "CampaignId", c.code AS "Code",
                   c.discount_percent AS "DiscountPercent", c.allowed_plan_codes AS "AllowedPlanCodes",
                   c.allowed_intervals AS "AllowedIntervals", c.starts_at AS "StartsAt",
                   c.expires_at AS "ExpiresAt", c.is_active AS "IsActive", c.created_at AS "CreatedAt",
                   COUNT(r.id) FILTER (WHERE r.status = 'redeemed')::int AS "RedeemedCount",
                   MAX(r.redeemed_at) AS "LastRedeemedAt"
            FROM billing_coupons c
            LEFT JOIN billing_coupon_redemptions r ON r.coupon_id = c.id
            GROUP BY c.id
            ORDER BY c.created_at DESC
            LIMIT 500
            """).ToListAsync(cancellationToken);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CouponCreateRequest request, CancellationToken cancellationToken)
    {
        var code = NormalizeCode(request.Code);
        if (code == null) return BadRequest("El código debe contener entre 4 y 64 caracteres alfanuméricos o guiones.");
        if (!ValidateDiscountAndEligibility(request.DiscountPercent, request.AllowedPlanCodes, request.AllowedIntervals))
            return BadRequest("El descuento o los planes/periodos permitidos no son válidos.");
        if (request.StartsAt.HasValue && request.ExpiresAt.HasValue && request.ExpiresAt <= request.StartsAt)
            return BadRequest("La fecha de caducidad debe ser posterior al inicio.");
        var userId = GetUserId();
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO billing_coupons
                    (code, discount_percent, allowed_plan_codes, allowed_intervals, starts_at, expires_at, created_by_user_id)
                VALUES ({code}, {request.DiscountPercent}, {request.AllowedPlanCodes}, {request.AllowedIntervals},
                        {request.StartsAt}, {request.ExpiresAt}, {userId})
                """, cancellationToken);
            var id = await _context.Database.SqlQueryRaw<long>("""
                SELECT currval(pg_get_serial_sequence('billing_coupons', 'id')) AS "Value"
                """).SingleAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CreatedAtAction(nameof(List), new { id }, new { id, code, request.DiscountPercent });
        }
        catch (DbUpdateException) { return Conflict("Ya existe un cupón con ese código."); }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        { return Conflict("Ya existe un cupón con ese código."); }
    }

    [HttpPost("campaigns")]
    public async Task<IActionResult> CreateCampaign([FromBody] CouponCampaignRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 ||
            request.CodeCount is < 1 or > 10000 ||
            !ValidateDiscountAndEligibility(request.DiscountPercent, request.AllowedPlanCodes, request.AllowedIntervals) ||
            (request.StartsAt.HasValue && request.ExpiresAt.HasValue && request.ExpiresAt <= request.StartsAt))
            return BadRequest("Los datos de la campaña no son válidos.");

        var userId = GetUserId();
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO billing_coupon_campaigns
                (name, discount_percent, allowed_plan_codes, allowed_intervals, starts_at, expires_at, requested_codes, created_by_user_id)
            VALUES ({name}, {request.DiscountPercent}, {request.AllowedPlanCodes}, {request.AllowedIntervals},
                    {request.StartsAt}, {request.ExpiresAt}, {request.CodeCount}, {userId})
            """, cancellationToken);
        var campaignId = await _context.Database.SqlQueryRaw<long>("""
            SELECT currval(pg_get_serial_sequence('billing_coupon_campaigns', 'id')) AS "Value"
            """).SingleAsync(cancellationToken);

        var codes = new List<string>(request.CodeCount);
        for (var i = 0; i < request.CodeCount; i++)
        {
            var code = $"DX-{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO billing_coupons
                    (campaign_id, code, discount_percent, allowed_plan_codes, allowed_intervals, starts_at, expires_at, created_by_user_id)
                VALUES ({campaignId}, {code}, {request.DiscountPercent}, {request.AllowedPlanCodes},
                        {request.AllowedIntervals}, {request.StartsAt}, {request.ExpiresAt}, {userId})
                """, cancellationToken);
            codes.Add(code);
        }

        await transaction.CommitAsync(cancellationToken);
        return Ok(new { campaignId, name, count = codes.Count, codes });
    }

    [HttpPut("{id:long}/active")]
    public async Task<IActionResult> SetActive(long id, [FromBody] CouponActiveRequest request, CancellationToken cancellationToken)
    {
        var affected = await _context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE billing_coupons SET is_active = {request.IsActive} WHERE id = {id}
            """, cancellationToken);
        if (affected == 0) return NotFound();
        return NoContent();
    }

    private int? GetUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static string? NormalizeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var code = value.Trim().ToUpperInvariant();
        return code.Length is >= 4 and <= 64 && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? code : null;
    }

    private static bool ValidateDiscountAndEligibility(int discount, string[]? plans, string[]? intervals) =>
        discount is >= 1 and <= 100 &&
        plans is { Length: > 0 } && plans.All(p => p is "nutri_full" or "clinic_full") &&
        intervals is { Length: > 0 } && intervals.All(i => i is "monthly" or "yearly");

    public sealed record CouponCreateRequest(string? Code, int DiscountPercent, string[] AllowedPlanCodes,
        string[] AllowedIntervals, DateTimeOffset? StartsAt, DateTimeOffset? ExpiresAt);
    public sealed record CouponCampaignRequest(string Name, int DiscountPercent, string[] AllowedPlanCodes,
        string[] AllowedIntervals, DateTimeOffset? StartsAt, DateTimeOffset? ExpiresAt, int CodeCount);
    public sealed record CouponActiveRequest(bool IsActive);
    public sealed record CouponAdminRow(long Id, long? CampaignId, string Code, int DiscountPercent,
        string[] AllowedPlanCodes, string[] AllowedIntervals, DateTimeOffset? StartsAt,
        DateTimeOffset? ExpiresAt, bool IsActive, DateTimeOffset CreatedAt, int RedeemedCount,
        DateTimeOffset? LastRedeemedAt);
}
