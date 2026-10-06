using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/admin/directory-verification")]
[Authorize(Roles = "superadmin")]
public class AdminDirectoryVerificationController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly IAuditLogService _audit;

    public AdminDirectoryVerificationController(angulosodbContext context, IAuditLogService audit)
    {
        _context = context;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DirectoryVerificationDto>>> List([FromQuery] string? status = null)
    {
        var query = _context.users.AsNoTracking()
            .Where(u => u.role == "nutritionist" && u.archived_at == null && u.directory_enabled == true);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim().ToLowerInvariant();
            query = query.Where(u => u.directory_publication_status == normalized);
        }

        return Ok(await query
            .OrderBy(u => u.directory_publication_status)
            .ThenBy(u => u.full_name)
            .Select(u => new DirectoryVerificationDto
            {
                Id = u.id,
                Username = u.username,
                FullName = u.full_name,
                Email = u.email,
                ClinicName = u.clinic_name,
                City = u.directory_city,
                Province = u.directory_province,
                Specialties = u.directory_specialties,
                Slug = u.directory_slug,
                PublicationStatus = u.directory_publication_status ?? "draft",
                VerifiedAt = u.directory_verified_at,
                VerifiedByUserId = u.directory_verified_by_user_id,
                VerificationNote = u.directory_verification_note,
                DirectoryEnabled = u.directory_enabled ?? false,
                OnlineConsultations = u.online_consultations ?? false
            })
            .ToListAsync());
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<DirectoryVerificationDto>> Update(
        int id,
        [FromBody] UpdateDirectoryVerificationRequest request)
    {
        var status = request.Status?.Trim().ToLowerInvariant();
        if (status is not ("pending" or "verified" or "published" or "rejected" or "draft"))
            return BadRequest("Estado de publicación no válido.");

        var note = request.Note?.Trim();
        if (note?.Length > 2000)
            return BadRequest("La nota de verificación no puede superar los 2000 caracteres.");

        var user = await _context.users.FirstOrDefaultAsync(u =>
            u.id == id && u.role == "nutritionist" && u.archived_at == null);
        if (user == null)
            return NotFound();

        if (status == "published" && user.directory_enabled != true)
            return BadRequest("No se puede publicar una ficha que el profesional ha desactivado.");

        if (status is "verified" or "published")
        {
            user.directory_verified_at ??= DateTime.UtcNow;
            user.directory_verified_by_user_id = AuthHelpers.GetUserId(User);
        }
        else if (status is "draft" or "rejected")
        {
            user.directory_verified_at = null;
            user.directory_verified_by_user_id = null;
        }

        user.directory_publication_status = status;
        user.directory_verification_note = note;

        await _context.SaveChangesAsync();

        await _audit.LogAccessAsync(
            "PUBLIC_DIRECTORY_VERIFICATION_UPDATED",
            "user",
            user.id.ToString(),
            details: $"status={status}; username={user.username}");

        return Ok(new DirectoryVerificationDto
        {
            Id = user.id,
            Username = user.username,
            FullName = user.full_name,
            Email = user.email,
            ClinicName = user.clinic_name,
            City = user.directory_city,
            Province = user.directory_province,
            Specialties = user.directory_specialties,
            Slug = user.directory_slug,
            PublicationStatus = user.directory_publication_status ?? "draft",
            VerifiedAt = user.directory_verified_at,
            VerifiedByUserId = user.directory_verified_by_user_id,
            VerificationNote = user.directory_verification_note,
            DirectoryEnabled = user.directory_enabled ?? false,
            OnlineConsultations = user.online_consultations ?? false
        });
    }
}

public class DirectoryVerificationDto
{
    public int Id { get; set; }
    public string? Username { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? ClinicName { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
    public string? Specialties { get; set; }
    public string? Slug { get; set; }
    public string PublicationStatus { get; set; } = "draft";
    public DateTime? VerifiedAt { get; set; }
    public int? VerifiedByUserId { get; set; }
    public string? VerificationNote { get; set; }
    public bool DirectoryEnabled { get; set; }
    public bool OnlineConsultations { get; set; }
}

public class UpdateDirectoryVerificationRequest
{
    public string? Status { get; set; }
    public string? Note { get; set; }
}