using Swashbuckle.AspNetCore.Annotations;

namespace Anguloso.Server.Model;

public class LoginRequest
{
    [SwaggerSchema("Nombre de usuario")]
    public string Username { get; set; } = null!;

    [SwaggerSchema("Contraseña del usuario")]
    public string Password { get; set; } = null!;
}

public class PasswordResetRequest
{
    public string? Username { get; set; }
    public string? OldPassword { get; set; }
    public string? NewPassword { get; set; }
    public string? NewPasswordRep { get; set; }
}

public class PasswordResetEmailRequest
{
    public string? Username { get; set; }
    public string? Email { get; set; }
}
