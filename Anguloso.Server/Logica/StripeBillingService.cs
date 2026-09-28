using System.Globalization;
using System.Net.Http.Headers;
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

    public StripeBillingService(HttpClient http, angulosodbContext context, IConfiguration configuration)
    {
        _http = http;
        _context = context;
        _configuration = configuration;
    }

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

        using var response = await SendStripeAsync(HttpMethod.Post, "/v1/checkout/sessions", form);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var url = json.RootElement.GetProperty("url").GetString();

        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Stripe no devolvió una URL de Checkout válida.");

        return url;
    }

    public async Task ChangeSubscriptionAsync(int tenantId, string planCode, string billingInterval)
    {
        if (billingInterval is not ("monthly" or "yearly"))
            throw new ArgumentException("El intervalo debe ser monthly o yearly.", nameof(billingInterval));

        var plan = await GetPlanAsync(planCode);
        var priceId = GetPriceId(plan, billingInterval);

        var subscription = await _context.subscriptions
            .FirstOrDefaultAsync(s => s.tenant_id == tenantId && s.provider_subscription_id != null &&
                                      s.status != "cancelled" && s.status != "canceled");

        if (subscription == null || string.IsNullOrWhiteSpace(subscription.provider_subscription_id))
            throw new InvalidOperationException("No existe una suscripción de Stripe activa para cambiar.");

        if (subscription.cancel_at_period_end)
            throw new InvalidOperationException("La renovación está cancelada para el final del periodo. Reactiva primero la renovación y después cambia de plan.");

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
    }

    public async Task CancelRenewalAsync(int tenantId)
    {
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
    }

    public async Task ReactivateRenewalAsync(int tenantId)
    {
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

    private async Task<HttpResponseMessage> SendStripeAsync(HttpMethod method, string path, Dictionary<string, string>? form = null)
    {
        var request = new HttpRequestMessage(method, $"https://api.stripe.com{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetSecretKey());

        if (form != null)
            request.Content = new FormUrlEncodedContent(form);

        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Stripe rechazó la operación ({(int)response.StatusCode}): {body}");
        }

        return response;
    }
}
