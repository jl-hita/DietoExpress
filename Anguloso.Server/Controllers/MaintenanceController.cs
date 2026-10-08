using Anguloso.Server.Logica;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/maintenance")]
public sealed class MaintenanceController : ControllerBase
{
    private readonly MaintenanceNoticeService _maintenance;

    public MaintenanceController(MaintenanceNoticeService maintenance) => _maintenance = maintenance;

    // Este endpoint es anónimo porque el aviso debe poder consultarse desde cualquier navegador
    // y no debe depender de que exista una sesión válida durante una incidencia.
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Get() => Ok(_maintenance.Get() ?? new MaintenanceNotice(false, "", "", DateTimeOffset.UtcNow));
}
