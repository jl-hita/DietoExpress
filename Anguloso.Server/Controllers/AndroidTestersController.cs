using System.Net;
using System.Net.Mail;
using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Altas de pacientes en la prueba cerrada de la app Android. El profesional solicita el alta,
/// SuperAdmin añade el correo en Play Console y después envía al paciente el enlace de participación.
/// Google no permite gestionar listas de correos de testers por API, por lo que ese paso es manual.
/// </summary>
[ApiController]
public sealed class AndroidTestersController : ControllerBase
{
    public const string OptInUrl = "https://play.google.com/apps/testing/com.dietoexpress.mobile";

    private readonly angulosodbContext _context;
    private readonly EmailServ _email;
    private readonly ILicenseService _licenses;
    private readonly IAuditLogService _audit;

    public AndroidTestersController(angulosodbContext context, EmailServ email, ILicenseService licenses, IAuditLogService audit)
    {
        _context = context;
        _email = email;
        _licenses = licenses;
        _audit = audit;
    }

    [HttpGet("api/clients/{clientId:int}/android-tester")]
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> Get(int clientId, CancellationToken ct)
    {
        if (await FindAccessibleClientAsync(clientId, ct) == null) return NotFound("Cliente no encontrado.");
        return Ok(new { request = await FindRequestAsync(clientId, ct) });
    }

    [HttpPost("api/clients/{clientId:int}/android-tester")]
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> RequestAccess(int clientId, [FromBody] AndroidTesterRequestInput input, CancellationToken ct)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (userId == null) return Unauthorized();

        var client = await FindAccessibleClientAsync(clientId, ct);
        if (client == null) return NotFound("Cliente no encontrado.");
        if (!await _licenses.CanUseFeatureAsync(client.tenant_id, "CLIENT_PORTAL")) return Forbid();

        var googleEmail = NormalizeEmail(input.GoogleEmail);
        if (googleEmail == null) return BadRequest(new { message = "Indica un correo válido." });
        if (!input.ConsentConfirmed)
            return BadRequest(new { message = "Confirma que el paciente consiente compartir su correo para la prueba." });

        // Repetir la solicitud con el mismo correo no la reabre ni vuelve a avisar a SuperAdmin.
        var changed = await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO android_tester_requests (client_id, requested_by_user_id, google_email)
            VALUES ({clientId}, {userId.Value}, {googleEmail})
            ON CONFLICT (client_id) DO UPDATE
            SET requested_by_user_id = EXCLUDED.requested_by_user_id, google_email = EXCLUDED.google_email,
                status = 'pending', consent_confirmed_at = NOW(), requested_at = NOW(), invited_at = NULL
            WHERE android_tester_requests.google_email <> EXCLUDED.google_email
            """, ct);

        if (changed > 0)
        {
            await _audit.LogAccessAsync("android_tester_requested", "client", clientId.ToString(), clientId,
                "Solicitud de alta en la prueba de la app Android con consentimiento confirmado.");
            await NotifySuperAdminsAsync(googleEmail, ct);
        }

        return Ok(new { request = await FindRequestAsync(clientId, ct) });
    }

    [HttpGet("api/admin/android-testers")]
    [Authorize(Roles = "superadmin")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await _context.Database.SqlQueryRaw<AndroidTesterAdminRow>("""
            SELECT r.id AS "Id", r.google_email AS "GoogleEmail", r.status AS "Status",
                   r.requested_at AS "RequestedAt", r.invited_at AS "InvitedAt",
                   c.full_name AS "ClientName", u.full_name AS "RequestedBy"
            FROM android_tester_requests r
            JOIN clients c ON c.id = r.client_id AND c.archived_at IS NULL
            LEFT JOIN users u ON u.id = r.requested_by_user_id
            ORDER BY (r.status = 'pending') DESC, r.requested_at DESC
            LIMIT 1000
            """).ToListAsync(ct);
        return Ok(new { optInUrl = OptInUrl, requests = rows });
    }

    /// <summary>Se invoca cuando los correos ya figuran en la lista de testers de Play Console.</summary>
    [HttpPost("api/admin/android-testers/invite")]
    [Authorize(Roles = "superadmin")]
    public async Task<IActionResult> Invite([FromBody] AndroidTesterInviteInput input, CancellationToken ct)
    {
        var ids = input.Ids?.Distinct().Take(200).ToArray() ?? [];
        if (ids.Length == 0) return BadRequest(new { message = "Selecciona al menos una solicitud." });

        var sent = 0;
        var failed = 0;
        foreach (var id in ids)
        {
            // La reserva atómica evita enviar dos veces la invitación ante un doble clic.
            var claimed = await _context.Database.SqlQueryRaw<string>("""
                UPDATE android_tester_requests r SET status = 'invited', invited_at = NOW()
                FROM clients c
                WHERE r.id = {0} AND c.id = r.client_id AND c.archived_at IS NULL
                RETURNING r.google_email AS "Value"
                """, id).ToListAsync(ct);
            if (claimed.Count == 0) continue;

            var result = await _email.SendEmailAsync(claimed[0], "Ya puedes probar la app de DietoExpress para Android", InvitationBody());
            if (result.Exito)
            {
                sent++;
                continue;
            }

            failed++;
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE android_tester_requests SET status = 'pending', invited_at = NULL WHERE id = {id}", CancellationToken.None);
        }

        return Ok(new { sent, failed });
    }

    public static string? NormalizeEmail(string? value)
    {
        var email = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || email.Length > 254) return null;
        // La comparación con Address rechaza nombres visibles, listas y saltos de línea.
        return MailAddress.TryCreate(email, out var parsed) && parsed.Address == email && parsed.Host.Contains('.')
            ? email
            : null;
    }

    private static string InvitationBody() =>
        "<h2>Prueba la app de DietoExpress</h2>" +
        "<p>Tu correo ya está dado de alta en la prueba de nuestra app para Android.</p>" +
        "<ol><li>Desde tu móvil, abre el enlace con la misma cuenta de Google que usas en Google Play.</li>" +
        "<li>Acepta participar en la prueba.</li>" +
        "<li>Instala la app desde Google Play y mantenla instalada al menos 14 días.</li></ol>" +
        $"<p><a href='{OptInUrl}'>Unirme a la prueba</a></p>" +
        "<p>Si el enlace indica que la app no está disponible, espera unas horas y vuelve a intentarlo.</p>";

    private async Task NotifySuperAdminsAsync(string googleEmail, CancellationToken ct)
    {
        var recipients = await _context.users
            .Where(u => u.role == "superadmin" && u.archived_at == null && u.email != null && u.email != "")
            .Select(u => u.email!)
            .ToListAsync(ct);

        // La solicitud ya está guardada: un fallo SMTP no la invalida y queda visible en el panel.
        foreach (var recipient in recipients)
            await _email.SendEmailAsync(recipient, "Nueva solicitud de tester Android",
                $"<p>Hay una nueva solicitud para la prueba de la app Android: <strong>{WebUtility.HtmlEncode(googleEmail)}</strong>.</p>" +
                "<p>Revísala en Administración → Testers Android.</p>");
    }

    private async Task<AndroidTesterStatus?> FindRequestAsync(int clientId, CancellationToken ct)
        => (await _context.Database.SqlQueryRaw<AndroidTesterStatus>("""
            SELECT google_email AS "GoogleEmail", status AS "Status", requested_at AS "RequestedAt", invited_at AS "InvitedAt"
            FROM android_tester_requests WHERE client_id = {0}
            """, clientId).ToListAsync(ct)).SingleOrDefault();

    private Task<clients?> FindAccessibleClientAsync(int clientId, CancellationToken ct)
    {
        var userId = AuthHelpers.GetUserId(User);
        var tenantId = AuthHelpers.GetTenantId(User);
        var isSuperAdmin = User.IsInRole("superadmin");
        var isClinicAdmin = User.IsInRole("clinic_admin");

        return _context.clients.AsNoTracking().FirstOrDefaultAsync(c => c.id == clientId && c.archived_at == null &&
            (isSuperAdmin ||
             (tenantId.HasValue && c.tenant_id == tenantId.Value &&
              (isClinicAdmin ||
               c.user_id == userId ||
               _context.client_nutritionist_assignments.Any(a => a.client_id == c.id && a.nutritionist_id == userId && a.is_active)))), ct);
    }
}

public sealed class AndroidTesterRequestInput
{
    public string? GoogleEmail { get; set; }
    public bool ConsentConfirmed { get; set; }
}

public sealed class AndroidTesterInviteInput
{
    public long[]? Ids { get; set; }
}

public sealed class AndroidTesterStatus
{
    public string GoogleEmail { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime? InvitedAt { get; set; }
}

public sealed class AndroidTesterAdminRow
{
    public long Id { get; set; }
    public string GoogleEmail { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime? InvitedAt { get; set; }
    public string? ClientName { get; set; }
    public string? RequestedBy { get; set; }
}
