using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Soporte interno entre profesionales/clínicas y SuperAdmin.
/// El ticket es la unidad de autorización y de conversación; no se mezcla con el chat paciente-profesional.
/// </summary>
[ApiController]
[Route("api/support")]
[Authorize(Roles = "nutritionist,clinic_admin,superadmin")]
public sealed class SupportController : ControllerBase
{
    private readonly SupportService _support;

    public SupportController(SupportService support)
    {
        _support = support;
    }

    [HttpGet("tickets")]
    public async Task<ActionResult<IReadOnlyList<SupportTicketSummaryDto>>> GetTickets(
        [FromQuery] string? status = null,
        [FromQuery] string? category = null,
        [FromQuery] string? priority = null)
    {
        if (!TryIdentity(out var userId, out var tenantId)) return Unauthorized();
        var isSuperAdmin = User.IsInRole("superadmin");
        return Ok(await _support.GetTicketsAsync(userId, tenantId, isSuperAdmin, status, category, priority));
    }

    [HttpGet("tickets/{ticketId:long}")]
    public async Task<ActionResult<SupportTicketDto>> GetTicket(long ticketId)
    {
        if (!TryIdentity(out var userId, out var tenantId)) return Unauthorized();
        var ticket = await _support.GetTicketAsync(ticketId, userId, tenantId, User.IsInRole("superadmin"));
        return ticket == null ? NotFound() : Ok(ticket);
    }

    [HttpPost("tickets")]
    public async Task<ActionResult> CreateTicket([FromBody] CreateSupportTicketRequest request)
    {
        if (!TryIdentity(out var userId, out var tenantId)) return Unauthorized();
        if (User.IsInRole("superadmin")) return Forbid();

        try
        {
            var id = await _support.CreateTicketAsync(userId, tenantId, request);
            return CreatedAtAction(nameof(GetTicket), new { ticketId = id }, new { id });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("tickets/{ticketId:long}/messages")]
    public async Task<ActionResult> AddMessage(long ticketId, [FromBody] AddSupportMessageRequest request)
    {
        if (!TryIdentity(out var userId, out var tenantId)) return Unauthorized();

        try
        {
            var updated = await _support.AddMessageAsync(
                ticketId, userId, tenantId, User.IsInRole("superadmin"), request.Body, request.Internal);
            return updated ? NoContent() : NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPatch("tickets/{ticketId:long}")]
    [Authorize(Roles = "superadmin")]
    public async Task<ActionResult> UpdateTicket(long ticketId, [FromBody] UpdateSupportTicketRequest request)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (!userId.HasValue) return Unauthorized();

        try
        {
            var updated = await _support.UpdateTicketAsync(ticketId, userId.Value, request.Status, request.Priority, request.AssignedToUserId);
            return updated ? NoContent() : NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private bool TryIdentity(out int userId, out int tenantId)
    {
        var id = AuthHelpers.GetUserId(User);
        var tenant = AuthHelpers.GetTenantId(User);
        if (!id.HasValue || (!tenant.HasValue && !User.IsInRole("superadmin")))
        {
            userId = 0;
            tenantId = 0;
            return false;
        }

        userId = id.Value;
        tenantId = tenant ?? 0;
        return true;
    }
}
