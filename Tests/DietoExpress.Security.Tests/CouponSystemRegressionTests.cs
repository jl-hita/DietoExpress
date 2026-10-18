using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class CouponSystemRegressionTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    [Fact]
    public void CouponSchema_HasUniqueCodesAndSingleActiveRedemptionConstraint()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "BillingSchemaBootstrap.cs"));
        Assert.Contains("CREATE TABLE IF NOT EXISTS billing_coupon_campaigns", source);
        Assert.Contains("CREATE TABLE IF NOT EXISTS billing_coupons", source);
        Assert.Contains("CREATE TABLE IF NOT EXISTS billing_coupon_redemptions", source);
        Assert.Contains("uq_billing_coupons_code_normalized", source);
        Assert.Contains("uq_billing_coupon_active_redemption", source);
        Assert.Contains("status IN ('reserved','redeemed')", source);
    }

    [Fact]
    public void Checkout_CreatesStripeCouponWithOnceDurationAndTracksRedemptionInWebhook()
    {
        var stripe = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "StripeBillingService.cs"));
        Assert.Contains("[\"duration\"] = \"once\"", stripe);
        Assert.Contains("[\"discounts[0][coupon]\"] = stripeCouponId", stripe);
        Assert.Contains("coupon_redemption_id", stripe);

        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "BillingController.cs"));
        Assert.Contains("status = 'redeemed'", controller);
        Assert.Contains("case \"checkout.session.expired\"", controller);
        Assert.Contains("paymentStatus is \"paid\" or \"no_payment_required\"", controller);
    }

    [Fact]
    public void CouponAdministration_IsRestrictedToSuperAdminAndValidationToProfessionalPolicy()
    {
        var admin = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "AdminCouponsController.cs"));
        Assert.Contains("[Authorize(Roles = \"superadmin\")]", admin);
        Assert.Contains("[HttpPost(\"campaigns\")]", admin);
        Assert.Contains("RandomNumberGenerator.GetBytes(8)", admin);

        var validation = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "CouponValidationController.cs"));
        Assert.Contains("[Authorize(Policy = \"Professional\")]", validation);
        Assert.Contains("discountAmount = discount", validation);
        Assert.Contains("appliesToFirstInvoiceOnly = true", validation);
    }
}
