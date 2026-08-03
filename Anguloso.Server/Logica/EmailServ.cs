using Anguloso.Server.Model;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;
using System.Runtime;

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
//public class EmailServ: IEmailService
public class EmailServ
{
    private readonly ConfigServ _configServ;
    private readonly LogServ _logServ;

    public EmailServ(ConfigServ configServ, LogServ logServ)
    {
        _configServ = configServ;
        _logServ = logServ;
    }

    public async Task<BoolMensaje> SendEmailAsync(string to, string subject, string htmlBody)
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

            Console.WriteLine($"smtpSever: {smtpSever}, smtpPort: {smtpPort}, smtpEnableSsl {smtpEnableSsl}, smtpFromEmail: {smtpFromEmail}, smtpFromName: {smtpFromName}, smtpUser: {smtpUser}, smtpPwd: {smtpPwd}");

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

                mail.To.Add(to);

                try
                {
                    await client.SendMailAsync(mail);

                    //Console.WriteLine("");
                    _logServ.LogInfo($"Email enviado a {to}");

                    return new BoolMensaje
                    {
                        Exito = true,
                        Mensaje = $"Email enviado a {to}"
                    };
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error al enviar email -> {e.Message}");
                    return new BoolMensaje
                    {
                        Exito = false,
                        Mensaje = $"Error al enviar email -> {e.Message}"
                    };
                }
            }
        }
        catch (Exception ex)
        {
            return new BoolMensaje
            {
                Exito = false,
                Mensaje = $"Error al enviar el email => {ex.Message}"
            };
        }
    }
}
