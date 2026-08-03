using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
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
            ClinicLogo = user.clinic_logo
        };

        return Ok(dto);
    }

    // PUT: api/profile
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

        await _context.SaveChangesAsync();
        return NoContent();
    }
}
