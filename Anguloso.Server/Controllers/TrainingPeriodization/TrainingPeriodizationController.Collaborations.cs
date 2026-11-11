using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Colaboración entre profesionales sobre el entrenamiento de un cliente.
/// </summary>
public sealed partial class TrainingPeriodizationController
{
    [HttpGet("clients/{clientId:int}/collaborations")]
    public async Task<IActionResult> GetCollaborations(int clientId, CancellationToken cancellationToken)
    {
        if (!await CanManageClient(clientId, cancellationToken)) return NotFound();
        await using var command = Command("""
            SELECT tc.id, tc.professional_user_id, u.full_name, u.email, tc.access_scope, tc.status,
                   tc.created_at, tc.accepted_at, tc.revoked_at
            FROM training_collaborations tc
            JOIN users u ON u.id = tc.professional_user_id
            WHERE tc.client_id = @clientId
            ORDER BY tc.created_at DESC, tc.id DESC
            """, ("clientId", clientId));
        var items = new List<TrainingCollaborationDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new TrainingCollaborationDto(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetDateTime(6),
                reader.IsDBNull(7) ? null : reader.GetDateTime(7), reader.IsDBNull(8) ? null : reader.GetDateTime(8)));
        return Ok(items);
    }

    [HttpPost("clients/{clientId:int}/collaborations")]
    public async Task<IActionResult> InviteCollaborator(int clientId, [FromBody] InviteTrainingCollaboratorRequest request, CancellationToken cancellationToken)
    {
        var inviterId = AuthHelpers.GetUserId(User);
        if (inviterId is null) return Unauthorized();
        if (!await CanManageClient(clientId, cancellationToken)) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Trim().Length > 254)
            return BadRequest(new { message = "Introduce el correo electrónico del profesional invitado." });
        if (request.AccessScope is not (null or "training"))
            return BadRequest(new { message = "Por ahora solo está disponible el acceso de consulta al entrenamiento." });

        await using var findProfessional = Command("""
            SELECT id FROM users
            WHERE LOWER(email) = LOWER(@email)
              AND role IN ('nutritionist','clinic_admin')
              AND archived_at IS NULL
            LIMIT 1
            """, ("email", request.Email.Trim()));
        var professionalIdValue = await findProfessional.ExecuteScalarAsync(cancellationToken);
        if (professionalIdValue is null) return NotFound(new { message = "No se ha encontrado una cuenta profesional activa con ese correo." });
        var professionalId = Convert.ToInt32(professionalIdValue);
        if (professionalId == inviterId.Value) return BadRequest(new { message = "No puedes invitarte a ti mismo." });

        await using var command = Command("""
            INSERT INTO training_collaborations(client_id, professional_user_id, tenant_id, access_scope, status, invited_by_user_id, created_at, accepted_at, revoked_at)
            SELECT c.id, @professionalId, c.tenant_id, @scope, 'pending', @inviterId, NOW(), NULL, NULL
            FROM clients c
            WHERE c.id = @clientId AND c.archived_at IS NULL
              AND c.tenant_id IS NOT NULL
              AND c.tenant_id = (SELECT u.tenant_id FROM users u WHERE u.id = @professionalId)
            ON CONFLICT(client_id, professional_user_id) DO UPDATE SET
                tenant_id = EXCLUDED.tenant_id, access_scope = EXCLUDED.access_scope,
                status = 'pending', invited_by_user_id = EXCLUDED.invited_by_user_id,
                created_at = NOW(), accepted_at = NULL, revoked_at = NULL
            RETURNING id, status
            """, ("clientId", clientId), ("professionalId", professionalId), ("scope", request.AccessScope ?? "training"),
            ("inviterId", inviterId.Value));
        int collaborationId;
        string status;
        await using (var result = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await result.ReadAsync(cancellationToken))
                return BadRequest(new { message = "El profesional debe pertenecer a la misma clínica para colaborar en este expediente." });
            collaborationId = result.GetInt32(0);
            status = result.GetString(1);
        }

        // El correo es una notificación complementaria: un fallo SMTP no invalida la invitación persistida.
        string? inviteeEmail;
        await using (var emailCommand = Command("""
            SELECT u.email
            FROM training_collaborations tc
            JOIN users u ON u.id = tc.professional_user_id
            WHERE tc.id = @id
            """, ("id", collaborationId)))
            inviteeEmail = Convert.ToString(await emailCommand.ExecuteScalarAsync(cancellationToken));

        var emailSent = false;
        if (!string.IsNullOrWhiteSpace(inviteeEmail))
        {
            var emailResult = await _emailServ.SendEmailAsync(
                inviteeEmail,
                "Invitación para colaborar en un plan deportivo · DietoExpress",
                "<p>Has recibido una invitación para consultar un plan de entrenamiento en DietoExpress.</p><p>Inicia sesión en tu cuenta y abre <strong>Invitaciones de entrenamiento</strong> para revisar y aceptar la solicitud.</p><p>El acceso es de solo lectura y queda limitado al ámbito autorizado de la clínica.</p>",
                $"training-collaboration-{collaborationId}");
            emailSent = emailResult.Exito;
        }

        return Ok(new {
            id = collaborationId,
            status,
            emailSent,
            message = emailSent
                ? "Invitación creada y solicitud de correo aceptada por el servidor SMTP. El profesional debe aceptarla para acceder."
                : "Invitación creada, pero no se pudo enviar el aviso por correo. El profesional puede encontrarla dentro de DietoExpress."
        });
    }

    [HttpPost("collaborations/{collaborationId:int}/accept")]
    public async Task<IActionResult> AcceptCollaboration(int collaborationId, CancellationToken cancellationToken)
    {
        var professionalId = AuthHelpers.GetUserId(User);
        if (professionalId is null) return Unauthorized();
        await using var command = Command("""
            UPDATE training_collaborations
            SET status = 'active', accepted_at = NOW(), revoked_at = NULL
            WHERE id = @id AND professional_user_id = @professionalId AND status = 'pending'
              AND EXISTS (
                  SELECT 1 FROM clients c
                  WHERE c.id = training_collaborations.client_id AND c.archived_at IS NULL
                    AND training_collaborations.tenant_id IS NOT DISTINCT FROM (SELECT u.tenant_id FROM users u WHERE u.id = @professionalId)
              )
            RETURNING id, client_id
            """, ("id", collaborationId), ("professionalId", professionalId.Value));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound(new { message = "La invitación no existe, ya fue respondida o no te corresponde." });
        return Ok(new { id = reader.GetInt32(0), clientId = reader.GetInt32(1), status = "active" });
    }

    [HttpPost("collaborations/{collaborationId:int}/revoke")]
    public async Task<IActionResult> RevokeCollaboration(int collaborationId, CancellationToken cancellationToken)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId is null) return Unauthorized();
        await using var command = Command("""
            UPDATE training_collaborations tc
            SET status = 'revoked', revoked_at = NOW()
            WHERE tc.id = @id AND tc.status IN ('pending','active')
              AND (tc.invited_by_user_id = @userId OR EXISTS (
                  SELECT 1 FROM clients c
                  WHERE c.id = tc.client_id AND c.archived_at IS NULL
                    AND (c.user_id = @userId OR c.tenant_id = (SELECT u.tenant_id FROM users u WHERE u.id = @userId AND u.role = 'clinic_admin'))
              ))
            RETURNING tc.id, tc.client_id
            """, ("id", collaborationId), ("userId", userId.Value));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound(new { message = "No se encontró una colaboración revocable para este usuario." });
        return Ok(new { id = reader.GetInt32(0), clientId = reader.GetInt32(1), status = "revoked" });
    }

    [HttpGet("collaborations/invitations")]
    public async Task<IActionResult> GetMyInvitations(CancellationToken cancellationToken)
    {
        var professionalId = AuthHelpers.GetUserId(User);
        if (professionalId is null) return Unauthorized();
        await using var command = Command("""
            SELECT tc.id, tc.client_id, c.full_name, tc.access_scope, tc.created_at, u.full_name
            FROM training_collaborations tc
            JOIN clients c ON c.id = tc.client_id
            JOIN users u ON u.id = tc.invited_by_user_id
            WHERE tc.professional_user_id = @professionalId AND tc.status = 'pending' AND c.archived_at IS NULL
              AND tc.tenant_id IS NOT DISTINCT FROM (SELECT u.tenant_id FROM users u WHERE u.id = @professionalId)
            ORDER BY tc.created_at DESC
            """, ("professionalId", professionalId.Value));
        var items = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new { id = reader.GetInt32(0), clientId = reader.GetInt32(1), clientName = reader.GetString(2),
                accessScope = reader.GetString(3), createdAt = reader.GetDateTime(4), invitedBy = reader.GetString(5) });
        return Ok(items);
    }
}
