using System.Security.Cryptography;
using System.Text;
using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
public class PatientDocumentsController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly IConfiguration _configuration;
    private readonly AutomationService _automationService;
    private readonly PatientDocumentService _patientDocumentService;
    private const long MaxFileSize = 20 * 1024 * 1024;

    public PatientDocumentsController(angulosodbContext context, IConfiguration configuration, AutomationService automationService, PatientDocumentService patientDocumentService)
    {
        _context = context;
        _configuration = configuration;
        _automationService = automationService;
        _patientDocumentService = patientDocumentService;
    }

    [HttpGet("api/clients/{clientId:int}/documents")]
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> ListForProfessional(int clientId)
    {
        if (!await CanAccessClientAsync(clientId)) return NotFound();

        await _patientDocumentService.CreateRequiredDocumentsAsync(
            GetTenantId() ?? 0,
            clientId,
            AuthHelpers.GetUserId(User),
            forClientCreation: false,
            includeAllRequired: true,
            HttpContext.RequestAborted);

        var rows = await _context.Database.SqlQueryRaw<PatientDocumentDto>(
            """
            SELECT id AS "Id", name AS "Name", document_type AS "DocumentType",
                   status AS "Status", version AS "Version",
                   requires_signature AS "RequiresSignature",
                   signed_at AS "SignedAt", viewed_at AS "ViewedAt",
                   original_file_name AS "OriginalFileName",
                   mime_type AS "MimeType", file_size AS "FileSize",
                   sha256 AS "Sha256", created_at AS "CreatedAt", updated_at AS "UpdatedAt"
            FROM patient_documents
            WHERE client_id = {0} AND tenant_id = {1}
            ORDER BY created_at DESC, id DESC
            """,
            clientId, GetTenantId() ?? 0).ToListAsync();

        return Ok(rows);
    }

    [HttpGet("api/clients/{clientId:int}/documents/summary")]
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> GetSummaryForProfessional(int clientId)
    {
        if (!await CanAccessClientAsync(clientId)) return NotFound();
        var tenantId = GetTenantId();
        if (!tenantId.HasValue) return Forbid();

        var summary = await _context.Database.SqlQueryRaw<DocumentSummaryDto>(
            """
            SELECT
                COUNT(*)::int AS "Total",
                COUNT(*) FILTER (WHERE requires_signature = true)::int AS "Required",
                COUNT(*) FILTER (WHERE requires_signature = true AND status = 'signed')::int AS "Accepted",
                COUNT(*) FILTER (WHERE requires_signature = true AND status = 'pending')::int AS "Pending",
                COUNT(*) FILTER (WHERE revoked_at IS NULL)::int AS "Active"
            FROM patient_documents
            WHERE tenant_id = {0} AND client_id = {1} AND revoked_at IS NULL
            """, tenantId.Value, clientId).SingleAsync();

        return Ok(new
        {
            total = summary.Total,
            required = summary.Required,
            accepted = summary.Accepted,
            pending = summary.Pending,
            active = summary.Active,
            allRequiredComplete = summary.Pending == 0
        });
    }

    [HttpPost("api/clients/{clientId:int}/documents")]
    [Authorize(Policy = "Professional")]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> UploadForProfessional(int clientId, IFormFile file,
        [FromForm] string? name = null, [FromForm] string? documentType = null,
        [FromForm] bool requiresSignature = false)
    {
        if (!await CanAccessClientAsync(clientId)) return NotFound();
        if (file == null || file.Length == 0) return BadRequest("Debes seleccionar un archivo.");
        if (file.Length > MaxFileSize) return BadRequest("El documento no puede superar los 20 MB.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".pdf") return BadRequest("En esta primera versión solo se admiten documentos PDF.");
        if (!await LooksLikePdfAsync(file)) return BadRequest("El archivo no parece ser un PDF válido.");

        var tenantId = GetTenantId();
        if (!tenantId.HasValue) return Forbid();

        var safeName = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileNameWithoutExtension(file.FileName)
            : name.Trim();
        if (safeName.Length > 255) return BadRequest("El nombre del documento es demasiado largo.");

        var storageKey = $"{tenantId.Value}/{clientId}/{Guid.NewGuid():N}.pdf";
        var root = GetStorageRoot();
        var physicalPath = Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        string hash;
        await using (var input = file.OpenReadStream())
        await using (var output = new FileStream(physicalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            using var sha = SHA256.Create();
            var buffer = new byte[81920];
            int read;
            long total = 0;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                total += read;
                if (total > MaxFileSize) throw new InvalidOperationException("El archivo supera el límite.");
                await output.WriteAsync(buffer.AsMemory(0, read));
                sha.TransformBlock(buffer, 0, read, null, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            hash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }

        var userId = AuthHelpers.GetUserId(User);
        var docType = string.IsNullOrWhiteSpace(documentType) ? "other" : documentType.Trim().ToLowerInvariant();
        var status = requiresSignature ? "pending" : "available";

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var id = await _context.Database.SqlQueryRaw<long>(
                """
                INSERT INTO patient_documents
                    (tenant_id, client_id, name, document_type, status, version,
                     requires_signature, storage_key, original_file_name, mime_type,
                     file_size, sha256, created_by_user_id)
                VALUES ({0}, {1}, {2}, {3}, {4}, 1, {5}, {6}, {7}, 'application/pdf', {8}, {9}, {10})
                RETURNING id AS "Value"
                """,
                tenantId.Value, clientId, safeName, docType, status, requiresSignature,
                storageKey, Path.GetFileName(file.FileName), file.Length, hash, userId).SingleAsync();

            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO patient_document_events
                    (tenant_id, patient_document_id, client_id, event_type, user_agent, details)
                VALUES ({tenantId.Value}, {id}, {clientId}, 'uploaded',
                        {Request.Headers.UserAgent.ToString()},
                        'Documento subido por el profesional.')
                """);

            await transaction.CommitAsync();
            return Ok(new { id, name = safeName, status, sha256 = hash });
        }
        catch
        {
            await transaction.RollbackAsync();
            try { System.IO.File.Delete(physicalPath); } catch { }
            throw;
        }
    }

    [HttpGet("api/clients/{clientId:int}/documents/{documentId:long}")]
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> DownloadForProfessional(int clientId, long documentId)
    {
        if (!await CanAccessClientAsync(clientId)) return NotFound();
        return await DownloadAsync(clientId, documentId, false);
    }

    [HttpGet("api/clients/{clientId:int}/documents/{documentId:long}/audit")]
    [Authorize(Policy = "Professional")]
    public async Task<IActionResult> GetAuditForProfessional(int clientId, long documentId)
    {
        if (!await CanAccessClientAsync(clientId)) return NotFound();

        var tenantId = GetTenantId();
        if (!tenantId.HasValue) return Forbid();

        var documentExists = await _context.Database.SqlQueryRaw<int>(
            """
            SELECT 1 AS "Value"
            FROM patient_documents
            WHERE id = {0} AND tenant_id = {1} AND client_id = {2}
            LIMIT 1
            """,
            documentId, tenantId.Value, clientId).SingleOrDefaultAsync();

        if (documentExists == 0) return NotFound();

        var events = await _context.Database.SqlQueryRaw<PatientDocumentAuditDto>(
            """
            SELECT id AS "Id",
                   event_type AS "EventType",
                   occurred_at AS "OccurredAt",
                   ip_address AS "IpAddress",
                   user_agent AS "UserAgent",
                   details AS "Details"
            FROM patient_document_events
            WHERE tenant_id = {0} AND client_id = {1} AND patient_document_id = {2}
            ORDER BY occurred_at ASC, id ASC
            """,
            tenantId.Value, clientId, documentId).ToListAsync();

        return Ok(events);
    }

    [HttpGet("api/portal/documents")]
    [Authorize]
    public async Task<IActionResult> ListForPatient()
    {
        if (!AuthHelpers.IsPatient(User)) return Forbid();
        var clientId = AuthHelpers.GetClientId(User);
        if (!clientId.HasValue) return Unauthorized();

        var tenantId = await _context.clients.Where(c => c.id == clientId.Value && c.archived_at == null)
            .Select(c => c.tenant_id).SingleOrDefaultAsync();
        if (!tenantId.HasValue) return NotFound();

        await _patientDocumentService.CreateRequiredDocumentsAsync(
            tenantId.Value,
            clientId.Value,
            AuthHelpers.GetUserId(User),
            forClientCreation: false,
            includeAllRequired: true,
            HttpContext.RequestAborted);

        var rows = await _context.Database.SqlQueryRaw<PatientDocumentDto>(
            """
            SELECT id AS "Id", name AS "Name", document_type AS "DocumentType",
                   status AS "Status", version AS "Version",
                   requires_signature AS "RequiresSignature",
                   signed_at AS "SignedAt", viewed_at AS "ViewedAt",
                   original_file_name AS "OriginalFileName",
                   mime_type AS "MimeType", file_size AS "FileSize",
                   sha256 AS "Sha256", created_at AS "CreatedAt", updated_at AS "UpdatedAt"
            FROM patient_documents
            WHERE client_id = {0} AND tenant_id = {1} AND revoked_at IS NULL
            ORDER BY created_at DESC, id DESC
            """,
            clientId.Value, tenantId.Value).ToListAsync();

        return Ok(rows);
    }

    [HttpPost("api/portal/documents/{documentId:long}/accept")]
    [Authorize]
    public async Task<IActionResult> AcceptForPatient(long documentId)
    {
        if (!AuthHelpers.IsPatient(User)) return Forbid();
        var clientId = AuthHelpers.GetClientId(User);
        var tenantId = GetTenantId();
        if (!clientId.HasValue || !tenantId.HasValue) return Unauthorized();

        var documentSnapshot = await _context.Database.SqlQueryRaw<DocumentAcceptanceSnapshot>(
            """
            SELECT version AS "Version", sha256 AS "Sha256"
            FROM patient_documents
            WHERE id = {0} AND client_id = {1} AND tenant_id = {2}
              AND revoked_at IS NULL AND requires_signature = true
            LIMIT 1
            """,
            documentId, clientId.Value, tenantId.Value).SingleOrDefaultAsync();

        if (documentSnapshot == null) return NotFound();

        var affected = await _context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE patient_documents
            SET status='signed', signed_at=NOW(), updated_at=NOW()
            WHERE id={documentId} AND client_id={clientId.Value} AND tenant_id={tenantId.Value}
              AND revoked_at IS NULL AND requires_signature=true AND status <> 'signed';
            """);

        if (affected == 0)
        {
            var exists = await _context.Database.SqlQueryRaw<int>(
                "SELECT 1 AS "Value" FROM patient_documents WHERE id = {0} AND client_id = {1} AND tenant_id = {2} AND revoked_at IS NULL",
                documentId, clientId.Value, tenantId.Value).SingleOrDefaultAsync();
            if (exists == 0) return NotFound();
            return Conflict(new { message = "El documento no requiere firma o ya ha sido aceptado." });
        }

        await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO patient_document_events
                (tenant_id, patient_document_id, client_id, event_type, ip_address, user_agent, details)
            VALUES ({tenantId.Value}, {documentId}, {clientId.Value}, 'accepted',
                    {HttpContext.Connection.RemoteIpAddress?.ToString()},
                    {Request.Headers.UserAgent.ToString()},
                    { $"Aceptación realizada por el paciente desde el portal. Versión {documentSnapshot.Version}; SHA-256 {documentSnapshot.Sha256}." });
            """);

        // Cuando se completa el último documento obligatorio, dejamos una tarea idempotente
        // al profesional asignado. El worker resuelve el destinatario al ejecutar el job.
        var pending = await _context.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*)::int AS "Value"
            FROM patient_documents
            WHERE tenant_id = {0} AND client_id = {1}
              AND revoked_at IS NULL AND requires_signature = true AND status = 'pending'
            """, tenantId.Value, clientId.Value).SingleAsync();

        if (pending == 0)
        {
            // Los recordatorios de 24h/72h dejan de ser válidos en cuanto se completa
            // la documentación. La cancelación es idempotente y está limitada al tenant.
            await _automationService.CancelPendingDocumentReminderJobsAsync(
                tenantId.Value,
                clientId.Value,
                HttpContext.RequestAborted);

            var assignedUserId = await _context.Database.SqlQueryRaw<int?>(
                """
                SELECT nutritionist_id
                FROM client_nutritionist_assignments
                WHERE client_id = {0} AND is_active = true
                ORDER BY assigned_at DESC, id DESC
                LIMIT 1
                """, clientId.Value).SingleOrDefaultAsync();

            if (assignedUserId.HasValue)
            {
                await _automationService.ScheduleActionAsync(
                    tenantId.Value,
                    "create_professional_task",
                    new CreateTaskAction(
                        clientId.Value,
                        assignedUserId.Value,
                        "Documentación completada",
                        "El paciente ha completado toda la documentación obligatoria.",
                        null,
                        "normal",
                        "automation:documents.completed"),
                    DateTime.UtcNow,
                    idempotencyKey: $"documents:completed:{clientId.Value}");
            }
        }

        return NoContent();
    }

    [HttpGet("api/portal/documents/{documentId:long}")]
    [Authorize]
    public async Task<IActionResult> DownloadForPatient(long documentId)
    {
        if (!AuthHelpers.IsPatient(User)) return Forbid();
        var clientId = AuthHelpers.GetClientId(User);
        if (!clientId.HasValue) return Unauthorized();
        return await DownloadAsync(clientId.Value, documentId, true);
    }

    private async Task<IActionResult> DownloadAsync(int clientId, long documentId, bool patientAccess)
    {
        var row = await _context.Database.SqlQueryRaw<DocumentStorageDto>(
            """
            SELECT id AS "Id", client_id AS "ClientId", tenant_id AS "TenantId",
                   storage_key AS "StorageKey", original_file_name AS "OriginalFileName",
                   mime_type AS "MimeType", name AS "Name"
            FROM patient_documents
            WHERE id = {0} AND client_id = {1} AND revoked_at IS NULL
            LIMIT 1
            """,
            documentId, clientId).SingleOrDefaultAsync();

        if (row == null) return NotFound();

        var path = GetSafePhysicalPath(row.StorageKey, row.TenantId, row.ClientId);
        if (path == null || !System.IO.File.Exists(path)) return NotFound();

        if (patientAccess)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE patient_documents
                SET viewed_at = COALESCE(viewed_at, NOW()), updated_at = NOW()
                WHERE id = {row.Id} AND client_id = {row.ClientId} AND tenant_id = {row.TenantId};

                INSERT INTO patient_document_events
                    (tenant_id, patient_document_id, client_id, event_type, ip_address, user_agent)
                VALUES ({row.TenantId}, {row.Id}, {row.ClientId}, 'viewed',
                        {HttpContext.Connection.RemoteIpAddress?.ToString()},
                        {Request.Headers.UserAgent.ToString()});
                """);
        }

        return PhysicalFile(path, row.MimeType, row.OriginalFileName ?? row.Name);
    }

    private async Task<bool> CanAccessClientAsync(int clientId)
    {
        var userId = AuthHelpers.GetUserId(User);
        if (!userId.HasValue) return false;
        if (User.IsInRole("superadmin")) return await _context.clients.AnyAsync(c => c.id == clientId && c.archived_at == null);

        var tenantId = GetTenantId();
        if (!tenantId.HasValue) return false;

        return await _context.clients.AnyAsync(c => c.id == clientId && c.archived_at == null &&
            c.tenant_id == tenantId.Value &&
            (c.user_id == userId.Value ||
             User.IsInRole("clinic_admin") ||
             _context.client_nutritionist_assignments.Any(a =>
                 a.client_id == c.id && a.nutritionist_id == userId.Value && a.is_active)));
    }

    private int? GetTenantId() => AuthHelpers.GetTenantId(User);

    private string GetStorageRoot()
    {
        var configured = _configuration["DIETOEXPRESS_DOCUMENTS_PATH"];
        var root = string.IsNullOrWhiteSpace(configured) ? "/var/lib/dietoexpress/documents" : configured;
        Directory.CreateDirectory(root);
        return Path.GetFullPath(root);
    }

    private string? GetSafePhysicalPath(string storageKey, int tenantId, int clientId)
    {
        var root = GetStorageRoot();
        var expectedPrefix = $"{tenantId}/{clientId}/";
        if (!storageKey.StartsWith(expectedPrefix, StringComparison.Ordinal)) return null;
        if (storageKey.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(storageKey)) return null;

        var path = Path.GetFullPath(Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        var fullRoot = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(fullRoot, StringComparison.Ordinal) ? path : null;
    }

    private static async Task<bool> LooksLikePdfAsync(IFormFile file)
    {
        await using var stream = file.OpenReadStream();
        var header = new byte[5];
        var read = await stream.ReadAsync(header);
        return read == 5 && Encoding.ASCII.GetString(header) == "%PDF-";
    }

    private sealed class DocumentAcceptanceSnapshot
    {
        public int Version { get; set; }
        public string Sha256 { get; set; } = "";
    }

    private sealed class DocumentSummaryDto
    {
        public int Total { get; set; }
        public int Required { get; set; }
        public int Accepted { get; set; }
        public int Pending { get; set; }
        public int Active { get; set; }
    }

    private sealed class PatientDocumentAuditDto
    {
        public long Id { get; set; }
        public string EventType { get; set; } = "";
        public DateTime OccurredAt { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? Details { get; set; }
    }

    public sealed class PatientDocumentDto
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string DocumentType { get; set; } = "other";
        public string Status { get; set; } = "pending";
        public int Version { get; set; }
        public bool RequiresSignature { get; set; }
        public DateTime? SignedAt { get; set; }
        public DateTime? ViewedAt { get; set; }
        public string? OriginalFileName { get; set; }
        public string MimeType { get; set; } = "application/pdf";
        public long FileSize { get; set; }
        public string Sha256 { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private sealed class DocumentStorageDto
    {
        public long Id { get; set; }
        public int ClientId { get; set; }
        public int TenantId { get; set; }
        public string StorageKey { get; set; } = "";
        public string? OriginalFileName { get; set; }
        public string MimeType { get; set; } = "application/pdf";
        public string Name { get; set; } = "";
    }
}
