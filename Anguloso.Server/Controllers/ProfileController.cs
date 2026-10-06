using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Policy = "Professional")]
public class ProfileController : ControllerBase
{
    private readonly angulosodbContext _context;

    public ProfileController(angulosodbContext context)
    {
        _context = context;
    }

    // GET: api/profile
    [HttpGet]
    public async Task<ActionResult<ProfileDto>> GetProfile()
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var user = await _context.users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.id == userId.Value);

        if (user == null) return NotFound("Usuario no encontrado.");

        var dto = new ProfileDto
        {
            Username = user.username,
            Email = user.email,
            FullName = user.full_name,
            ClinicName = user.clinic_name,
            ClinicAddress = user.clinic_address,
            ClinicPhone = user.clinic_phone,
            ClinicLogo = user.clinic_logo,
            DirectoryEnabled = user.directory_enabled ?? false,
            OnlineConsultations = user.online_consultations ?? false,
            DirectoryCity = user.directory_city ?? string.Empty,
            DirectoryProvince = user.directory_province ?? string.Empty,
            DirectoryBio = user.directory_bio ?? string.Empty,
            DirectorySpecialties = user.directory_specialties ?? string.Empty,
            DirectorySlug = user.directory_slug ?? string.Empty
        };

        return Ok(dto);
    }

    // PUT: api/profile
    // Solo se modifican los datos de perfil permitidos; identidad, rol y credenciales no forman parte de este endpoint.
    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var user = await _context.users.FirstOrDefaultAsync(u => u.id == userId.Value);
        if (user == null) return NotFound("Usuario no encontrado.");

        user.full_name = dto.FullName ?? user.full_name;
        user.clinic_name = dto.ClinicName;
        user.clinic_address = dto.ClinicAddress;
        user.clinic_phone = dto.ClinicPhone;
        user.clinic_logo = dto.ClinicLogo;
        user.directory_enabled = dto.DirectoryEnabled;
        user.online_consultations = dto.OnlineConsultations;
        user.directory_city = string.IsNullOrWhiteSpace(dto.DirectoryCity) ? null : dto.DirectoryCity.Trim();
        user.directory_province = string.IsNullOrWhiteSpace(dto.DirectoryProvince) ? null : dto.DirectoryProvince.Trim();
        user.directory_bio = string.IsNullOrWhiteSpace(dto.DirectoryBio) ? null : dto.DirectoryBio.Trim();
        user.directory_specialties = string.IsNullOrWhiteSpace(dto.DirectorySpecialties) ? null : dto.DirectorySpecialties.Trim();

        // El slug solo se necesita cuando el profesional publica su ficha; una vez creado permanece estable.
        if (dto.DirectoryEnabled && string.IsNullOrWhiteSpace(user.directory_slug))
        {
            var baseSlug = BuildSlug(user.full_name, user.username);
            user.directory_slug = baseSlug;
            var suffix = 2;
            while (await _context.users.AnyAsync(u => u.id != user.id && u.directory_slug == user.directory_slug))
                user.directory_slug = $"{baseSlug}-{suffix++}";
        }

        await _context.SaveChangesAsync();
        return Ok(new ProfileDto
        {
            Username = user.username, Email = user.email, FullName = user.full_name,
            ClinicName = user.clinic_name, ClinicAddress = user.clinic_address, ClinicPhone = user.clinic_phone,
            ClinicLogo = user.clinic_logo, DirectoryEnabled = user.directory_enabled ?? false,
            OnlineConsultations = user.online_consultations ?? false, DirectoryCity = user.directory_city ?? string.Empty,
            DirectoryBio = user.directory_bio ?? string.Empty, DirectorySpecialties = user.directory_specialties ?? string.Empty,
            DirectorySlug = user.directory_slug ?? string.Empty
        });
    }
    private static string BuildSlug(string? fullName, string username)
    {
        var source = string.IsNullOrWhiteSpace(fullName) ? username : fullName;
        var normalized = source.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars).Replace("--", "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? $"profesional-{username.ToLowerInvariant()}" : slug;
    }

}
