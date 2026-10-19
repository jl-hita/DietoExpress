using Anguloso.Server.Model;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;

namespace Anguloso.Server.Logica;

/*
 * https://app.brevo.com/
 */

/*
public interface IEmailService
{
    Task<BoolMensaje> SendEmailAsync(string to, string subject, string htmlBody);
}
*/
// El servicio encapsula la configuración SMTP y la composición del envío para que los procesos de negocio no dependan de detalles de transporte.
public class EmailServ
{
    private readonly ConfigServ _configServ;
    private readonly LogServ _logServ;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public EmailServ(ConfigServ configServ, LogServ logServ, IHttpContextAccessor httpContextAccessor)
    {
        _configServ = configServ;
        _logServ = logServ;
        _httpContextAccessor = httpContextAccessor;
    }

    private string GetEmailActor()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return "sistema/proceso en segundo plano";

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "ID desconocido";
        var userName = user.Identity?.Name;
        var role = user.FindFirstValue(ClaimTypes.Role) ?? "rol desconocido";
        return string.IsNullOrWhiteSpace(userName)
            ? $"usuario ID {userId}, rol {role}"
            : $"usuario ID {userId} ({userName}), rol {role}";
    }

    private static string SafeLogValue(string? value) =>
        (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");

    // Las plantillas de negocio son fragmentos HTML; este contenedor común aplica una presentación
    // coherente y adaptable en clientes de correo sin depender de hojas de estilo externas.
    private static string BuildHtmlEmail(string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(htmlBody))
            htmlBody = "<p>Consulta DietoExpress para ver más información.</p>";

        // No anidamos documentos HTML completos creados por plantillas específicas.
        if (htmlBody.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
            htmlBody.Contains("<!doctype", StringComparison.OrdinalIgnoreCase))
            return htmlBody;

        return $@"<!doctype html>
<html lang=""es"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
  <title>DietoExpress</title>
</head>
<body style=""margin:0;padding:0;background:#f1f5f9;font-family:Arial,Helvetica,sans-serif;color:#0f172a;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""background:#f1f5f9;padding:24px 12px;"">
    <tr><td align=""center"">
      <table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""width:100%;max-width:600px;background:#ffffff;border:1px solid #e2e8f0;border-radius:12px;overflow:hidden;"">
        <tr><td style=""background:#0d9488;padding:22px 28px;color:#ffffff;font-size:22px;font-weight:700;letter-spacing:.2px;"">DietoExpress</td></tr>
        <tr><td style=""padding:28px;line-height:1.6;font-size:15px;"">{htmlBody}</td></tr>
        <tr><td style=""padding:16px 28px;background:#f8fafc;border-top:1px solid #e2e8f0;color:#64748b;font-size:12px;line-height:1.5;"">Mensaje enviado desde DietoExpress. Accede a la aplicación para consultar más información.</td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>";
    }

    // SMTP se configura externamente; las credenciales nunca forman parte del código ni de la respuesta al cliente.
    // El Message-ID determinista permite a los servidores/proveedores que implementan deduplicación SMTP reconocer
    // reenvíos del mismo efecto tras una caída del worker entre el envío y el marcado del job como completado.
    // SMTP no ofrece una garantía universal de exactly-once: la ventana de caída después de aceptar el mensaje
    // por el servidor SMTP sigue siendo intrínsecamente ambigua y debe considerarse at-least-once.
    public async Task<BoolMensaje> SendEmailAsync(string to, string subject, string htmlBody, string? idempotencyKey = null)
    {
        var actor = GetEmailActor();
        var safeSubject = SafeLogValue(subject);
        var safeRecipient = SafeLogValue(to);

        _logServ.LogInfo($"Solicitud de envío de email: asunto \"{safeSubject}\", destinatario {safeRecipient}, actor {actor}.");

        try
        {
            string smtpSever = _configServ.GetConfigString("smtpServer") ?? throw new Exception("smtpServer no configurado");
            int smtpPort = _configServ.GetConfigInt("smtpPort", 587) ?? 587;
            bool smtpEnableSsl = _configServ.GetConfigBool("smtpEnableSsl", true) ?? true;
            string smtpFromEmail = _configServ.GetConfigString("smtpFromEmail") ?? throw new Exception("smtpFromEmail no configurado");
            string smtpFromName = _configServ.GetConfigString("smtpFromName", "dietexpress") ?? "dietexpress";
            string smtpUser = _configServ.GetConfigString("smtpUser") ?? throw new Exception("smtpUser no configurado");
            string smtpPwd = _configServ.GetConfigString("smtpPwd") ?? throw new Exception("smtpPwd no configurado");

            _logServ.LogInfo($"Conectando con SMTP {smtpSever}:{smtpPort} para el email con asunto \"{safeSubject}\" a {safeRecipient}, solicitado por {actor}.");

            using (var client = new SmtpClient(smtpSever, smtpPort))
            {
                client.EnableSsl = smtpEnableSsl;
                // Evita que la autenticación dependa de credenciales del proceso o de la cuenta del servicio.
                client.UseDefaultCredentials = false;
                client.Credentials = new NetworkCredential(smtpUser, smtpPwd);

                var mail = new MailMessage
                {
                    From = new MailAddress(smtpFromEmail, smtpFromName),
                    Subject = subject,
                    SubjectEncoding = Encoding.UTF8,
                    Body = BuildHtmlEmail(htmlBody),
                    BodyEncoding = Encoding.UTF8,
                    IsBodyHtml = true
                };

                // Los jobs persistentes aportan una clave estable para que un reintento del mismo efecto
                // conserve el Message-ID. Las llamadas directas no tienen una identidad durable y reciben
                // un identificador nuevo para no confundir dos envíos legítimos con el mismo contenido.
                var identity = idempotencyKey ?? Guid.NewGuid().ToString("N");
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
                var messageId = $"<{Convert.ToHexString(hash).ToLowerInvariant()}@dietoexpress.local>";
                mail.Headers.Add("Message-ID", messageId);

                mail.To.Add(to);

                try
                {
                    await client.SendMailAsync(mail);

                    _logServ.LogInfo($"SMTP aceptó el email: asunto \"{safeSubject}\", destinatario {safeRecipient}, actor {actor}, Message-ID {messageId}. Esto confirma la aceptación SMTP, no la entrega en bandeja de entrada.");

                    return new BoolMensaje
                    {
                        Exito = true,
                        Mensaje = $"Email enviado a {to}"
                    };
                }
                catch (Exception ex)
                {
                    _logServ.LogError($"Error SMTP al enviar email: asunto \"{safeSubject}\", destinatario {safeRecipient}, actor {actor}: {ex.GetType().Name}: {ex.Message}");
                    if (ex.Message.Contains("authenticate", StringComparison.OrdinalIgnoreCase)
                        || ex.Message.Contains("5.7.0", StringComparison.OrdinalIgnoreCase)
                        || ex.Message.Contains("5.7.1", StringComparison.OrdinalIgnoreCase))
                    {
                        _logServ.LogWarning(
                            "El servidor SMTP ha rechazado el envío por autenticación. La configuración se lee de la tabla config (EMAIL_SMTP_USERNAME y EMAIL_SMTP_PASSWORD, además de servidor, puerto y TLS). Comprueba que esas claves estén configuradas y que SMTP AUTH esté habilitado en el proveedor; no se han registrado sus valores.");
                    }
                    return new BoolMensaje
                    {
                        Exito = false,
                        Mensaje = "No se ha podido enviar el email."
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logServ.LogError($"Error preparando email: asunto \"{safeSubject}\", destinatario {safeRecipient}, actor {actor}: {ex.GetType().Name}: {ex.Message}");
            return new BoolMensaje
            {
                Exito = false,
                Mensaje = "No se ha podido preparar el envío del email."
            };
        }
    }
}
