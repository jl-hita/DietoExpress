using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

public interface IStripeBillingService
{
    Task<string> CreateCheckoutSessionAsync(int tenantId, string planCode, string billingInterval, string successUrl, string cancelUrl);
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

        var secretKey = _configuration["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException("Stripe no está configurado: falta Stripe:SecretKey.");

        var plan = await _context.subscription_plans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.code == planCode && p.active);

        if (plan == null)
            throw new InvalidOperationException("El plan solicitado no existe o no está activo.");

        var priceId = billingInterval == "monthly"
            ? plan.stripe_monthly_price_id
            : plan.stripe_yearly_price_id;

        if (string.IsNullOrWhiteSpace(priceId))
            throw new InvalidOperationException($"El plan {plan.code} todavía no tiene configurado su Price ID de Stripe para {billingInterval}.");

        var tenant = await _context.tenants.AsNoTracking().FirstOrDefaultAsync(t => t.id == tenantId);
        if (tenant == null)
            throw new InvalidOperationException("La organización no existe.");

        var existingSubscription = await _context.subscriptions
            .FirstOrDefaultAsync(s => s.tenant_id == tenantId && s.status != "cancelled" && s.status != "canceled");

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

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.stripe.com/v1/checkout/sessions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        request.Content = new FormUrlEncodedContent(form);

        using var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Stripe rechazó la creación del checkout ({(int)response.StatusCode}): {body}");

        using var json = JsonDocument.Parse(body);
        var url = json.RootElement.GetProperty("url").GetString();

        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Stripe no devolvió una URL de Checkout válida.");

        return url;
    }
}
