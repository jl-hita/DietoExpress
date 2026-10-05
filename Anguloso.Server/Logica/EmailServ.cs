using Anguloso.Server.Model;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
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

    public EmailServ(ConfigServ configServ, LogServ logServ)
    {
        _configServ = configServ;
        _logServ = logServ;
    }

    // SMTP se configura externamente; las credenciales nunca forman parte del código ni de la respuesta al cliente.
    // El Message-ID determinista permite a los servidores/proveedores que implementan deduplicación SMTP reconocer
    // reenvíos del mismo efecto tras una caída del worker entre el envío y el marcado del job como completado.
    // SMTP no ofrece una garantía universal de exactly-once: la ventana de caída después de aceptar el mensaje
    // por el servidor SMTP sigue siendo intrínsecamente ambigua y debe considerarse at-least-once.
    public async Task<BoolMensaje> SendEmailAsync(string to, string subject, string htmlBody, string? idempotencyKey = null)
    {
        try
        {
            string smtpSever = _configServ.GetConfigString("smtpServer") ?? throw new Exception("smtpServer no configurado");
            int smtpPort = _configServ.GetConfigInt("smtpPort", 587) ?? 587;
            bool smtpEnableSsl = _configServ.GetConfigBool("smtpEnableSsl", true) ?? true;
            string smtpFromEmail = _configServ.GetConfigString("smtpFromEmail") ?? throw new Exception("smtpFromEmail no configurado");
            string smtpFromName = _configServ.GetConfigString("smtpFromName", "dietexpress") ?? "dietexpress";
            string smtpUser = _configServ.GetConfigString("smtpUser") ?? throw new Exception("smtpUser no configurado");
            string smtpPwd = _configServ.GetConfigString("smtpPwd") ?? throw new Exception("smtpPwd no configurado");

            _logServ.LogInfo($"Preparando envío SMTP a {to} usando {smtpSever}:{smtpPort}.");

            using (var client = new SmtpClient(smtpSever, smtpPort))
            {
                client.EnableSsl = smtpEnableSsl;
                client.Credentials = new NetworkCredential(smtpUser, smtpPwd);

                var mail = new MailMessage
                {
                    From = new MailAddress(smtpFromEmail, smtpFromName),
                    Subject = subject,
                    Body = htmlBody,
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

                    _logServ.LogInfo($"Email enviado a {to} con Message-ID {messageId}");

                    return new BoolMensaje
                    {
                        Exito = true,
                        Mensaje = $"Email enviado a {to}"
                    };
                }
                catch (Exception ex)
                {
                    _logServ.LogError($"Error SMTP al enviar email a {to}: {ex.GetType().Name}: {ex.Message}");
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
            _logServ.LogError($"Error preparando email para {to}: {ex.GetType().Name}: {ex.Message}");
            return new BoolMensaje
            {
                Exito = false,
                Mensaje = "No se ha podido preparar el envío del email."
            };
        }
    }
}
