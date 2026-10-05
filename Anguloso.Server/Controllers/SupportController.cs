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
    private readonly SupportEnhancementService _enhancements;

    public SupportController(SupportService support, SupportEnhancementService enhancements){_support=support;_enhancements=enhancements;}

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
            await _enhancements.NotifyAsync(id,userId,false);
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
            if(updated) await _enhancements.NotifyAsync(ticketId,userId,request.Internal);
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

    [HttpGet("notifications")]
    public async Task<ActionResult> Notifications(){var id=AuthHelpers.GetUserId(User);return id.HasValue?Ok(await _enhancements.ListAsync(id.Value)):Unauthorized();}
    [HttpGet("notifications/unread-count")]
    public async Task<ActionResult<int>> UnreadCount(){var id=AuthHelpers.GetUserId(User);return id.HasValue?Ok(await _enhancements.UnreadAsync(id.Value)):Unauthorized();}
    [HttpPost("notifications/{id:long}/read")]
    public async Task<ActionResult> ReadNotification(long id){var u=AuthHelpers.GetUserId(User);if(!u.HasValue)return Unauthorized();await _enhancements.ReadAsync(u.Value,id);return NoContent();}
    [HttpPost("tickets/{ticketId:long}/reopen")]
    public async Task<ActionResult> Reopen(long ticketId){if(!TryIdentity(out var u,out var t)||User.IsInRole("superadmin"))return BadRequest();try{await _enhancements.ReopenAsync(ticketId,u,t);return NoContent();}catch(KeyNotFoundException){return NotFound();}}
    [HttpGet("tickets/{ticketId:long}/audit")]
    [Authorize(Roles="superadmin")]
    public async Task<ActionResult> Audit(long ticketId)=>Ok(await _enhancements.GetAuditAsync(ticketId));
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
