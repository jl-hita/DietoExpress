using Anguloso.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[Route("api/[controller]")]
[ApiController]
public class SetupController : ControllerBase
{
    private readonly angulosodbContext _context;

    public SetupController(angulosodbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Devuelve si la aplicación ya ha sido configurada (existe al menos un superadmin).
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var isConfigured = await _context.users.AnyAsync(u => u.role == "superadmin");
        return Ok(new { isConfigured });
    }

    /// <summary>
    /// Crea el primer superadmin. Solo funciona si no existe ninguno aún.
    /// </summary>
    [HttpPost("init")]
    public async Task<IActionResult> Init([FromBody] SetupInitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest("Todos los campos son obligatorios.");

        if (request.Password.Length < 12)
            return BadRequest("La contraseña debe tener al menos 12 caracteres.");

        // Serializar las inicializaciones mediante un bloqueo transaccional de PostgreSQL.
        // Así dos peticiones simultáneas no pueden pasar ambas la comprobación de
        // "no existe superadmin" y crear dos cuentas de administración.
        await using var transaction = await _context.Database.BeginTransactionAsync();
        await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(748392615)");

        // Volver a comprobar dentro de la transacción, después de adquirir el bloqueo.
        var alreadyConfigured = await _context.users.AnyAsync(u => u.role == "superadmin");
        if (alreadyConfigured)
            return StatusCode(403, "La aplicación ya ha sido configurada.");

        // Verificar que el username o email no estén en uso
        var exists = await _context.users.AnyAsync(u =>
            u.username == request.Username || u.email == request.Email);
        if (exists)
            return Conflict("El nombre de usuario o email ya está en uso.");

        var superAdmin = new users
        {
            username = request.Username,
            full_name = request.FullName,
            email = request.Email,
            password_hash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            role = "superadmin",
            email_confirmed = true,
            subscription_plan = "enterprise",
            subscription_status = "active",
            created_at = DateTime.UtcNow
        };

        _context.users.Add(superAdmin);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(new { message = "SuperAdmin creado correctamente. Ya puedes iniciar sesión." });
    }
}

public class SetupInitRequest
{
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
