using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anguloso.Server.Controllers;

[ApiController]
[Authorize(Policy = "Professional")]
[Route("api/license")]
public class LicenseController : ControllerBase
{
    private readonly ILicenseService _licenseService;

    public LicenseController(ILicenseService licenseService)
    {
        _licenseService = licenseService;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        // Un SuperAdmin es una cuenta de plataforma, no una organización facturable.
        // No debe intentar resolver una licencia de tenant.
        if (User.IsInRole("superadmin"))
            return Forbid();

        var tenantId = AuthHelpers.GetTenantId(User);
        if (!tenantId.HasValue)
            return Unauthorized();

        var license = await _licenseService.GetLicenseAsync(tenantId);
        if (license == null)
            return NotFound();

        return Ok(new
        {
            license.TenantId,
            license.PlanCode,
            license.PlanName,
            license.Status,
            license.ExpiresAt,
            license.CurrentPeriodStart,
            license.CurrentPeriodEnd,
            license.BillingInterval,
            license.CancelAtPeriodEnd,
            license.Nutritionists,
            license.Clients,
            license.MaxNutritionists,
            license.MaxClientsPerNutritionist,
            license.MaxTotalClients,
            license.NutritionistReplacementAvailableAt,
            license.Features
        });
    }
}
