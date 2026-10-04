using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/directory")]
public class DirectoryController : ControllerBase
{
    private readonly angulosodbContext _context;

    public DirectoryController(angulosodbContext context) => _context = context;

    // Solo los profesionales que activan expresamente la visibilidad salen al directorio público.
    [AllowAnonymous]
    [HttpGet("professionals")]
    public async Task<ActionResult<IEnumerable<DirectoryProfileDto>>> Search([FromQuery] DirectorySearchDto filter)
    {
        var query = _context.users.AsNoTracking()
            .Where(u => u.archived_at == null && u.role != "admin" && u.role != "superadmin" && u.directory_enabled == true);

        if (!string.IsNullOrWhiteSpace(filter.City))
            query = query.Where(u => u.directory_city != null && EF.Functions.ILike(u.directory_city, $"%{filter.City.Trim()}%"));
        if (filter.Online == true)
            query = query.Where(u => u.online_consultations == true);
        if (!string.IsNullOrWhiteSpace(filter.Speciality))
            query = query.Where(u => u.directory_specialties != null && EF.Functions.ILike(u.directory_specialties, $"%{filter.Speciality.Trim()}%"));

        return Ok(await query.OrderBy(u => u.directory_city).ThenBy(u => u.full_name).Take(100)
            .Select(u => new DirectoryProfileDto
            {
                Username = u.username,
                Slug = u.directory_slug ?? string.Empty,
                FullName = u.full_name ?? string.Empty,
                ClinicName = u.clinic_name ?? string.Empty,
                City = u.directory_city ?? string.Empty,
                ClinicLogo = u.clinic_logo ?? string.Empty,
                PublicBio = u.directory_bio ?? string.Empty,
                Specialties = u.directory_specialties ?? string.Empty,
                OnlineConsultations = u.online_consultations ?? false
            }).ToListAsync());
    }

    [AllowAnonymous]
    [HttpGet("professionals/{slug}")]
    public async Task<ActionResult<DirectoryProfileDto>> GetBySlug(string slug)
    {
        var normalized = slug.Trim().ToLowerInvariant();
        var profile = await _context.users.AsNoTracking()
            .Where(u => u.archived_at == null && u.directory_enabled == true && u.directory_slug == normalized)
            .Select(u => new DirectoryProfileDto
            {
                Username = u.username,
                Slug = u.directory_slug ?? string.Empty,
                FullName = u.full_name ?? string.Empty,
                ClinicName = u.clinic_name ?? string.Empty,
                // La ficha pública no expone la dirección exacta de la consulta.
                City = u.directory_city ?? string.Empty,
                ClinicLogo = u.clinic_logo ?? string.Empty,
                PublicBio = u.directory_bio ?? string.Empty,
                Specialties = u.directory_specialties ?? string.Empty,
                OnlineConsultations = u.online_consultations ?? false
            }).FirstOrDefaultAsync();

        return profile == null ? NotFound() : Ok(profile);
    }
}
