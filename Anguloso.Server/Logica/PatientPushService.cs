using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Anguloso.Server.Models;

namespace Anguloso.Server.Logica;

/// <summary>
/// Sends privacy-preserving patient push alerts through Firebase Cloud Messaging HTTP v1.
/// Server credentials are supplied outside the repository; push failures never roll back a message.
/// </summary>
public sealed class PatientPushService(
    angulosodbContext context,
    IConfiguration configuration,
    HttpClient httpClient,
    ILogger<PatientPushService> logger)
{
    private const string MessagingScope = "https://www.googleapis.com/auth/firebase.messaging";

    public async Task NotifyPatientOfNewMessageAsync(int clientId, CancellationToken cancellationToken = default)
    {
        await SendToClientAsync(clientId, "Nuevo mensaje", "Tienes un nuevo mensaje de tu profesional.", "messages", cancellationToken);
    }

    public async Task SendToClientAsync(
        int clientId,
        string title,
        string body,
        string target,
        CancellationToken cancellationToken = default)
    {
        var projectId = configuration["DIETOEXPRESS_FCM_PROJECT_ID"];
        var serviceAccountJson = configuration["DIETOEXPRESS_FCM_SERVICE_ACCOUNT_JSON"];
        var serviceAccountPath = configuration["DIETOEXPRESS_FCM_SERVICE_ACCOUNT_PATH"];
        if (string.IsNullOrWhiteSpace(serviceAccountJson) &&
            !string.IsNullOrWhiteSpace(serviceAccountPath) && File.Exists(serviceAccountPath))
        {
            try { serviceAccountJson = await File.ReadAllTextAsync(serviceAccountPath, cancellationToken); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo leer el archivo de credenciales FCM configurado.");
                return;
            }
        }
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(serviceAccountJson))
        {
            logger.LogDebug("FCM no está configurado; se omite el envío push al paciente {ClientId}.", clientId);
            return;
        }

        var tokens = await context.Database.SqlQueryRaw<string>(
            @"SELECT token AS ""Value"" FROM patient_push_tokens WHERE client_id = {0}", clientId)
            .ToListAsync(cancellationToken);
        if (tokens.Count == 0) return;

        string accessToken;
        try
        {
            var credential = GoogleCredential.FromJson(serviceAccountJson).CreateScoped(MessagingScope);
            accessToken = await credential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo obtener credenciales de FCM para enviar notificaciones.");
            return;
        }

        foreach (var token in tokens)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"https://fcm.googleapis.com/v1/projects/{Uri.EscapeDataString(projectId)}/messages:send");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                request.Content = JsonContent.Create(new
                {
                    message = new
                    {
                        token,
                        notification = new { title, body },
                        data = new { target }
                    }
                });

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                using var response = await httpClient.SendAsync(request, timeout.Token);
                if (response.IsSuccessStatusCode) continue;

                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound ||
                    (response.StatusCode == HttpStatusCode.BadRequest && errorBody.Contains("UNREGISTERED", StringComparison.OrdinalIgnoreCase)))
                {
                    await context.Database.ExecuteSqlInterpolatedAsync(
                        $@"DELETE FROM patient_push_tokens WHERE client_id = {clientId} AND token = {token}",
                        cancellationToken);
                }
                else
                {
                    logger.LogWarning("FCM rechazó una notificación para paciente {ClientId} con estado HTTP {StatusCode}.",
                        clientId, (int)response.StatusCode);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo enviar una notificación push al paciente {ClientId}.", clientId);
            }
        }
    }
}
