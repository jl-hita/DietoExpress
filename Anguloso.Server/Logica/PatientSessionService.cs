using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Anguloso.Server.Models;
using Microsoft.IdentityModel.Tokens;

namespace Anguloso.Server.Logica;

/// <summary>
/// Emite la sesión del portal del paciente (JWT en cookie HttpOnly). Es el único punto que crea esa sesión, de modo que
/// todos los métodos de acceso (enlace de email, PIN y biometría del dispositivo) producen una sesión idéntica.
/// </summary>
public sealed class PatientSessionService
{
    public const string CookieName = "dietoexpress_patient_session";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    private readonly IConfiguration _config;

    public PatientSessionService(IConfiguration config) => _config = config;

    /// <summary>Crea el JWT y lo deja en la cookie de la respuesta. La versión de token permite revocar la sesión después.</summary>
    public void Issue(HttpResponse response, clients client)
    {
        var expiresAtUtc = DateTime.UtcNow.Add(SessionLifetime);
        SetCookie(response, GenerateJwt(client, expiresAtUtc), expiresAtUtc);
    }

    private string GenerateJwt(clients client, DateTime expiresAtUtc)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"]!);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, client.id.ToString()),
            new("clientId", client.id.ToString()),
            new(ClaimTypes.Name, client.full_name ?? "Paciente"),
            new(ClaimTypes.Role, "patient"),
            new("portalTokenVersion", client.portal_token_version.ToString())
        };
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = _config["Jwt:Issuer"],
            Audience = _config["Jwt:Audience"],
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAtUtc,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        return tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));
    }

    private static void SetCookie(HttpResponse response, string jwt, DateTime expiresAtUtc)
    {
        response.Cookies.Append(CookieName, jwt, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = new DateTimeOffset(expiresAtUtc),
            MaxAge = expiresAtUtc - DateTime.UtcNow,
            Path = "/"
        });
    }
}
