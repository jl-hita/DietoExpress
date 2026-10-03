using Anguloso.Server.Logica;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/privacy")]
[Authorize(Policy = "Professional")]
public sealed class PrivacyOperationsController : ControllerBase
{
    private readonly PrivacyOperationsService _service;
    public PrivacyOperationsController(PrivacyOperationsService service) => _service = service;

    [HttpGet("requests")]
    public async Task<ActionResult<IReadOnlyList<PrivacyRequestDto>>> Requests(CancellationToken ct) => Ok(await _service.ListRequestsAsync(ct));

    [HttpPost("requests")]
    public async Task<IActionResult> CreateRequest([FromBody] CreatePrivacyRequest request, CancellationToken ct)
    {
        try { return Ok(new { id = await _service.CreateRequestAsync(request, ct) }); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
    }

    [HttpPatch("requests/{id:long}")]
    public async Task<IActionResult> UpdateRequest(long id, [FromBody] UpdatePrivacyRequest request, CancellationToken ct)
        => await _service.UpdateRequestAsync(id, request, ct) ? NoContent() : NotFound();

    [HttpGet("incidents")]
    public async Task<ActionResult<IReadOnlyList<PrivacyIncidentDto>>> Incidents(CancellationToken ct) => Ok(await _service.ListIncidentsAsync(ct));

    [HttpPost("incidents")]
    public async Task<IActionResult> CreateIncident([FromBody] CreatePrivacyIncident request, CancellationToken ct)
    {
        try { return Ok(new { id = await _service.CreateIncidentAsync(request, ct) }); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpPatch("incidents/{id:long}")]
    public async Task<IActionResult> UpdateIncident(long id, [FromBody] UpdatePrivacyIncident request, CancellationToken ct)
        => await _service.UpdateIncidentAsync(id, request, ct) ? NoContent() : NotFound();
}
