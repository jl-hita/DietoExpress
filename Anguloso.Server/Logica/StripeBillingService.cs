// Encapsula las operaciones contra Stripe. La base de datos local sigue siendo la fuente de estado de la aplicación; Stripe actúa como proveedor de pagos y origen de los eventos de facturación.
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

public interface IStripeBillingService
{
    Task<string> CreateCheckoutSessionAsync(int tenantId, string planCode, string billingInterval, string successUrl, string cancelUrl, string? couponCode = null, int? userId = null);
    Task ChangeSubscriptionAsync(int tenantId, string planCode, string billingInterval);
    Task CancelRenewalAsync(int tenantId);
    Task ReactivateRenewalAsync(int tenantId);
    Task ChangeNutritionistSeatsAsync(int tenantId, int targetSeats);
}

public sealed class StripeBillingService : IStripeBillingService
{
    private readonly HttpClient _http;
    private readonly angulosodbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StripeBillingService> _logger;

    public StripeBillingService(HttpClient http, angulosodbContext context, IConfiguration configuration, ILogger<StripeBillingService> logger)
    {
        _http = http;
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    // La operación se mantiene asíncrona para no bloquear el hilo de la petición mientras espera I/O (BD, red o almacenamiento).


    public async Task<string> CreateCheckoutSessionAsync(
        int tenantId,
        string planCode,
        string billingInterval,
        string successUrl,
        string cancelUrl,
        string? couponCode = null,
        int? userId = null)
    {
        if (billingInterval is not ("monthly" or "yearly"))
            throw new ArgumentException("El intervalo debe ser monthly o yearly.", nameof(billingInterval));

        var secretKey = GetSecretKey();
        var plan = await GetPlanAsync(planCode);

        var priceId = GetPriceId(plan, billingInterval);

        // Serializa el check-and-create para evitar dos Checkout simultáneos
        // del mismo tenant cuando todavía no existe una suscripción de Stripe.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", tenantId);

        var tenant = await _context.tenants.AsNoTracking().FirstOrDefaultAsync(t => t.id == tenantId);
        if (tenant == null)
            throw new InvalidOperationException("La organización no existe.");

        var existingSubscription = await _context.subscriptions
            .FirstOrDefaultAsync(s => s.tenant_id == tenantId && s.status != "cancelled" && s.status != "canceled");

        if (existingSubscription?.provider_subscription_id is not null &&
            (existingSubscription.status is "active" or "past_due"))
        {
            throw new InvalidOperationException("La cuenta ya tiene una suscripción de pago activa. Usa la opción de cambiar de plan.");
        }

        var pendingAttempt = await _context.billing_checkout_attempts
            .FirstOrDefaultAsync(a => a.tenant_id == tenantId &&
                                      (a.status == "creating" || a.status == "pending"));

        var normalizedCouponCode = string.IsNullOrWhiteSpace(couponCode) ? null : couponCode.Trim().ToUpperInvariant();
        if (normalizedCouponCode != null &&
            (normalizedCouponCode.Length > 64 || normalizedCouponCode.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '-')))
            throw new ArgumentException("El código promocional no tiene un formato válido.", nameof(couponCode));

        if (pendingAttempt != null && pendingAttempt.expires_at > DateTime.UtcNow)
        {
            if (!string.Equals(pendingAttempt.coupon_code, normalizedCouponCode, StringComparison.Ordinal))
                throw new InvalidOperationException("Ya existe un checkout pendiente para esta cuenta. Completa o deja caducar ese checkout antes de cambiar el cupón.");
            if (pendingAttempt.status == "creating" && normalizedCouponCode != null)
                throw new InvalidOperationException("El checkout con cupón anterior todavía se está preparando. Vuelve a intentarlo en unos segundos.");
            await transaction.CommitAsync();

            if (pendingAttempt.status == "pending" && !string.IsNullOrWhiteSpace(pendingAttempt.checkout_url))
                return pendingAttempt.checkout_url!;

            // A request may arrive while another request is between persisting
            // the attempt and receiving Stripe's response. Reusing the same
            // idempotency key guarantees that Stripe will not create a second session.
            var retryForm = new Dictionary<string, string>
            {
                ["mode"] = "subscription",
                ["line_items[0][price]"] = priceId,
                ["line_items[0][quantity]"] = "1",
                ["success_url"] = successUrl,
                ["cancel_url"] = cancelUrl,
                ["client_reference_id"] = tenantId.ToString(CultureInfo.InvariantCulture),
                ["metadata[tenant_id]"] = tenantId.ToString(CultureInfo.InvariantCulture),
                ["metadata[plan_id]"] = plan.id.ToString(CultureInfo.InvariantCulture),
                ["metadata[plan_code]"] = plan.code,
                ["metadata[billing_interval]"] = billingInterval
            };

            if (!string.IsNullOrWhiteSpace(tenant.contact_email))
                retryForm["customer_email"] = tenant.contact_email;

            if (!string.IsNullOrWhiteSpace(existingSubscription?.provider_customer_id))
                retryForm["customer"] = existingSubscription.provider_customer_id;

            using var retryResponse = await SendStripeAsync(
                HttpMethod.Post,
                "/v1/checkout/sessions",
                retryForm,
                pendingAttempt.idempotency_key);
            using var retryJson = JsonDocument.Parse(await retryResponse.Content.ReadAsStringAsync());
            var retryUrl = retryJson.RootElement.GetProperty("url").GetString();
            var retrySessionId = retryJson.RootElement.GetProperty("id").GetString();

            if (string.IsNullOrWhiteSpace(retryUrl) || string.IsNullOrWhiteSpace(retrySessionId))
                throw new InvalidOperationException("Stripe no devolvió una sesión de Checkout válida.");

            await using var completeTransaction = await _context.Database.BeginTransactionAsync();
            var persistedAttempt = await _context.billing_checkout_attempts
                .FirstOrDefaultAsync(a => a.id == pendingAttempt.id);
            if (persistedAttempt == null)
                throw new InvalidOperationException("No se pudo recuperar el intento de Checkout.");

            persistedAttempt.stripe_session_id = retrySessionId;
            persistedAttempt.checkout_url = retryUrl;
            persistedAttempt.status = "pending";
            persistedAttempt.completed_at = null;
            await _context.SaveChangesAsync();
            await completeTransaction.CommitAsync();
            return retryUrl;
        }

        if (pendingAttempt != null)
        {
            pendingAttempt.status = "expired";
            pendingAttempt.completed_at = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        long? couponRedemptionId = null;
        string? stripeCouponId = null;
        long? couponId = null;
        if (normalizedCouponCode != null)
        {
            if (!userId.HasValue)
                throw new InvalidOperationException("No se ha podido identificar al usuario para reservar el cupón.");

            await _context.Database.ExecuteSqlRawAsync("""
                UPDATE billing_coupon_redemptions
                SET status = 'released'
                WHERE status = 'reserved' AND reserved_until IS NOT NULL AND reserved_until < NOW()
                """);

            var candidate = await _context.Database.SqlQueryRaw<CouponCheckoutInfo>("""
                SELECT id AS "Id", discount_percent AS "DiscountPercent",
                       allowed_plan_codes AS "AllowedPlanCodes", allowed_intervals AS "AllowedIntervals",
                       starts_at AS "StartsAt", expires_at AS "ExpiresAt", (is_active AND COALESCE((SELECT ca.is_active FROM billing_coupon_campaigns ca WHERE ca.id = billing_coupons.campaign_id), TRUE)) AS "IsActive"
                FROM billing_coupons
                WHERE UPPER(BTRIM(code)) = {0}
                LIMIT 1
                """, normalizedCouponCode).SingleOrDefaultAsync();

            if (candidate == null || !candidate.IsActive)
                throw new InvalidOperationException("El código promocional no existe o no está activo.");

            await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", candidate.Id);
            var coupon = await _context.Database.SqlQueryRaw<CouponCheckoutInfo>("""
                SELECT id AS "Id", discount_percent AS "DiscountPercent",
                       allowed_plan_codes AS "AllowedPlanCodes", allowed_intervals AS "AllowedIntervals",
                       starts_at AS "StartsAt", expires_at AS "ExpiresAt", is_active AS "IsActive"
                FROM billing_coupons
                WHERE id = {0}
                LIMIT 1
                """, candidate.Id).SingleAsync();

            var now = DateTimeOffset.UtcNow;
            if (!coupon.IsActive || (coupon.StartsAt.HasValue && coupon.StartsAt > now) ||
                (coupon.ExpiresAt.HasValue && coupon.ExpiresAt <= now))
                throw new InvalidOperationException("El código promocional todavía no es válido o ha caducado.");
            if (!coupon.AllowedPlanCodes.Contains(plan.code) || !coupon.AllowedIntervals.Contains(billingInterval))
                throw new InvalidOperationException("El código promocional no es válido para este plan o periodo de facturación.");

            var alreadyRedeemed = await _context.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS (
                    SELECT 1 FROM billing_coupon_redemptions
                    WHERE coupon_id = {0} AND status IN ('reserved','redeemed')
                ) AS "Value"
                """, coupon.Id).SingleAsync();
            if (alreadyRedeemed)
                throw new InvalidOperationException("El código promocional ya se ha utilizado o está reservado en otra contratación.");

            couponId = coupon.Id;
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO billing_coupon_redemptions
                    (coupon_id, tenant_id, user_id, status, reserved_until)
                VALUES ({coupon.Id}, {tenantId}, {userId.Value}, 'reserved', {DateTime.UtcNow.AddHours(24)})
                """);
            couponRedemptionId = await _context.Database.SqlQueryRaw<long>("""
                SELECT currval(pg_get_serial_sequence('billing_coupon_redemptions', 'id')) AS "Value"
                """).SingleAsync();

            using var stripeCouponResponse = await SendStripeAsync(HttpMethod.Post, "/v1/coupons",
                new Dictionary<string, string>
                {
                    ["duration"] = "once",
                    ["percent_off"] = coupon.DiscountPercent.ToString(CultureInfo.InvariantCulture),
                    ["name"] = $"DietoExpress {coupon.DiscountPercent}% first invoice"
                },
                $"dietoexpress-coupon-redemption-{couponRedemptionId.Value}");
            using var stripeCouponJson = JsonDocument.Parse(await stripeCouponResponse.Content.ReadAsStringAsync());
            stripeCouponId = stripeCouponJson.RootElement.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(stripeCouponId))
                throw new InvalidOperationException("Stripe no devolvió un identificador de cupón válido.");

            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE billing_coupon_redemptions
                SET stripe_coupon_id = {stripeCouponId}
                WHERE id = {couponRedemptionId.Value}
                """);
        }

        var form = new Dictionary<string, string>
        {
            ["mode"] = "subscription",
            ["line_items[0][price]"] = priceId,
            ["line_items[0][quantity]"] = "1",
            ["success_url"] = successUrl,
            ["cancel_url"] = cancelUrl,
            ["client_reference_id"] = tenantId.ToString(CultureInfo.InvariantCulture),
            ["metadata[tenant_id]"] = tenantId.ToString(CultureInfo.InvariantCulture),
            ["metadata[plan_id]"] = plan.id.ToString(CultureInfo.InvariantCulture),
            ["metadata[plan_code]"] = plan.code,
            ["metadata[billing_interval]"] = billingInterval
        };
        if (stripeCouponId != null && couponId.HasValue && couponRedemptionId.HasValue)
        {
            form["discounts[0][coupon]"] = stripeCouponId;
            form["metadata[coupon_id]"] = couponId.Value.ToString(CultureInfo.InvariantCulture);
            form["metadata[coupon_redemption_id]"] = couponRedemptionId.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(tenant.contact_email))
            form["customer_email"] = tenant.contact_email;

        if (!string.IsNullOrWhiteSpace(existingSubscription?.provider_customer_id))
            form["customer"] = existingSubscription.provider_customer_id;

        // Persistimos la clave ANTES de llamar a Stripe. Si Stripe crea la
        // sesión pero falla el commit posterior, un reintento reutiliza exactamente
        // la misma clave y no puede crear una segunda sesión.
        var attemptId = Guid.NewGuid();
        var idempotencyKey = CreateCheckoutIdempotencyKey(tenantId, plan.id, billingInterval, attemptId);
        var attempt = new billing_checkout_attempts
        {
            tenant_id = tenantId,
            plan_id = plan.id,
            billing_interval = billingInterval,
            coupon_code = normalizedCouponCode,
            idempotency_key = idempotencyKey,
            checkout_url = string.Empty,
            status = "creating",
            created_at = DateTime.UtcNow,
            expires_at = DateTime.UtcNow.AddHours(24)
        };

        _context.billing_checkout_attempts.Add(attempt);
        await _context.SaveChangesAsync();
        if (couponRedemptionId.HasValue)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE billing_coupon_redemptions
                SET checkout_attempt_id = {attempt.id}
                WHERE id = {couponRedemptionId.Value}
                """);
        }
        await transaction.CommitAsync();

        using var response = await SendStripeAsync(HttpMethod.Post, "/v1/checkout/sessions", form, idempotencyKey);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var url = json.RootElement.GetProperty("url").GetString();
        var checkoutSessionId = json.RootElement.GetProperty("id").GetString();

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(checkoutSessionId))
            throw new InvalidOperationException("Stripe no devolvió una sesión de Checkout válida.");

        await using var completionTransaction = await _context.Database.BeginTransactionAsync();
        var persisted = await _context.billing_checkout_attempts
            .FirstOrDefaultAsync(a => a.id == attempt.id);
        if (persisted == null)
            throw new InvalidOperationException("No se pudo recuperar el intento de Checkout.");

        persisted.stripe_session_id = checkoutSessionId;
        persisted.checkout_url = url;
        persisted.status = "pending";
        persisted.completed_at = null;

        await _context.SaveChangesAsync();
        if (couponRedemptionId.HasValue)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE billing_coupon_redemptions
                SET stripe_checkout_session_id = {checkoutSessionId}
                WHERE id = {couponRedemptionId.Value}
                """);
        }
        await completionTransaction.CommitAsync();
        return url;
    }

    public async Task ChangeSubscriptionAsync(int tenantId, string planCode, string billingInterval)
    {
        if (billingInterval is not ("monthly" or "yearly"))
            throw new ArgumentException("El intervalo debe ser monthly o yearly.", nameof(billingInterval));

        var plan = await GetPlanAsync(planCode);
        var priceId = GetPriceId(plan, billingInterval);

        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", tenantId);

        var subscription = await _context.subscriptions
            .Include(s => s.plan)
            .FirstOrDefaultAsync(s => s.tenant_id == tenantId && s.provider_subscription_id != null &&
                                      s.status != "cancelled" && s.status != "canceled");

        if (subscription == null || string.IsNullOrWhiteSpace(subscription.provider_subscription_id))
            throw new InvalidOperationException("No existe una suscripción de Stripe activa para cambiar.");

        if (subscription.cancel_at_period_end)
            throw new InvalidOperationException("La renovación está cancelada para el final del periodo. Reactiva primero la renovación y después cambia de plan.");

        var currentPlan = subscription.plan;
        if (currentPlan?.code == "clinic_full" && plan.code == "nutri_full")
            throw new InvalidOperationException("No se puede cambiar de Enterprise a Professional mientras la suscripción Enterprise siga activa. Cancela la renovación y, cuando termine el periodo actual, podrás contratar Professional.");

        if (subscription.plan_id == plan.id &&
            string.Equals(subscription.billing_interval, billingInterval, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La cuenta ya tiene ese plan y periodo de facturación.");

        var stripeSubscription = await GetStripeSubscriptionAsync(subscription.provider_subscription_id);
        var items = stripeSubscription.GetProperty("items").GetProperty("data");

        JsonElement? baseItem = null;
        JsonElement? additionalItem = null;
        var currentAdditionalMonthly = currentPlan?.stripe_additional_monthly_price_id;
        var currentAdditionalYearly = currentPlan?.stripe_additional_yearly_price_id;

        foreach (var item in items.EnumerateArray())
        {
            var itemPriceId = item.GetProperty("price").GetProperty("id").GetString();
            if (string.Equals(itemPriceId, currentAdditionalMonthly, StringComparison.Ordinal) ||
                string.Equals(itemPriceId, currentAdditionalYearly, StringComparison.Ordinal))
                additionalItem = item;
            else if (!baseItem.HasValue)
                baseItem = item;
        }

        if (!baseItem.HasValue)
            throw new InvalidOperationException("Stripe no devolvió el elemento principal de la suscripción.");

        var form = new Dictionary<string, string>
        {
            ["items[0][id]"] = baseItem.Value.GetProperty("id").GetString() ?? throw new InvalidOperationException("Elemento principal de Stripe sin identificador."),
            ["items[0][price]"] = priceId,
            ["items[0][quantity]"] = "1",
            ["proration_behavior"] = "always_invoice",
            ["payment_behavior"] = "pending_if_incomplete",
            ["metadata[plan_id]"] = plan.id.ToString(CultureInfo.InvariantCulture),
            ["metadata[plan_code]"] = plan.code,
            ["metadata[billing_interval]"] = billingInterval
        };

        if (plan.code == "clinic_full")
        {
            var included = plan.max_nutritionists ?? 0;
            var contracted = subscription.contracted_nutritionists ?? included;
            var targetAdditionalQuantity = Math.Max(0, contracted - included);
            var targetAdditionalPriceId = billingInterval == "yearly"
                ? plan.stripe_additional_yearly_price_id
                : plan.stripe_additional_monthly_price_id;

            if (targetAdditionalQuantity > 0 && string.IsNullOrWhiteSpace(targetAdditionalPriceId))
                throw new InvalidOperationException("Falta configurar el Price ID de los puestos adicionales de Stripe.");

            if (additionalItem.HasValue)
            {
                var additionalId = additionalItem.Value.GetProperty("id").GetString();
                if (string.IsNullOrWhiteSpace(additionalId))
                    throw new InvalidOperationException("Elemento adicional de Stripe sin identificador.");

                if (targetAdditionalQuantity > 0)
                {
                    form["items[1][id]"] = additionalId;
                    form["items[1][price]"] = targetAdditionalPriceId!;
                    form["items[1][quantity]"] = targetAdditionalQuantity.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    form["items[1][id]"] = additionalId;
                    form["items[1][deleted]"] = "true";
                }
            }
            else if (targetAdditionalQuantity > 0)
            {
                form["items[1][price]"] = targetAdditionalPriceId!;
                form["items[1][quantity]"] = targetAdditionalQuantity.ToString(CultureInfo.InvariantCulture);
            }
        }
        else if (additionalItem.HasValue)
        {
            var additionalId = additionalItem.Value.GetProperty("id").GetString();
            if (!string.IsNullOrWhiteSpace(additionalId))
            {
                form["items[1][id]"] = additionalId;
                form["items[1][deleted]"] = "true";
            }
        }

        using var response = await SendStripeAsync(
            HttpMethod.Post,
            $"/v1/subscriptions/{Uri.EscapeDataString(subscription.provider_subscription_id)}",
            form);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var status = json.RootElement.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString()
            : null;

        if (status is "incomplete" or "past_due")
            throw new InvalidOperationException("Stripe no ha podido completar el cambio de plan. Revisa el método de pago.");

        // La BD local se sincroniza con el webhook customer.subscription.updated.
        // Así no adelantamos un cambio de capacidad si Stripe ha dejado la operación
        // como pending_update a la espera del pago de la prorrata.
        await transaction.CommitAsync();
    }

    public async Task ChangeNutritionistSeatsAsync(int tenantId, int targetSeats)
    {
        if (targetSeats < 1)
            throw new ArgumentException("La capacidad debe ser de al menos 1 nutricionista.", nameof(targetSeats));

        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", tenantId);

        var subscription = await _context.subscriptions
            .Include(s => s.plan)
            .FirstOrDefaultAsync(s => s.tenant_id == tenantId &&
                                      s.payment_provider == "stripe" &&
                                      s.provider_subscription_id != null &&
                                      s.status != "cancelled" && s.status != "canceled");

        if (subscription == null || string.IsNullOrWhiteSpace(subscription.provider_subscription_id))
            throw new InvalidOperationException("No existe una suscripción de Stripe activa.");

        if (!string.Equals(subscription.plan.code, "clinic_full", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Los puestos adicionales solo están disponibles para una suscripción de clínica.");

        var included = subscription.plan.max_nutritionists ?? 0;
        if (targetSeats < included)
            throw new InvalidOperationException($"La suscripción Clínica incluye {included} puestos y no puede reducirse por debajo de esa cantidad.");

        var activeNutritionists = await _context.users.CountAsync(u =>
            u.tenant_id == tenantId && u.archived_at == null && u.role == "nutritionist");
        if (targetSeats < activeNutritionists)
            throw new InvalidOperationException($"No puedes contratar {targetSeats} puestos mientras haya {activeNutritionists} nutricionistas activos.");

        if (subscription.cancel_at_period_end)
            throw new InvalidOperationException("La renovación está cancelada. Reactiva primero la renovación.");

        var additionalPriceId = subscription.billing_interval == "yearly"
            ? subscription.plan.stripe_additional_yearly_price_id
            : subscription.plan.stripe_additional_monthly_price_id;
        if (string.IsNullOrWhiteSpace(additionalPriceId))
            throw new InvalidOperationException("Falta configurar en DietoExpress el Price ID de los puestos adicionales de Stripe.");

        var stripeSubscription = await GetStripeSubscriptionAsync(subscription.provider_subscription_id);
        var items = stripeSubscription.GetProperty("items").GetProperty("data");
        string? additionalItemId = null;

        foreach (var item in items.EnumerateArray())
        {
            var price = item.GetProperty("price");
            var priceId = price.TryGetProperty("id", out var priceIdElement) ? priceIdElement.GetString() : null;
            if (!string.Equals(priceId, additionalPriceId, StringComparison.Ordinal))
                continue;

            additionalItemId = item.GetProperty("id").GetString();
            break;
        }

        var targetAdditionalQuantity = targetSeats - included;
        var form = new Dictionary<string, string>
        {
            ["proration_behavior"] = "always_invoice",
            ["payment_behavior"] = "pending_if_incomplete"
        };

        if (targetAdditionalQuantity == 0)
        {
            if (!string.IsNullOrWhiteSpace(additionalItemId))
            {
                form["items[0][id]"] = additionalItemId;
                form["items[0][deleted]"] = "true";
            }
        }
        else if (!string.IsNullOrWhiteSpace(additionalItemId))
        {
            form["items[0][id]"] = additionalItemId;
            form["items[0][quantity]"] = targetAdditionalQuantity.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            form["items[0][price]"] = additionalPriceId;
            form["items[0][quantity]"] = targetAdditionalQuantity.ToString(CultureInfo.InvariantCulture);
        }

        using var response = await SendStripeAsync(
            HttpMethod.Post,
            $"/v1/subscriptions/{Uri.EscapeDataString(subscription.provider_subscription_id)}",
            form);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var status = json.RootElement.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        if (status is "incomplete" or "past_due")
            throw new InvalidOperationException("Stripe no ha podido completar el cambio de puestos. Revisa el método de pago.");

        // La capacidad local se confirma con customer.subscription.updated.
        // Stripe puede dejar una modificación como pending_update si necesita completar
        // el pago de la prorrata; no debemos reflejar el nuevo cupo antes de ese momento.
        await transaction.CommitAsync();
    }

    public async Task CancelRenewalAsync(int tenantId)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", tenantId);

        var subscription = await GetStripeBackedSubscriptionAsync(tenantId);

        if (subscription.cancel_at_period_end)
            return;

        var form = new Dictionary<string, string>
        {
            ["cancel_at_period_end"] = "true"
        };

        await SendStripeAsync(
            HttpMethod.Post,
            $"/v1/subscriptions/{Uri.EscapeDataString(subscription.provider_subscription_id!)}",
            form);

        await transaction.CommitAsync();
    }

    public async Task ReactivateRenewalAsync(int tenantId)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", tenantId);

        var subscription = await GetStripeBackedSubscriptionAsync(tenantId);

        if (!subscription.cancel_at_period_end)
            return;

        var form = new Dictionary<string, string>
        {
            ["cancel_at_period_end"] = "false"
        };

        await SendStripeAsync(
            HttpMethod.Post,
            $"/v1/subscriptions/{Uri.EscapeDataString(subscription.provider_subscription_id!)}",
            form);

        await transaction.CommitAsync();
    }

    private sealed record CouponCheckoutInfo(long Id, int DiscountPercent, string[] AllowedPlanCodes,
        string[] AllowedIntervals, DateTimeOffset? StartsAt, DateTimeOffset? ExpiresAt, bool IsActive);

    private static string CreateCheckoutIdempotencyKey(int tenantId, int planId, string billingInterval, Guid attemptId)
    {
        var input = "checkout:" + tenantId.ToString(CultureInfo.InvariantCulture) + ":" + planId.ToString(CultureInfo.InvariantCulture) + ":" + billingInterval + ":" + attemptId.ToString("N");
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<subscription_plans> GetPlanAsync(string planCode)
    {
        if (string.IsNullOrWhiteSpace(planCode))
            throw new ArgumentException("El plan es obligatorio.", nameof(planCode));

        var plan = await _context.subscription_plans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.code == planCode && p.active);

        if (plan == null || plan.code is "free" or "demo_nutri" or "trial_nutri")
            throw new InvalidOperationException("El plan solicitado no es un plan comercial válido.");

        return plan;
    }

    private static string GetPriceId(subscription_plans plan, string billingInterval)
    {
        var priceId = billingInterval == "monthly"
            ? plan.stripe_monthly_price_id
            : plan.stripe_yearly_price_id;

        if (string.IsNullOrWhiteSpace(priceId))
            throw new InvalidOperationException($"El plan {plan.code} todavía no tiene configurado su Price ID de Stripe para {billingInterval}.");

        return priceId;
    }

    private async Task<subscriptions> GetStripeBackedSubscriptionAsync(int tenantId)
    {
        var subscription = await _context.subscriptions
            .FirstOrDefaultAsync(s => s.tenant_id == tenantId && s.provider_subscription_id != null &&
                                      s.status != "cancelled" && s.status != "canceled");

        if (subscription == null || string.IsNullOrWhiteSpace(subscription.provider_subscription_id))
            throw new InvalidOperationException("No existe una suscripción de Stripe activa.");

        return subscription;
    }

    private async Task<JsonElement> GetStripeSubscriptionAsync(string providerSubscriptionId)
    {
        using var response = await SendStripeAsync(
            HttpMethod.Get,
            $"/v1/subscriptions/{Uri.EscapeDataString(providerSubscriptionId)}");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private string GetSecretKey()
    {
        var secretKey = _configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException("Stripe no está configurado: falta Stripe:SecretKey.");

        return secretKey;
    }

    private async Task<HttpResponseMessage> SendStripeAsync(HttpMethod method, string path, Dictionary<string, string>? form = null, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, $"https://api.stripe.com{path}");
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetSecretKey());

        if (form != null)
            request.Content = new FormUrlEncodedContent(form);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "No se pudo conectar con Stripe para una operación de billing. Método {Method}, ruta {Path}", method.Method, path);
            throw new StripeBillingProviderException(ex);
        }
        catch (TaskCanceledException ex)
        {
            // Stripe HttpClient no recibe un token de cancelación de la petición HTTP entrante;
            // aquí una cancelación suele indicar timeout del proveedor.
            _logger.LogWarning(ex, "Timeout al contactar con Stripe para una operación de billing. Método {Method}, ruta {Path}", method.Method, path);
            throw new StripeBillingProviderException(ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            // El cuerpo de Stripe puede contener detalles internos del proveedor.
            // Nunca lo devolvemos al cliente autenticado: solo registramos el código HTTP.
            _logger.LogWarning("Stripe rechazó una operación de billing. HTTP {StatusCode}, método {Method}, ruta {Path}",
                (int)response.StatusCode, method.Method, path);
            response.Dispose();
            throw new StripeBillingProviderException();
        }

        return response;
    }
}


/// <summary>Fallo devuelto por el proveedor de pagos Stripe.</summary>
public sealed class StripeBillingProviderException : Exception
{
    public StripeBillingProviderException() : base("Stripe no ha podido completar la operación solicitada.") { }
    public StripeBillingProviderException(Exception innerException) : base("Stripe no ha podido completar la operación solicitada.", innerException) { }
}
