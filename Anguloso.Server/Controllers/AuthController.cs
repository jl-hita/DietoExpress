using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using Serilog;


//using Microsoft.IdentityModel.Tokens;
using System;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Runtime.ConstrainedExecution;


//using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace Anguloso.Server.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly IConfiguration _config;
    //private readonly IEmailService _emailService;
    private readonly EmailServ _emailServ;
    private readonly ConfigServ _configServ;

    //public AuthController(angulosodbContext context, IConfiguration config, IEmailService emailService, ConfigServ configServ)
    public AuthController(angulosodbContext context, IConfiguration config, EmailServ emailServ, ConfigServ configServ)
    {
        _context = context;
        _config = config;
        //_emailService = emailService;
        _emailServ = emailServ;
        _configServ = configServ;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest login)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(login.Username) || string.IsNullOrWhiteSpace(login.Password))
                return BadRequest("Usuario o contraseña no válidos.");

            // Buscar el usuario
            var user = await _context.users.FirstOrDefaultAsync(u => u.username == login.Username);

            if (user == null)
                return Unauthorized("Usuario no encontrado.");

            if (!BCrypt.Net.BCrypt.Verify(login.Password, user.password_hash))
                return Unauthorized("Contraseña incorrecta.");

            if (user.email_confirmed == null || user.email_confirmed == false)
                return Unauthorized("Debes confirmar tu email antes de iniciar sesión.");

            // Actualizar fecha de último login
            user.last_login = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            /*
             *Crear token JWT
             *Ahora lo hace su propio método - se comparte lógica con login normal y login google
             *
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"]!);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.id.ToString()),
                new Claim(ClaimTypes.Name, user.username),
                new Claim(ClaimTypes.Role, user.role ?? "user")
            };

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(3),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            var tokenNuevo = tokenHandler.CreateToken(tokenDescriptor);
            var tokenString = tokenHandler.WriteToken(tokenNuevo);
            */

            var tokenString = CrearJwtParaUsuario(user);

            return Ok(new
            {
                token = tokenString,
                username = user.username,
                role = user.role
            });
        }
        catch(Exception e) 
        {
            return BadRequest(e.Message);
        }
    }

    [HttpGet("crearAdmin")]
    public IActionResult CreateAdmin()
    {
        return BadRequest("Desactivado");
        /*
        string username = "admin";
        string fullName = "Administrador del sistema";
        string plainPassword = "1234"; // puedes cambiarlo luego
        string role = "admin";

        // Encriptamos la contraseña
        string passwordHash = BCrypt.Net.BCrypt.HashPassword(plainPassword);

        // Verificamos si ya existe
        if (_context.users.Any(u => u.username == username))
        {
            return BadRequest("El usuario 'admin' ya existe.");
        }

        // Creamos el nuevo usuario
        var user = new users
        {
            username = username,
            full_name = fullName,
            password_hash = passwordHash,
            role = role,
            created_at = DateTime.Now
        };

        _context.users.Add(user);
        _context.SaveChanges();

        return Ok($"Usuario '{username}' creado correctamente.");
        */
    }

    //Crea usuario y envía enlace de confirmación via email
    [HttpPut("crearUser")]
    //public async Task<IActionResult> CrearUser([FromBody] Usuario usuario)
    public async Task<BoolMensaje> CrearUserAsync([FromBody] Usuario usuario)
    {
        try
        {
            if (usuario == null || string.IsNullOrEmpty(usuario.Username) || string.IsNullOrEmpty(usuario.PasswordPlain) || string.IsNullOrEmpty(usuario.Email))
            {
                //return BadRequest("Usuario o contraseña no válidos.");
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = $"Usuario, contraseña o email no válidos"
                };
            }

            string nombreCompleto = string.IsNullOrEmpty(usuario.FullName) ? "" : usuario.FullName;
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(usuario.PasswordPlain);

            //Comprobamos si el usuario existe ya
            users? user = _context.users.FirstOrDefault(u => u.email == usuario.Username || u.username == usuario.Username);
            if (user != null)
            {
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = $"El usuario {usuario.Username} ya existe"
                };
            }

            //Comprobamos si el email existe
            user = _context.users.FirstOrDefault(u => u.email == usuario.Email);
            if (user != null)
            {
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = $"El email {usuario.Email} ya existe"
                };
            }

            // Generar token de confirmación
            string token = Guid.NewGuid().ToString();

            //Creamos el usuario
            user = new users
            {
                username = usuario.Username,
                full_name = nombreCompleto,
                password_hash = passwordHash,
                email = usuario.Email,
                role = "user",
                created_at = DateTime.UtcNow,
                email_confirmed = false,
                email_confirmation_token = token
            };

            //Guardamos el usuario en la base de datos
            _context.users.Add(user);
            _context.SaveChanges();

            //obtenemos el dominio de la url
            string dominio = _configServ.GetConfigString("dominio", "www.tusitio.com") ?? "www.tusitio.com";

            //Enviar email de confirmación
            //string urlConfirm = $"https://{dominio}/confirmar-email?token={token}";
            string urlConfirm = $"https://localhost:4200/confirmar-email?token={token}";
            BoolMensaje? bmEmail = await _emailServ.SendEmailAsync(
                usuario.Email,
                "Confirma tu email",
                $"<h2>Bienvenido, {usuario.Username}</h2><p>Haz clic en el siguiente enlace para confirmar tu email:</p><a href = '{urlConfirm}' > Confirmar email </a>"
            );

            if(bmEmail == null || bmEmail.Exito == false)
            {
                string mensaje = bmEmail == null ? "Fallo genérico" : bmEmail.Mensaje;
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = $"Resultado envío email => {mensaje}"
                };
            }

            //Intentamos hacer login después de crear el usuario
            //return await Login(new LoginRequest { Username = user.username, Password = user.password_hash });

            return new BoolMensaje
            {
                Exito = true,
                Mensaje = $"Usuario {usuario.Username} creado"
            };
        }
        catch (Exception ex)
        {
            //return BadRequest(ex.Message);
            return new BoolMensaje
            {
                Exito = false,
                Mensaje = $"Error creando usuario -> {ex.Message}"
            };
        }
    }

    //Confirma cuenta accediento a través de enlace en email de confirmación
    [HttpGet("confirmarEmail")]
    public async Task<IActionResult> ConfirmarEmail([FromQuery]string token)
    {
        var user = await _context.users.FirstOrDefaultAsync(u => u.email_confirmation_token == token);

        if (user == null)
            return BadRequest("Token inválido");

        user.email_confirmed = true;
        user.email_confirmation_token = null;
        //await _context.SaveChangesAsync();
        //return Ok("Email confirmado correctamente");

        //A partir de aquí logea al usuario y devuelve el token
        // Actualizar fecha de último login
        user.last_login = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        /*
         * Crear token JWT
         * Ahora lo hace su propio método - se comparte lógica con login normal y login google
         *
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"]!);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.id.ToString()),
            new Claim(ClaimTypes.Name, user.username),
            new Claim(ClaimTypes.Role, user.role ?? "user")
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(3),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenNuevo = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(tokenNuevo);
        */

        var tokenString = CrearJwtParaUsuario(user);

        return Ok(new
        {
            token = tokenString,
            username = user.username,
            role = user.role
        });
    }

    [HttpPost("enviarReset")]
    public async Task<BoolMensaje> EnviarReset([FromBody] PasswordResetEmailRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email))
            return new BoolMensaje { Exito = false, Mensaje = "Email obligatorio" };

        var user = await _context.users.FirstOrDefaultAsync(u => u.email == req.Email);

        if (user == null)
            return new BoolMensaje { Exito = false, Mensaje = "No existe un usuario con ese email" };

        //if (req.Username != user.username)
        //    return new BoolMensaje { Exito = false, Mensaje = "Usuario incorrecto" };

        // Generar token
        string token = Guid.NewGuid().ToString();

        user.reset_password_token = token;
        user.reset_token_expiration = DateTime.UtcNow.AddMinutes(30);
        await _context.SaveChangesAsync();

        string dominio = _configServ.GetConfigString("dominio", "www.tusitio.com") ?? "www.tusitio.com";
        string url = $"https://{dominio}/reset-password?token={token}";

        var bm = await _emailServ.SendEmailAsync(
            user.email,
            "Recuperar contraseña",
            $@"<p>Hola {user.username},</p>
           <p>Puedes restablecer tu contraseña desde el siguiente enlace:</p>
           <a href='{url}'>Restablecer contraseña</a>
           <p>Este enlace caduca en 30 minutos.</p>"
        );

        //return new BoolMensaje { Exito = true, Mensaje = "Email enviado con instrucciones" };

        string resultado = bm.Exito
            ? "Email enviado con instrucciones"
            : bm.Mensaje;
        return new BoolMensaje { Exito = bm.Exito, Mensaje = resultado };
    }

    [HttpPut("resetPassword")]
    public async Task<BoolMensaje> ResetPassword([FromBody] PasswordResetByTokenRequest req)
    {
        var user = await _context.users
            .FirstOrDefaultAsync(u => u.reset_password_token == req.Token);

        if (user == null)
            return new BoolMensaje { Exito = false, Mensaje = "Token inválido" };

        if (user.reset_token_expiration < DateTime.UtcNow)
            return new BoolMensaje { Exito = false, Mensaje = "El token ha expirado" };

        user.password_hash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        user.reset_password_token = null;
        user.reset_token_expiration = null;

        await _context.SaveChangesAsync();

        return new BoolMensaje { Exito = true, Mensaje = "Contraseña cambiada correctamente" };
    }

    //Para usar en un componente de settings de usuario
    [HttpPut("cambiarPassword")]
    public BoolMensaje CambiarPassword([FromBody] PasswordResetRequest passwordResetRequest)
    {
        try
        {
            if(passwordResetRequest.NewPassword != passwordResetRequest.NewPasswordRep)
            {
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = "Las nuevas contraseñas no coinciden"
                };
            }

            // Buscar el usuario
            users? user = _context.users.FirstOrDefault(u => u.username == passwordResetRequest.Username);

            if(user == null)
            {
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = $"Usuario {passwordResetRequest.Username} no encontrado"
                };
            }

            //Comprobamos el password antiguo
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(passwordResetRequest.OldPassword);
            if (user.password_hash != passwordHash)
            {
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = "Contraseña incorrecta."
                };
            }
                
            //Cambiamos la contraseña
            user.password_hash = passwordHash;
            _context.SaveChanges();

            return new BoolMensaje
            {
                Exito = true,
                Mensaje = $"Password del usuario {passwordResetRequest.Username} cambiado correctamente"
            };
        }
        catch (Exception ex)
        {
            //return BadRequest(ex.Message);
            return new BoolMensaje
            {
                Exito = false,
                Mensaje = $"Error cambiando password -> {ex.Message}"
            };
        }
    }

    //Login con google
    [HttpPost("google")]
    public async Task<IActionResult> LoginGoogle([FromBody] GoogleLoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.IdToken))
            return BadRequest("IdToken requerido");

        GoogleJsonWebSignature.Payload payload;
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings()
            {
                // Comprueba que el token fue emitido para nuestro client id
                Audience = new[] { _config["Authentication:Google:ClientId"] } // añade esto en appsettings
            };

            payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken, settings);
        }
        catch (Exception ex)
        {
            // token inválido o expirado
            return Unauthorized($"Token inválido: {ex.Message}");
        }

        // payload contiene: Email, EmailVerified, Name, GivenName, FamilyName, Picture, Subject (sub = google id)
        var googleId = payload.Subject;
        var email = payload.Email;
        var name = payload.Name ?? payload.Email;

        // Buscar por google_id primero
        var user = await _context.users.FirstOrDefaultAsync(u => u.google_id == googleId);

        if (user == null)
        {
            // Si no existe, buscar por email (posible usuario local ya creado)
            user = await _context.users.FirstOrDefaultAsync(u => u.email == email);

            if (user != null)
            {
                // Opción A: enlazar cuentas (recomendado) -> guardamos google_id y provider
                user.google_id = googleId;
                user.provider = "google";
                user.email_confirmed = true; // Google garantiza el email verificado, pero comprueba payload.EmailVerified si quieres.
                user.last_login = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            else
            {
                // Crear nuevo usuario
                user = new users
                {
                    username = GenerateUniqueUsername(name), // función auxiliar que te propongo abajo
                    full_name = name,
                    email = email,
                    google_id = googleId,
                    provider = "google",
                    role = "user",
                    email_confirmed = true,
                    created_at = DateTime.UtcNow,
                    last_login = DateTime.UtcNow
                };

                _context.users.Add(user);
                await _context.SaveChangesAsync();
            }
        }
        else
        {
            // usuario encontrado por google_id -> actualizar last_login
            user.last_login = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        // Generar tu JWT (reutiliza el código que ya tienes en Login)
        var jwt = CrearJwtParaUsuario(user);

        return Ok(new
        {
            token = jwt,
            username = user.username,
            role = user.role
        });
    }

    //Subrutina que se usa en los distintos modos de login. Genera un token con el user
    private string CrearJwtParaUsuario(users user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"]!);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.id.ToString()),
            new Claim(ClaimTypes.Name, user.username),
            new Claim(ClaimTypes.Role, user.role ?? "user")
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(3),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    //Crea un nombre de usuario único
    //puede transformar José López en jose.lopez y si ya existe añadir un sufijo numérico
    private string GenerateUniqueUsername(string name)
    {
        // Normalizar: quitar espacios, acentos, etc. (aquí simple)
        var baseName = name.ToLower().Replace(" ", ".").Normalize(NormalizationForm.FormD);
        baseName = Regex.Replace(baseName, @"\p{Mn}", ""); // quitar diacríticos
        baseName = Regex.Replace(baseName, @"[^a-z0-9.]", "");

        var candidate = baseName;
        int suffix = 0;
        while (_context.users.Any(u => u.username == candidate))
        {
            suffix++;
            candidate = $"{baseName}{suffix}";
        }
        return candidate;
    }

    /*
     * TODO Envía email con enlace para cambiar el password
    [HttpPut("resetPassword")]
    public async Task<BoolMensaje> ResetPasswordAsync([FromBody] PasswordResetEmailRequest passwordResetEmailRequest)
    {
        try
        {
            if (string.IsNullOrEmpty(passwordResetEmailRequest.Username) || string.IsNullOrEmpty(passwordResetEmailRequest.Email))
            {
                //return BadRequest("Usuario o contraseña no válidos.");
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = $"Usuario y email no pueden estar en blanco"
                };
            }

            // Buscar el usuario
            users? user = _context.users.FirstOrDefault(u => u.username == passwordResetEmailRequest.Username && u.email == passwordResetEmailRequest.Email);

            if (user == null)
            {
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = $"Usuario {passwordResetEmailRequest.Username} con email {passwordResetEmailRequest.Email} no encontrado"
                };
            }

            //Creamos un password nuevo
            int longitud = 10;
            const string caracteres = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
            var bytes = new byte[longitud];
            var resultado = new char[longitud];

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            for (int i = 0; i < longitud; i++)
            {
                resultado[i] = caracteres[bytes[i] % caracteres.Length];
            }
            string passwordPlain = new string(resultado);
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(passwordPlain);
            

            //Cambiamos la contraseña
            user.password_hash = passwordHash;
            _context.SaveChanges();

            //Enviamos un email con la nueva contraseña
            string html = $"<h1>Anguloso</h1><p>Has solicitado un reset de contraseña. Tu nueva contraseña es {passwordPlain}</p><p>No respondas a este correo.</p>";
            BoolMensaje bmEmail = await _emailService.SendEmailAsync(passwordResetEmailRequest.Email, "Nuevo password Anguloso", html);

            return bmEmail;
        }
        catch (Exception ex)
        {
            //return BadRequest(ex.Message);
            return new BoolMensaje
            {
                Exito = false,
                Mensaje = $"Error solititando cambio de password -> {ex.Message}"
            };
        }
    }
    */

    [HttpGet("whoami")]
    public IActionResult WhoAmI()
    {
        if (!User.Identity?.IsAuthenticated ?? true)
            return Unauthorized("No autenticado");

        var claims = User.Claims.Select(c => new { c.Type, c.Value });
        return Ok(claims);
    }
}

public class PasswordResetByTokenRequest
{
    public string? Token { get; set; }
    public string? NewPassword { get; set; }
}

//DTO para la petición desde fronten
public class GoogleLoginDto
{
    public string IdToken { get; set; } = string.Empty;
}