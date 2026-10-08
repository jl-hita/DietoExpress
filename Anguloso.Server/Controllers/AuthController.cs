using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
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

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace Anguloso.Server.Controllers;

[Route("api/[controller]")]
[ApiController]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly IConfiguration _config;
    //private readonly IEmailService _emailService;
    private readonly EmailServ _emailServ;
    private readonly ConfigServ _configServ;
    private readonly LogServ _logServ;
    private readonly IAuditLogService _audit;
    private readonly PatientDocumentTemplateSeeder _patientDocumentTemplateSeeder;

    //public AuthController(angulosodbContext context, IConfiguration config, IEmailService emailService, ConfigServ configServ)
    public AuthController(angulosodbContext context, IConfiguration config, EmailServ emailServ, ConfigServ configServ, LogServ logServ, IAuditLogService audit, PatientDocumentTemplateSeeder patientDocumentTemplateSeeder)
    {
        _context = context;
        _config = config;
        //_emailService = emailService;
        _emailServ = emailServ;
        _configServ = configServ;
        _logServ = logServ;
        _audit = audit;
        _patientDocumentTemplateSeeder = patientDocumentTemplateSeeder;
    }

    /// <summary>
    /// Inicia sesión con usuario y contraseña.
    /// </summary>
    /// <remarks>
    /// Ejemplo:
    ///
    ///     POST /api/auth/login
    ///     {
    ///         "username":"admin",
    ///         "password":"1234"
    ///     }
    ///
    /// </remarks>
    /// <response code="200">Login correcto.</response>
    /// <response code="400">Datos incorrectos.</response>
    /// <response code="401">Usuario o contraseña inválidos.</response>
    [HttpPost("login")]
    // Valida credenciales y estado de la cuenta antes de emitir la sesión JWT.
    public async Task<IActionResult> Login([FromBody] LoginRequest login)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(login.Username) || string.IsNullOrWhiteSpace(login.Password))
                return BadRequest("Usuario o contraseña no válidos.");
            if (login.Username.Length > 320 || login.Password.Length > 256)
                return BadRequest("Usuario o contraseña no válidos.");

            // Buscar el usuario
            var identifier = login.Username.Trim();
            var normalizedUsername = identifier.ToLowerInvariant();
            var user = await _context.users.FirstOrDefaultAsync(u =>
                u.username.ToLower() == normalizedUsername ||
                u.email == identifier ||
                u.email.ToLower() == identifier.ToLower());

            if (user == null)
                return Unauthorized("Credenciales inválidas.");

            if (!BCrypt.Net.BCrypt.Verify(login.Password, user.password_hash))
                return Unauthorized("Credenciales inválidas.");

            if (user.archived_at.HasValue)
            {
                if (user.role != "nutritionist")
                    return Unauthorized("Esta cuenta está archivada y no puede iniciar sesión.");

                var supportJwt = CrearJwtParaUsuario(user, supportOnly: true);
                SetProfessionalSessionCookie(supportJwt);
                return Ok(new
                {
                    username = user.username,
                    email = user.email,
                    role = user.role,
                    subscriptionPlan = user.subscription_plan,
                    subscriptionStatus = user.subscription_status,
                    archivedSupport = true
                });
            }

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
                new Claim(ClaimTypes.Role, user.role ?? "nutritionist")
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
            SetProfessionalSessionCookie(tokenString);

            return Ok(new
            {
                username = user.username,
                email = user.email,
                role = user.role,
                subscriptionPlan = user.subscription_plan,
                subscriptionStatus = user.subscription_status
            });
        }
        catch(Exception e) 
        {
            _logServ.LogError($"Error en Login: {e.Message}");
            return BadRequest("Error al iniciar sesión o credenciales inválidas.");
        }
    }

    // Crea usuario y envía enlace de confirmación via email
    [HttpPut("crearUser")]
    //public async Task<IActionResult> CrearUser([FromBody] Usuario usuario)
    // El alta pública crea una cuenta gratuita y su tenant dentro de una transacción protegida frente a carreras concurrentes.
    public async Task<BoolMensaje> CrearUserAsync([FromBody] Usuario usuario)
    {
        try
        {
            if (usuario == null)
                return new BoolMensaje { Exito = false, Mensaje = "Datos de registro no válidos." };

            var username = usuario.Username?.Trim().ToLowerInvariant() ?? string.Empty;
            var email = usuario.Email?.Trim().ToLowerInvariant() ?? string.Empty;
            var password = usuario.PasswordPlain ?? string.Empty;
            var nombreCompleto = usuario.FullName?.Trim() ?? string.Empty;
            var legalDocumentKey = usuario.LegalDocumentKey?.Trim() ?? string.Empty;
            var legalDocumentVersion = usuario.LegalDocumentVersion;
            var legalDocumentSha256 = usuario.LegalDocumentSha256?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return new BoolMensaje { Exito = false, Mensaje = "Usuario, contraseña y email son obligatorios." };

            if (username.Length > 50 || email.Length > 150 || nombreCompleto.Length > 100)
                return new BoolMensaje { Exito = false, Mensaje = "Los datos de registro superan la longitud permitida." };

            if (password.Length < 12 || password.Length > 256)
                return new BoolMensaje { Exito = false, Mensaje = "La contraseña debe tener entre 12 y 256 caracteres." };

            if (!string.Equals(legalDocumentKey, "saas_terms", StringComparison.Ordinal) ||
                !legalDocumentVersion.HasValue || string.IsNullOrWhiteSpace(legalDocumentSha256))
                return new BoolMensaje { Exito = false, Mensaje = "Debes aceptar las condiciones de contratación vigentes antes de crear la cuenta." };

            var currentTerms = await _context.Database.SqlQueryRaw<CurrentLegalDocument>(
                """
                SELECT document_key AS "DocumentKey", version AS "Version", sha256 AS "Sha256"
                FROM legal_documents
                WHERE document_key = 'saas_terms' AND status = 'published'
                ORDER BY version DESC
                LIMIT 1
                """).SingleOrDefaultAsync();

            if (currentTerms == null)
                return new BoolMensaje { Exito = false, Mensaje = "Las condiciones de contratación todavía no están publicadas. El registro está temporalmente deshabilitado." };

            if (currentTerms.Version != legalDocumentVersion.Value ||
                !string.Equals(currentTerms.Sha256, legalDocumentSha256, StringComparison.OrdinalIgnoreCase))
                return new BoolMensaje { Exito = false, Mensaje = "Las condiciones de contratación han cambiado. Recarga la página y acepta la versión vigente." };

            string passwordHash = BCrypt.Net.BCrypt.HashPassword(password);

            // Generar token de confirmación
            string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

            // La comprobación de username/email y el INSERT deben ejecutarse con
            // aislamiento Serializable para cerrar la ventana de carrera entre dos
            // registros concurrentes. Sin una restricción UNIQUE histórica en la BD,
            // un simple FirstOrDefaultAsync seguido de SaveChangesAsync no basta.
            await using var transaction = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

            // Comprobamos si el usuario existe ya dentro de la transacción serializable.
            // Email se compara sin distinguir mayúsculas/minúsculas porque el login
            // aplica esa misma semántica.
            users? user = await _context.users.FirstOrDefaultAsync(u =>
                u.username.ToLower() == username.ToLower() ||
                u.email.ToLower() == email.ToLower());
            if (user != null)
            {
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = user.username.Equals(username, StringComparison.OrdinalIgnoreCase)
                        ? $"El usuario {username} ya existe"
                        : $"El email {email} ya está registrado"
                };
            }

            var freePlan = await _context.subscription_plans
                .FirstOrDefaultAsync(p => p.code == "free" && p.active);

            if (freePlan == null)
                return new BoolMensaje { Exito = false, Mensaje = "El plan gratuito no está configurado." };

            // El registro público siempre crea una cuenta FREE. Las demos se conceden
            // exclusivamente desde el panel de SuperAdmin.
            user = new users
            {
                username = username,
                full_name = nombreCompleto,
                password_hash = passwordHash,
                email = email,
                role = "nutritionist",
                created_at = DateTime.UtcNow,
                email_confirmed = false,
                email_confirmation_token = HashSecurityToken(token),
                email_confirmation_expires_at = DateTime.UtcNow.AddHours(24),
                token_version = 1,
                subscription_plan = "free",
                subscription_status = "active",
                max_clients_allowed = 100
            };

            var tenant = new tenants
            {
                legal_name = string.IsNullOrWhiteSpace(nombreCompleto) ? username : nombreCompleto,
                trade_name = string.IsNullOrWhiteSpace(nombreCompleto) ? username : nombreCompleto,
                slug = $"{Regex.Replace(username.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-')}-{Guid.NewGuid():N}",
                contact_email = email,
                status = "active"
            };
            _context.tenants.Add(tenant);
            await _context.SaveChangesAsync();
            user.tenant_id = tenant.id;

            _context.users.Add(user);
            await _context.SaveChangesAsync();

            _context.subscriptions.Add(new subscriptions
            {
                tenant_id = tenant.id,
                plan_id = freePlan.id,
                status = "active",
                started_at = DateTime.UtcNow,
                expires_at = DateTime.UtcNow.AddDays(freePlan.trial_days ?? 7)
            });
            await _context.SaveChangesAsync();

            await _context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO legal_acceptances
                    (user_id, tenant_id, legal_document_id, document_key, document_version, document_sha256, accepted_at, ip_address, user_agent, context)
                SELECT {0}, {1}, id, document_key, version, sha256, CURRENT_TIMESTAMP, {2}, {3}, 'signup'
                FROM legal_documents
                WHERE document_key = 'saas_terms' AND version = {4} AND status = 'published'
                ON CONFLICT (user_id, legal_document_id, document_version, context) DO NOTHING
                """,
                user.id,
                tenant.id,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
                Request.Headers.UserAgent.ToString(),
                currentTerms.Version);

            await transaction.CommitAsync();
            await _patientDocumentTemplateSeeder.SeedTenantAsync(tenant.id, HttpContext.RequestAborted);

            //obtenemos el dominio de la url
            string frontendUrl = _configServ.GetConfigString("frontendUrl", "https://localhost:4200") ?? "https://localhost:4200";

            //Enviar email de confirmación
            string urlConfirm = $"{frontendUrl.TrimEnd('/')}/confirmar-email?token={Uri.EscapeDataString(token)}";
            BoolMensaje? bmEmail = await _emailServ.SendEmailAsync(
                email,
                "Confirma tu email",
                $"<h2>Bienvenido, {System.Net.WebUtility.HtmlEncode(usuario.Username)}</h2><p>Haz clic en el siguiente enlace para confirmar tu email:</p><a href = '{System.Net.WebUtility.HtmlEncode(urlConfirm)}' > Confirmar email </a>"
            );

            if(bmEmail == null || bmEmail.Exito == false)
            {
                _logServ.LogError($"No se pudo enviar el email de confirmación a la cuenta recién creada: {email}");
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = "La cuenta se creó, pero no se pudo enviar el email de confirmación."
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
            _logServ.LogError($"Error creando usuario: {ex.Message}");
            return new BoolMensaje
            {
                Exito = false,
                Mensaje = "No se pudo completar el registro."
            };
        }
    }

    //Confirma cuenta accediento a través de enlace en email de confirmación
    [HttpGet("confirmarEmail")]
    // El token recibido por correo se compara mediante su hash y, una vez usado, se elimina para impedir su reutilización.
    public async Task<IActionResult> ConfirmarEmail([FromQuery]string token)
    {
        var tokenHash = HashSecurityToken(token);
        var user = await _context.users.FirstOrDefaultAsync(u => u.email_confirmation_token == tokenHash);

        if (user == null)
            return BadRequest("Token inválido");

        if (!user.email_confirmation_expires_at.HasValue || user.email_confirmation_expires_at.Value <= DateTime.UtcNow)
            return BadRequest("El enlace de confirmación ha expirado.");

        if (user.archived_at.HasValue)
            return Unauthorized("Esta cuenta está archivada y no puede iniciar sesión.");

        user.email_confirmed = true;
        user.email_confirmation_token = null;
        user.email_confirmation_expires_at = null;
        await _context.SaveChangesAsync();

        // La confirmación del email no debe crear una sesión autenticada.
        // Los escáneres de enlaces de correo pueden ejecutar automáticamente GETs;
        // devolver un JWT aquí convertiría la visita del enlace en una autenticación.
        return Ok(new
        {
            username = user.username,
            email = user.email,
            message = "Email confirmado correctamente."
        });
    }

    [HttpPost("enviarReset")]
    // La respuesta es deliberadamente uniforme para no revelar si el correo corresponde a una cuenta existente.
    public async Task<BoolMensaje> EnviarReset([FromBody] PasswordResetEmailRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || req.Email.Length > 320)
            return new BoolMensaje { Exito = false, Mensaje = "Email obligatorio" };

        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        var user = await _context.users.FirstOrDefaultAsync(u => u.email.ToLower() == normalizedEmail);

        if (user == null)
            return new BoolMensaje { Exito = true, Mensaje = "Si el email corresponde a una cuenta, recibirás instrucciones." };

        //if (req.Username != user.username)
        //    return new BoolMensaje { Exito = false, Mensaje = "Usuario incorrecto" };

        // Generar token
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        user.reset_password_token = HashSecurityToken(token);
        user.reset_token_expiration = DateTime.UtcNow.AddMinutes(30);
        await _context.SaveChangesAsync();

        string frontendUrl = _configServ.GetConfigString("frontendUrl", "https://localhost:4200") ?? "https://localhost:4200";
        string url = $"{frontendUrl.TrimEnd('/')}/reset-pwd?token={Uri.EscapeDataString(token)}";

        var bm = await _emailServ.SendEmailAsync(
            user.email,
            "Recuperar contraseña",
            $@"<p>Hola {System.Net.WebUtility.HtmlEncode(user.username)},</p>
           <p>Puedes restablecer tu contraseña desde el siguiente enlace:</p>
           <a href='{url}'>Restablecer contraseña</a>
           <p>Este enlace caduca en 30 minutos.</p>"
        );

        //return new BoolMensaje { Exito = true, Mensaje = "Email enviado con instrucciones" };

        if (!bm.Exito)
        {
            _logServ.LogError($"No se pudo enviar el email de recuperación a la cuenta solicitante: {user.email}");
        }

        // No revelamos si el correo existe ni si el servidor SMTP respondió correctamente.
        // La misma respuesta evita la enumeración de cuentas mediante este endpoint.
        return new BoolMensaje
        {
            Exito = true,
            Mensaje = "Si el email corresponde a una cuenta, recibirás instrucciones."
        };
    }

    [HttpPut("resetPassword")]
    // El consumo del token se serializa para impedir que dos peticiones concurrentes cambien la contraseña con el mismo enlace.
    public async Task<BoolMensaje> ResetPassword([FromBody] PasswordResetByTokenRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 12 || req.NewPassword.Length > 256)
            return new BoolMensaje { Exito = false, Mensaje = "La nueva contraseña debe tener entre 12 y 256 caracteres." };

        var tokenHash = string.IsNullOrWhiteSpace(req.Token) ? null : HashSecurityToken(req.Token);
        if (tokenHash == null)
            return new BoolMensaje { Exito = false, Mensaje = "Token inválido" };

        // Consumimos el token bajo un lock transaccional para impedir que dos
        // peticiones concurrentes reutilicen el mismo token de recuperación.
        await using var resetTransaction = await _context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);
        await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(748392617)");

        var user = await _context.users
            .FirstOrDefaultAsync(u => u.reset_password_token == tokenHash);

        if (user == null)
            return new BoolMensaje { Exito = false, Mensaje = "Token inválido" };

        if (user.archived_at.HasValue)
            return new BoolMensaje { Exito = false, Mensaje = "Esta cuenta está archivada y no puede restablecer la contraseña." };

        if (user.reset_token_expiration < DateTime.UtcNow)
            return new BoolMensaje { Exito = false, Mensaje = "El token ha expirado" };

        user.password_hash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        user.token_version++;
        user.reset_password_token = null;
        user.reset_token_expiration = null;

        await _context.SaveChangesAsync();
        await resetTransaction.CommitAsync();

        return new BoolMensaje { Exito = true, Mensaje = "Contraseña cambiada correctamente" };
    }

    private static string HashSecurityToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    //Para usar en un componente de settings de usuario
    [Authorize(Policy = "Professional")]
    [HttpPut("cambiarPassword")]
    public async Task<BoolMensaje> CambiarPassword([FromBody] PasswordResetRequest passwordResetRequest)
    {
        try
        {
            if (passwordResetRequest == null)
            {
                return new BoolMensaje { Exito = false, Mensaje = "Datos de solicitud no válidos." };
            }

            if (string.IsNullOrWhiteSpace(passwordResetRequest.NewPassword) || passwordResetRequest.NewPassword.Length < 12 || passwordResetRequest.NewPassword.Length > 256)
            {
                return new BoolMensaje { Exito = false, Mensaje = "La contraseña debe tener al menos 12 caracteres." };
            }

            if (passwordResetRequest.NewPassword != passwordResetRequest.NewPasswordRep)
            {
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = "Las nuevas contraseñas no coinciden."
                };
            }

            var userId = AuthHelpers.GetUserId(User);
            if (userId == null)
            {
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = "No autorizado."
                };
            }

            // Buscar el usuario autenticado
            var user = await _context.users.FirstOrDefaultAsync(u => u.id == userId.Value);

            if (user == null)
            {
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = "Usuario no encontrado."
                };
            }

            // Comprobamos el password antiguo con BCrypt.Verify
            if (!BCrypt.Net.BCrypt.Verify(passwordResetRequest.OldPassword, user.password_hash))
            {
                return new BoolMensaje
                {
                    Exito = false,
                    Mensaje = "La contraseña actual es incorrecta."
                };
            }

            // Cambiamos la contraseña hasheando la nueva
            user.password_hash = BCrypt.Net.BCrypt.HashPassword(passwordResetRequest.NewPassword);
            user.token_version++;
            await _context.SaveChangesAsync();

            await _audit.LogAccessAsync(
                "CHANGE_PASSWORD",
                "users",
                user.id.ToString(),
                null,
                "El usuario cambió su propia contraseña.");

            if (!string.IsNullOrWhiteSpace(user.email))
            {
                var notification = await _emailServ.SendEmailAsync(
                    user.email,
                    "Contraseña cambiada en DietoExpress",
                    $@"<p>Hola {System.Net.WebUtility.HtmlEncode(user.username)},</p>
                       <p>Tu contraseña de DietoExpress se ha cambiado correctamente.</p>
                       <p>Si no has realizado este cambio, restablece tu contraseña inmediatamente y contacta con soporte.</p>");
                if (!notification.Exito)
                    _logServ.LogError($"No se pudo enviar la notificación de cambio de contraseña a {user.email}.");
            }

            return new BoolMensaje
            {
                Exito = true,
                Mensaje = "Contraseña cambiada correctamente."
            };
        }
        catch (Exception ex)
        {
            _logServ.LogError($"Error cambiando password: {ex.Message}");
            return new BoolMensaje
            {
                Exito = false,
                Mensaje = "Se produjo un error al cambiar la contraseña."
            };
        }
    }

    //Login con google
    [HttpGet("google-client-id")]
    [AllowAnonymous]
    public IActionResult GetGoogleClientId()
    {
        var clientId = _configServ.GetConfigString("googleClientId");

        if (string.IsNullOrWhiteSpace(clientId))
            return StatusCode(500, "Google Client ID no configurado.");

        return Ok(new { clientId });
    }

    [HttpPost("google")]
    // Google valida la identidad externa; aquí se resuelve la asociación con el usuario, tenant y plan de DietoExpress.
    public async Task<IActionResult> LoginGoogle([FromBody] GoogleLoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.IdToken) || dto.IdToken.Length > 20000)
            return BadRequest("IdToken no válido.");

        GoogleJsonWebSignature.Payload payload;
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _configServ.GetConfigString("googleClientId") }
            };

            payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken, settings);
        }
        catch (Exception)
        {
            return Unauthorized("Token de Google inválido o expirado.");
        }

        if (string.IsNullOrWhiteSpace(payload.Email) || payload.EmailVerified != true)
            return Unauthorized("La cuenta de Google no tiene el email verificado.");

        var googleId = payload.Subject;
        var email = payload.Email.Trim().ToLowerInvariant();
        var name = string.IsNullOrWhiteSpace(payload.Name) ? payload.Email : payload.Name;

        // Una cuenta ya vinculada por Google puede iniciar sesión sin volver a aceptar
        // las condiciones contractuales. La aceptación solo es requisito para un alta nueva.
        var user = await _context.users.FirstOrDefaultAsync(u => u.google_id == googleId);

        if (user == null)
        {
            // Si existe una cuenta local con ese email, enlazamos Google con ella.
            // Esto no constituye un alta nueva y, por tanto, no exige una nueva aceptación.
            user = await _context.users.FirstOrDefaultAsync(u => u.email.ToLower() == email);

            if (user != null)
            {
                if (user.archived_at.HasValue)
                    return Unauthorized("Esta cuenta está archivada y no puede iniciar sesión.");

                user.google_id = googleId;
                user.provider = "google";
                user.email_confirmed = true;
                user.last_login = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            else
            {
                // El alta Google y su aceptación legal deben ser atómicos: si la creación
                // termina correctamente, la evidencia de aceptación queda asociada al
                // mismo usuario y tenant. El lock evita dos altas simultáneas para el mismo
                // email/Google ID.
                var currentTerms = await _context.Database.SqlQueryRaw<CurrentLegalDocument>(
                    """
                    SELECT document_key AS "DocumentKey", version AS "Version", sha256 AS "Sha256"
                    FROM legal_documents
                    WHERE document_key = 'saas_terms' AND status = 'published'
                    ORDER BY version DESC
                    LIMIT 1
                    """).SingleOrDefaultAsync();

                if (currentTerms == null ||
                    dto.LegalDocumentVersion != currentTerms.Version ||
                    !string.Equals(dto.LegalDocumentSha256, currentTerms.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(StatusCodes.Status428PreconditionRequired,
                        "Para crear una cuenta con Google debes aceptar las condiciones de contratación vigentes.");
                }

                await using var googleTransaction = await _context.Database.BeginTransactionAsync();
                var createdGoogleTenantId = 0;
                try
                {
                    await _context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(748392616)");

                    // Reconsultar después del lock por si otra petición creó la cuenta.
                    user = await _context.users.FirstOrDefaultAsync(u =>
                        u.google_id == googleId || u.email.ToLower() == email);

                    if (user != null)
                    {
                        if (user.archived_at.HasValue)
                            return Unauthorized("Esta cuenta está archivada y no puede iniciar sesión.");

                        if (user.google_id != googleId)
                        {
                            user.google_id = googleId;
                            user.provider = "google";
                            user.email_confirmed = true;
                        }

                        user.last_login = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                    }
                    else
                    {
                        var username = GenerateUniqueUsername(name);
                        user = new users
                        {
                            username = username,
                            full_name = name,
                            email = email,
                            google_id = googleId,
                            provider = "google",
                            role = "nutritionist",
                            email_confirmed = true,
                            created_at = DateTime.UtcNow,
                            last_login = DateTime.UtcNow,
                            subscription_plan = "free",
                            subscription_status = "active",
                            max_clients_allowed = 100
                        };

                        var tenantGoogle = new tenants
                        {
                            legal_name = name,
                            trade_name = name,
                            slug = $"{username}-{Guid.NewGuid():N}",
                            contact_email = email,
                            status = "active"
                        };

                        _context.tenants.Add(tenantGoogle);
                        await _context.SaveChangesAsync();

                        user.tenant_id = tenantGoogle.id;
                        createdGoogleTenantId = tenantGoogle.id;
                        _context.users.Add(user);
                        await _context.SaveChangesAsync();

                        var freePlanGoogle = await _context.subscription_plans
                            .FirstOrDefaultAsync(p => p.code == "free" && p.active);

                        if (freePlanGoogle == null)
                            throw new InvalidOperationException("El plan gratuito no está configurado.");

                        _context.subscriptions.Add(new subscriptions
                        {
                            tenant_id = tenantGoogle.id,
                            plan_id = freePlanGoogle.id,
                            status = "active",
                            started_at = DateTime.UtcNow,
                            expires_at = DateTime.UtcNow.AddDays(freePlanGoogle.trial_days ?? 7)
                        });
                        await _context.SaveChangesAsync();

                        await _context.Database.ExecuteSqlRawAsync(
                            """
                            INSERT INTO legal_acceptances
                                (user_id, tenant_id, legal_document_id, document_key, document_version,
                                 document_sha256, accepted_at, ip_address, user_agent, context)
                            SELECT {0}, {1}, id, document_key, version, sha256, CURRENT_TIMESTAMP,
                                   {2}, {3}, 'signup_google'
                            FROM legal_documents
                            WHERE document_key = 'saas_terms'
                              AND version = {4}
                              AND sha256 = {5}
                              AND status = 'published'
                            ON CONFLICT (user_id, legal_document_id, document_version, context) DO NOTHING
                            """,
                            user.id,
                            tenantGoogle.id,
                            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
                            Request.Headers.UserAgent.ToString(),
                            currentTerms.Version,
                            currentTerms.Sha256);

                    }

                    await googleTransaction.CommitAsync();
                    if (createdGoogleTenantId > 0)
                        await _patientDocumentTemplateSeeder.SeedTenantAsync(createdGoogleTenantId, HttpContext.RequestAborted);
                }
                catch
                {
                    await googleTransaction.RollbackAsync();
                    throw;
                }
            }
        }
        else
        {
            user.last_login = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        if (user.archived_at.HasValue)
            return Unauthorized("Esta cuenta está archivada y no puede iniciar sesión.");

        var jwt = CrearJwtParaUsuario(user);
        SetProfessionalSessionCookie(jwt);

        return Ok(new
        {
            username = user.username,
            email = user.email,
            role = user.role,
            subscriptionPlan = user.subscription_plan,
            subscriptionStatus = user.subscription_status
        });
    }

    //Subrutina que se usa en los distintos modos de login. Genera un token con el user
    // Un único constructor mantiene coherentes los claims de identidad, tenant, suscripción y revocación.
    private string CrearJwtParaUsuario(users user, bool supportOnly = false)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"]!);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.id.ToString()),
            new Claim(ClaimTypes.Name, user.username),
            new Claim(ClaimTypes.Role, user.role ?? "nutritionist"),
            new Claim("subscriptionPlan", user.subscription_plan ?? "free"),
            new Claim("subscriptionStatus", user.subscription_status ?? "active"),
            new Claim("tokenVersion", user.token_version.ToString())
        };

        if (supportOnly)
            claims.Add(new Claim("archivedSupport", "true"));

        if (user.tenant_id.HasValue)
        {
            claims.Add(new Claim("tenantId", user.tenant_id.Value.ToString()));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = _config["Jwt:Issuer"],
            Audience = _config["Jwt:Audience"],
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(3),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    //Crea un nombre de usuario único
    //puede transformar José López en jose.lopez y si ya existe añadir un sufijo numérico
    // Genera un identificador legible a partir del nombre de Google y añade sufijo si ya está ocupado.
    private string GenerateUniqueUsername(string name)
    {
        // Normalizar: quitar espacios, acentos, etc. (aquí simple)
        var baseName = name.ToLower().Replace(" ", ".").Normalize(NormalizationForm.FormD);
        baseName = Regex.Replace(baseName, @"\p{Mn}", ""); // quitar diacríticos
        baseName = Regex.Replace(baseName, @"[^a-z0-9.]", "");

        var candidate = baseName;
        int suffix = 0;
        while (_context.users.Any(u => u.username.ToLower() == candidate.ToLower()))
        {
            suffix++;
            candidate = $"{baseName}{suffix}";
        }
        return candidate;
    }

    [Authorize(Policy = "Professional")]
    [HttpPost("refreshSession")]
    // La renovación vuelve a leer la cuenta para reflejar cambios recientes de rol, suscripción o token_version.
    public async Task<IActionResult> RefreshSession()
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var user = await _context.users.FirstOrDefaultAsync(u => u.id == userId.Value);
        if (user == null || user.archived_at.HasValue)
            return Unauthorized("La cuenta no está disponible.");

        var jwt = CrearJwtParaUsuario(user);
        SetProfessionalSessionCookie(jwt);
        return Ok(new
        {
            username = user.username,
            email = user.email,
            role = user.role,
            subscriptionPlan = user.subscription_plan,
            subscriptionStatus = user.subscription_status
        });
    }

    [Authorize(Policy = "Professional")]
    [HttpPost("logout")]
    // Incrementar token_version revoca las sesiones emitidas previamente además de borrar la cookie del navegador.
    public async Task<IActionResult> Logout()
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId.HasValue)
        {
            var user = await _context.users.FirstOrDefaultAsync(u => u.id == userId.Value && u.archived_at == null);
            if (user != null)
            {
                user.token_version++;
                await _context.SaveChangesAsync();
            }
        }

        Response.Cookies.Delete("dietoexpress_professional_session", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
        return NoContent();
    }

    // El JWT queda en una cookie HttpOnly, Secure y SameSite=Strict para evitar su acceso desde JavaScript.
    private void SetProfessionalSessionCookie(string jwt)
    {
        Response.Cookies.Append("dietoexpress_professional_session", jwt, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddHours(3),
            MaxAge = TimeSpan.FromHours(3),
            Path = "/"
        });
    }

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
    public int? LegalDocumentVersion { get; set; }
    public string? LegalDocumentSha256 { get; set; }
}