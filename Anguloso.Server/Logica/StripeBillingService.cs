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
    Task<string> CreateCheckoutSessionAsync(int tenantId, string planCode, string billingInterval, string successUrl, string cancelUrl);
    Task ChangeSubscriptionAsync(int tenantId, string planCode, string billingInterval);
    Task CancelRenewalAsync(int tenantId);
    Task ReactivateRenewalAsync(int tenantId);
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
        string cancelUrl)
    {
        if (billingInterval is not ("monthly" or "yearly"))
            throw new ArgumentException("El intervalo debe ser monthly o yearly.", nameof(billingInterval));

        var secretKey = GetSecretKey();
        var plan = await GetPlanAsync(planCode);

        var priceId = GetPriceId(plan, billingInterval);

        // El bloqueo transaccional es por tenant y dura solo durante la creación del intento:
        // dos peticiones concurrentes pueden llegar al endpoint, pero solo una puede iniciar el flujo.
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

        if (pendingAttempt != null && pendingAttempt.expires_at > DateTime.UtcNow)
        {
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
            idempotency_key = idempotencyKey,
            checkout_url = string.Empty,
            status = "creating",
            created_at = DateTime.UtcNow,
            expires_at = DateTime.UtcNow.AddHours(24)
        };

        _context.billing_checkout_attempts.Add(attempt);
        await _context.SaveChangesAsync();
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
            .FirstOrDefaultAsync(s => s.tenant_id == tenantId && s.provider_subscription_id != null &&
                                      s.status != "cancelled" && s.status != "canceled");

        if (subscription == null || string.IsNullOrWhiteSpace(subscription.provider_subscription_id))
            throw new InvalidOperationException("No existe una suscripción de Stripe activa para cambiar.");

        if (subscription.cancel_at_period_end)
            throw new InvalidOperationException("La renovación está cancelada para el final del periodo. Reactiva primero la renovación y después cambia de plan.");

        var currentPlan = await _context.subscription_plans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.id == subscription.plan_id);

        if (currentPlan?.code == "clinic_full" && plan.code == "nutri_full")
            throw new InvalidOperationException("No se puede cambiar de Enterprise a Professional mientras la suscripción Enterprise siga activa. Cancela la renovación y, cuando termine el periodo actual, podrás contratar Professional.");

        if (subscription.plan_id == plan.id &&
            string.Equals(subscription.billing_interval, billingInterval, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La cuenta ya tiene ese plan y periodo de facturación.");

        var stripeSubscription = await GetStripeSubscriptionAsync(subscription.provider_subscription_id);
        var item = stripeSubscription.GetProperty("items").GetProperty("data")[0];
        var itemId = item.GetProperty("id").GetString();

        if (string.IsNullOrWhiteSpace(itemId))
            throw new InvalidOperationException("Stripe no devolvió el elemento de suscripción.");

        var form = new Dictionary<string, string>
        {
            ["items[0][id]"] = itemId,
            ["items[0][price]"] = priceId,
            ["proration_behavior"] = "always_invoice",
            ["payment_behavior"] = "pending_if_incomplete",
            ["metadata[plan_id]"] = plan.id.ToString(CultureInfo.InvariantCulture),
            ["metadata[plan_code]"] = plan.code,
            ["metadata[billing_interval]"] = billingInterval
        };

        using var response = await SendStripeAsync(
            HttpMethod.Post,
            $"/v1/subscriptions/{Uri.EscapeDataString(subscription.provider_subscription_id)}",
            form);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var status = json.RootElement.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString()
            : null;

        if (status == "incomplete" || status == "past_due")
            throw new InvalidOperationException("Stripe no ha podido completar el cambio de plan. Revisa el método de pago.");

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
        var request = new HttpRequestMessage(method, $"https://api.stripe.com{path}");
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetSecretKey());

        if (form != null)
            request.Content = new FormUrlEncodedContent(form);

        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            // El cuerpo de Stripe puede contener detalles internos del proveedor.
            // Nunca lo devolvemos al cliente autenticado: solo registramos el código HTTP.
            _logger.LogWarning("Stripe rechazó una operación de billing. HTTP {StatusCode}, método {Method}, ruta {Path}",
                (int)response.StatusCode, method.Method, path);
            throw new InvalidOperationException("Stripe no ha podido completar la operación solicitada. Inténtalo de nuevo o revisa la configuración de facturación.");
        }

        return response;
    }
}
