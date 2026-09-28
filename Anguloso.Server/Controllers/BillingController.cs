using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/billing")]
public sealed class BillingController : ControllerBase
{
    private readonly IStripeBillingService _stripe;
    private readonly angulosodbContext _context;
    private readonly ITenantContextService _tenantContext;
    private readonly IConfiguration _configuration;

    public BillingController(
        IStripeBillingService stripe,
        angulosodbContext context,
        ITenantContextService tenantContext,
        IConfiguration configuration)
    {
        _stripe = stripe;
        _context = context;
        _tenantContext = tenantContext;
        _configuration = configuration;
    }

    [Authorize]
    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans()
    {
        var plans = await _context.subscription_plans
            .AsNoTracking()
            .Where(p => p.active && p.code != "free" && p.code != "demo_nutri" && p.code != "trial_nutri")
            .OrderBy(p => p.id)
            .Select(p => new BillingPlanResponse(
                p.id,
                p.code,
                p.name,
                p.description,
                p.monthly_price,
                p.yearly_price,
                p.max_nutritionists,
                p.max_clients_per_nutritionist,
                p.max_total_clients,
                p.stripe_monthly_price_id != null && p.stripe_monthly_price_id != "",
                p.stripe_yearly_price_id != null && p.stripe_yearly_price_id != ""))
            .ToListAsync();

        return Ok(plans);
    }

    [Authorize]
    [HttpPost("checkout")]
    public async Task<IActionResult> CreateCheckout([FromBody] CheckoutRequest request)
    {
        if (!_tenantContext.TenantId.HasValue)
            return BadRequest("La cuenta no tiene una organización asociada.");

        if (string.IsNullOrWhiteSpace(request.PlanCode))
            return BadRequest("El plan es obligatorio.");

        try
        {
            var url = await _stripe.CreateCheckoutSessionAsync(
                _tenantContext.TenantId.Value,
                request.PlanCode.Trim().ToLowerInvariant(),
                request.BillingInterval.Trim().ToLowerInvariant(),
                request.SuccessUrl,
                request.CancelUrl);

            return Ok(new { url });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [AllowAnonymous]
    [HttpPost("stripe/webhook")]
    public async Task<IActionResult> StripeWebhook()
    {
        var webhookSecret = _configuration["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(webhookSecret))
            return StatusCode(500, "Stripe webhook no configurado.");

        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var payload = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        var signature = Request.Headers["Stripe-Signature"].FirstOrDefault();
        if (!VerifyStripeSignature(payload, signature, webhookSecret))
            return BadRequest("Firma de Stripe no válida.");

        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        var eventId = root.GetProperty("id").GetString() ?? string.Empty;
        var eventType = root.GetProperty("type").GetString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(eventId))
            return BadRequest("Evento de Stripe sin identificador.");

        var paymentEvent = new payment_events
        {
            provider = "stripe",
            event_id = eventId,
            event_type = eventType,
            payload = payload,
            received_at = DateTime.UtcNow,
            status = "received"
        };

        _context.payment_events.Add(paymentEvent);

        try
        {
            await ProcessStripeEventAsync(root, eventType);
            paymentEvent.status = "processed";
            paymentEvent.processed_at = DateTime.UtcNow;
        }
        catch (DbUpdateException)
        {
            _context.Entry(paymentEvent).State = EntityState.Detached;
            return Ok();
        }
        catch (Exception ex)
        {
            paymentEvent.status = "error";
            paymentEvent.error = ex.Message;
            await _context.SaveChangesAsync();
            return StatusCode(500);
        }

        await _context.SaveChangesAsync();
        return Ok();
    }

    private async Task ProcessStripeEventAsync(JsonElement root, string eventType)
    {
        var data = root.GetProperty("data").GetProperty("object");

        switch (eventType)
        {
            case "checkout.session.completed":
            {
                var tenantId = ReadIntMetadata(data, "tenant_id");
                var planId = ReadIntMetadata(data, "plan_id");
                var interval = ReadMetadata(data, "billing_interval");

                if (!tenantId.HasValue || !planId.HasValue)
                    return;

                var subscriptionId = ReadString(data, "subscription");
                var customerId = ReadString(data, "customer");
                var paymentStatus = ReadString(data, "payment_status");

                var subscription = await _context.subscriptions
                    .FirstOrDefaultAsync(s => s.tenant_id == tenantId.Value);

                if (subscription == null)
                    return;

                subscription.plan_id = planId.Value;
                subscription.billing_interval = interval;
                subscription.payment_provider = "stripe";
                subscription.provider_customer_id = customerId;
                subscription.provider_subscription_id = subscriptionId;
                subscription.status = paymentStatus == "paid" ? "active" : "past_due";
                subscription.cancel_at_period_end = false;
                subscription.updated_at = DateTime.UtcNow;

                var plan = await _context.subscription_plans.FindAsync(planId.Value);
                if (plan != null)
                {
                    subscription.amount = interval == "yearly" ? plan.yearly_price : plan.monthly_price;
                    subscription.currency = "eur";
                }

                _context.subscription_events.Add(new subscription_events
                {
                    subscription_id = subscription.id,
                    event_type = "CHECKOUT_COMPLETED",
                    old_plan_id = null,
                    new_plan_id = planId.Value,
                    details = JsonSerializer.Serialize(new
                    {
                        stripe_event = root.GetProperty("id").GetString(),
                        stripe_session = ReadString(data, "id"),
                        stripe_subscription = subscriptionId
                    })
                });

                break;
            }

            case "invoice.paid":
            {
                var providerInvoiceId = ReadString(data, "id");
                var providerSubscriptionId = ReadString(data, "subscription");

                if (string.IsNullOrWhiteSpace(providerInvoiceId) || string.IsNullOrWhiteSpace(providerSubscriptionId))
                    return;

                var subscription = await _context.subscriptions
                    .FirstOrDefaultAsync(s => s.provider_subscription_id == providerSubscriptionId);

                if (subscription == null)
                    return;

                subscription.status = "active";
                subscription.updated_at = DateTime.UtcNow;

                var amountPaid = ReadDecimalMinorUnits(data, "amount_paid");
                var currency = ReadString(data, "currency") ?? "eur";

                var existing = await _context.Set<subscription_payments>()
                    .FirstOrDefaultAsync(p => p.provider == "stripe" && p.provider_payment_id == providerInvoiceId);

                if (existing == null)
                {
                    _context.Set<subscription_payments>().Add(new subscription_payments
                    {
                        subscription_id = subscription.id,
                        provider = "stripe",
                        provider_payment_id = providerInvoiceId,
                        provider_invoice_id = providerInvoiceId,
                        status = "paid",
                        amount = amountPaid,
                        currency = currency,
                        paid_at = DateTime.UtcNow,
                        invoice_number = ReadString(data, "number"),
                        invoice_url = ReadString(data, "hosted_invoice_url")
                    });
                }

                break;
            }

            case "customer.subscription.updated":
            case "customer.subscription.deleted":
            {
                var providerSubscriptionId = ReadString(data, "id");
                if (string.IsNullOrWhiteSpace(providerSubscriptionId))
                    return;

                var subscription = await _context.subscriptions
                    .FirstOrDefaultAsync(s => s.provider_subscription_id == providerSubscriptionId);

                if (subscription == null)
                    return;

                subscription.status = eventType == "customer.subscription.deleted"
                    ? "cancelled"
                    : MapStripeSubscriptionStatus(ReadString(data, "status"));

                subscription.cancel_at_period_end = ReadBool(data, "cancel_at_period_end");

                var metadataPlanId = ReadIntMetadata(data, "plan_id");
                if (metadataPlanId.HasValue)
                    subscription.plan_id = metadataPlanId.Value;

                var metadataInterval = ReadMetadata(data, "billing_interval");
                if (!string.IsNullOrWhiteSpace(metadataInterval))
                    subscription.billing_interval = metadataInterval;
                subscription.updated_at = DateTime.UtcNow;

                if (data.TryGetProperty("current_period_start", out var start) && start.ValueKind == JsonValueKind.Number)
                    subscription.current_period_start = DateTimeOffset.FromUnixTimeSeconds(start.GetInt64()).UtcDateTime;

                if (data.TryGetProperty("current_period_end", out var end) && end.ValueKind == JsonValueKind.Number)
                    subscription.current_period_end = DateTimeOffset.FromUnixTimeSeconds(end.GetInt64()).UtcDateTime;

                if (subscription.cancel_at_period_end)
                    subscription.cancelled_at = subscription.current_period_end;

                break;
            }
        }
    }

    private static string MapStripeSubscriptionStatus(string? status) => status switch
    {
        "active" => "active",
        "trialing" => "active",
        "past_due" => "past_due",
        "unpaid" => "past_due",
        "canceled" => "cancelled",
        "incomplete" => "past_due",
        "incomplete_expired" => "cancelled",
        _ => "past_due"
    };

    private static bool VerifyStripeSignature(string payload, string? signatureHeader, string secret)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        long timestamp = 0;
        string? v1 = null;

        foreach (var item in signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = item.Split('=', 2);
            if (parts.Length != 2) continue;
            if (parts[0] == "t") long.TryParse(parts[1], out timestamp);
            if (parts[0] == "v1") v1 = parts[1];
        }

        if (timestamp == 0 || string.IsNullOrWhiteSpace(v1))
            return false;

        var age = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp);
        if (age > 300)
            return false;

        var signedPayload = $"{timestamp}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload))).ToLowerInvariant();

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(v1));
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadMetadata(JsonElement element, string key)
    {
        if (!element.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
            return null;
        return metadata.TryGetProperty(key, out var value) ? value.GetString() : null;
    }

    private static int? ReadIntMetadata(JsonElement element, string key)
        => int.TryParse(ReadMetadata(element, key), out var value) ? value : null;

    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool ReadBool(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static decimal ReadDecimalMinorUnits(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number)
            return 0m;
        return value.GetDecimal() / 100m;
    }
}

public sealed record CheckoutRequest(
    string PlanCode,
    string BillingInterval,
    string SuccessUrl,
    string CancelUrl);

public sealed record ChangeSubscriptionRequest(
    string PlanCode,
    string BillingInterval);

public sealed record BillingPlanResponse(
    int Id,
    string Code,
    string Name,
    string? Description,
    decimal MonthlyPrice,
    decimal YearlyPrice,
    int? MaxNutritionists,
    int? MaxClientsPerNutritionist,
    int? MaxTotalClients,
    bool HasMonthlyStripePrice,
    bool HasYearlyStripePrice);
