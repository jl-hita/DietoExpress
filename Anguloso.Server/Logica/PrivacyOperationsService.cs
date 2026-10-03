using System.Data;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

public sealed record PrivacyRequestDto(long Id, string RequesterType, int? ClientId, string RightType, string Status, DateTime ReceivedAt, DateTime? DueAt, DateTime? ResolvedAt, string? Decision, string? Notes);
public sealed record PrivacyIncidentDto(long Id, string Status, DateTime DetectedAt, DateTime? OccurredFrom, DateTime? OccurredTo, string? SystemsAffected, string? DataCategories, string? SubjectCategories, string Description, string? Containment, string? RiskAssessment, string? Communications, string? CorrectiveActions, DateTime? ClosedAt);

public sealed record CreatePrivacyRequest(string RequesterType, int? ClientId, string RightType, DateTime? DueAt, string? Notes);
public sealed record UpdatePrivacyRequest(string Status, string? Decision, string? Notes);
public sealed record CreatePrivacyIncident(string Description, DateTime? OccurredFrom, DateTime? OccurredTo, string? SystemsAffected, string? DataCategories, string? SubjectCategories, string? Containment, string? RiskAssessment);
public sealed record UpdatePrivacyIncident(string Status, string? Containment, string? RiskAssessment, string? Communications, string? CorrectiveActions);

/// <summary>Gestiona expedientes administrativos RGPD sin duplicar ni almacenar el contenido clínico en los registros de cumplimiento.</summary>
public sealed class PrivacyOperationsService
{
    private readonly angulosodbContext _context;
    private readonly ITenantContextService _tenant;
    private readonly IAuditLogService _audit;

    public PrivacyOperationsService(angulosodbContext context, ITenantContextService tenant, IAuditLogService audit)
    {
        _context = context;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task<IReadOnlyList<PrivacyRequestDto>> ListRequestsAsync(CancellationToken ct)
    {
        var tenantId = RequireTenant();
        return await QueryAsync<PrivacyRequestDto>(
            @"SELECT id, requester_type, client_id, right_type, status, received_at, due_at, resolved_at, decision, notes
              FROM privacy_requests WHERE tenant_id = @tenant ORDER BY received_at DESC",
            tenantId, (r) => new PrivacyRequestDto(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetDateTime(5), r.IsDBNull(6) ? null : r.GetDateTime(6), r.IsDBNull(7) ? null : r.GetDateTime(7), r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9)), ct);
    }

    public async Task<long?> CreateRequestAsync(CreatePrivacyRequest request, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        ValidateRequest(request.RequesterType, request.RightType);
        await EnsureClientBelongsToTenantAsync(request.ClientId, tenantId, ct);

        var id = await ExecuteScalarAsync<long?>(@"INSERT INTO privacy_requests
            (tenant_id, requester_type, client_id, right_type, due_at, notes, created_by, updated_by)
            VALUES (@tenant, @requester, @client, @right, @due, @notes, @user, @user)
            RETURNING id", tenantId, cmd => Add(cmd, request, tenantId), ct);

        await _audit.LogAccessAsync("CREATE_PRIVACY_REQUEST", "privacy_requests", id?.ToString(), request.ClientId, $"RGPD: {request.RightType}");
        return id;
    }

    public async Task<bool> UpdateRequestAsync(long id, UpdatePrivacyRequest request, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        if (!new[] { "received", "verifying", "in_progress", "awaiting_client", "resolved", "rejected", "cancelled" }.Contains(request.Status))
            throw new ArgumentException("Estado de solicitud RGPD no válido.");

        var resolved = request.Status is "resolved" or "rejected" or "cancelled";
        var count = await ExecuteNonQueryAsync(@"UPDATE privacy_requests SET status=@status, decision=@decision, notes=@notes,
            resolved_at=CASE WHEN @resolved THEN COALESCE(resolved_at, NOW()) ELSE NULL END, updated_by=@user
            WHERE id=@id AND tenant_id=@tenant", tenantId, cmd => {
                Add(cmd, "status", request.Status); Add(cmd, "decision", request.Decision); Add(cmd, "notes", request.Notes);
                Add(cmd, "resolved", resolved); Add(cmd, "user", _tenant.UserId); Add(cmd, "id", id); return cmd;
            }, ct);
        if (count == 0) return false;
        await _audit.LogAccessAsync("UPDATE_PRIVACY_REQUEST", "privacy_requests", id.ToString(), null, $"RGPD solicitud: {request.Status}");
        return true;
    }

    public async Task<IReadOnlyList<PrivacyIncidentDto>> ListIncidentsAsync(CancellationToken ct)
    {
        var tenantId = RequireTenant();
        return await QueryAsync<PrivacyIncidentDto>(
            @"SELECT id,status,detected_at,occurred_from,occurred_to,systems_affected,data_categories,subject_categories,description,containment,risk_assessment,communications,corrective_actions,closed_at
              FROM privacy_incidents WHERE tenant_id=@tenant ORDER BY detected_at DESC",
            tenantId, r => new PrivacyIncidentDto(r.GetInt64(0),r.GetString(1),r.GetDateTime(2),r.IsDBNull(3)?null:r.GetDateTime(3),r.IsDBNull(4)?null:r.GetDateTime(4),r.IsDBNull(5)?null:r.GetString(5),r.IsDBNull(6)?null:r.GetString(6),r.IsDBNull(7)?null:r.GetString(7),r.GetString(8),r.IsDBNull(9)?null:r.GetString(9),r.IsDBNull(10)?null:r.GetString(10),r.IsDBNull(11)?null:r.GetString(11),r.IsDBNull(12)?null:r.GetString(12),r.IsDBNull(13)?null:r.GetDateTime(13)), ct);
    }

    public async Task<long> CreateIncidentAsync(CreatePrivacyIncident request, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        if (string.IsNullOrWhiteSpace(request.Description)) throw new ArgumentException("La descripción del incidente es obligatoria.");
        var id = await ExecuteScalarAsync<long>(@"INSERT INTO privacy_incidents
            (tenant_id, description, occurred_from, occurred_to, systems_affected, data_categories, subject_categories, containment, risk_assessment, created_by, updated_by)
            VALUES (@tenant,@description,@from,@to,@systems,@categories,@subjects,@containment,@risk,@user,@user) RETURNING id",
            tenantId, cmd => {
                Add(cmd,"description",request.Description); Add(cmd,"from",request.OccurredFrom); Add(cmd,"to",request.OccurredTo); Add(cmd,"systems",request.SystemsAffected);
                Add(cmd,"categories",request.DataCategories); Add(cmd,"subjects",request.SubjectCategories); Add(cmd,"containment",request.Containment); Add(cmd,"risk",request.RiskAssessment); Add(cmd,"user",_tenant.UserId); return cmd;
            }, ct);
        await _audit.LogAccessAsync("CREATE_PRIVACY_INCIDENT", "privacy_incidents", id.ToString(), null, "Incidente de privacidad registrado");
        return id;
    }

    public async Task<bool> UpdateIncidentAsync(long id, UpdatePrivacyIncident request, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        if (!new[] { "detected", "contained", "assessing", "notified", "remediating", "closed", "false_positive" }.Contains(request.Status))
            throw new ArgumentException("Estado de incidente no válido.");
        var closed = request.Status is "closed" or "false_positive";
        var count = await ExecuteNonQueryAsync(@"UPDATE privacy_incidents SET status=@status, containment=@containment, risk_assessment=@risk,
            communications=@communications, corrective_actions=@actions, closed_at=CASE WHEN @closed THEN COALESCE(closed_at,NOW()) ELSE NULL END, updated_by=@user
            WHERE id=@id AND tenant_id=@tenant", tenantId, cmd => {
                Add(cmd,"status",request.Status); Add(cmd,"containment",request.Containment); Add(cmd,"risk",request.RiskAssessment); Add(cmd,"communications",request.Communications);
                Add(cmd,"actions",request.CorrectiveActions); Add(cmd,"closed",closed); Add(cmd,"user",_tenant.UserId); Add(cmd,"id",id); return cmd;
            }, ct);
        if (count == 0) return false;
        await _audit.LogAccessAsync("UPDATE_PRIVACY_INCIDENT", "privacy_incidents", id.ToString(), null, $"Incidente de privacidad: {request.Status}");
        return true;
    }

    public async Task<object?> ExportClientAsync(int clientId, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        var client = await _context.clients.AsNoTracking()
            .Include(c => c.biometrics)
            .Include(c => c.medical_history)
            .Include(c => c.digestive_health)
            .Include(c => c.food_preferences)
            .Include(c => c.lifestyle_history)
            .Include(c => c.client_diets)
            .FirstOrDefaultAsync(c => c.id == clientId && c.tenant_id == tenantId, ct);
        if (client == null) return null;

        // La exportación se limita al tenant y devuelve una fotografía de los datos operativos conocidos; no incluye secretos de autenticación.
        await _audit.LogAccessAsync("EXPORT_PATIENT_DATA", "clients", clientId.ToString(), clientId, "Exportación de datos del expediente");
        return new
        {
            exportedAt = DateTime.UtcNow,
            client = new
            {
                client.id, client.full_name, client.email, client.phone, client.birth_date, client.gender,
                client.notes, client.created_at, client.archived_at, client.lifecycle_status,
                client.onboarding_consent_at, client.onboarding_consent_version
            },
            biometrics = client.biometrics,
            medicalHistory = client.medical_history,
            digestiveHealth = client.digestive_health,
            foodPreferences = client.food_preferences,
            lifestyleHistory = client.lifestyle_history,
            diets = client.client_diets,
            documents = await QueryClientAsync("SELECT id,name,document_type,status,version,requires_signature,original_file_name,mime_type,file_size,sha256,created_at,updated_at,revoked_at FROM patient_documents WHERE client_id=@client AND tenant_id=@tenant ORDER BY created_at,id",
                tenantId, clientId, r => new { Id = r.GetInt64(0), Name = r.GetString(1), DocumentType = r.GetString(2), Status = r.GetString(3), Version = r.GetInt32(4), RequiresSignature = r.GetBoolean(5), OriginalFileName = r.IsDBNull(6) ? null : r.GetString(6), MimeType = r.IsDBNull(7) ? null : r.GetString(7), FileSize = r.IsDBNull(8) ? null : r.GetInt64(8), Sha256 = r.IsDBNull(9) ? null : r.GetString(9), CreatedAt = r.GetDateTime(10), UpdatedAt = r.GetDateTime(11), RevokedAt = r.IsDBNull(12) ? null : r.GetDateTime(12) }),
            appointments = await QueryClientAsync("SELECT id,starts_at,ends_at,status,professional_notes,created_at FROM patient_appointments WHERE client_id=@client AND tenant_id=@tenant ORDER BY starts_at,id",
                tenantId, clientId, r => new { Id = r.GetInt64(0), StartsAt = r.GetDateTime(1), EndsAt = r.IsDBNull(2) ? null : r.GetDateTime(2), Status = r.GetString(3), ProfessionalNotes = r.IsDBNull(4) ? null : r.GetString(4), CreatedAt = r.GetDateTime(5) }),
            messages = await QueryClientAsync("SELECT id,conversation_id,sender_user_id,sender_client_id,body,created_at FROM patient_messages WHERE client_id=@client AND tenant_id=@tenant ORDER BY created_at,id",
                tenantId, clientId, r => new { Id = r.GetInt64(0), ConversationId = r.GetInt64(1), SenderUserId = r.IsDBNull(2) ? null : r.GetInt32(2), SenderClientId = r.IsDBNull(3) ? null : r.GetInt32(3), Body = r.GetString(4), CreatedAt = r.GetDateTime(5) }),
            legalAcceptances = client.user_id.HasValue
                ? await QueryUserAsync("SELECT la.document_key,la.document_version,la.document_sha256,la.accepted_at,la.context FROM legal_acceptances la WHERE la.user_id=@user AND la.tenant_id=@tenant ORDER BY la.accepted_at,id",
                    tenantId, client.user_id.Value, r => new { DocumentKey = r.GetString(0), DocumentVersion = r.GetInt32(1), DocumentSha256 = r.GetString(2), AcceptedAt = r.GetDateTime(3), Context = r.GetString(4) })
                : new List<object>()
        };
    }

    public async Task<bool> RequestErasureAsync(long requestId, CancellationToken ct)
    {
        var tenantId = RequireTenant();
        var count = await ExecuteNonQueryAsync(@"UPDATE privacy_requests
            SET status='in_progress', decision='Supresión solicitada: pendiente de revisión de obligaciones de conservación.', updated_by=@user
            WHERE id=@id AND tenant_id=@tenant AND right_type='erasure'",
            tenantId, cmd => { Add(cmd,"id",requestId); Add(cmd,"user",_tenant.UserId); return cmd; }, ct);
        if (count == 0) return false;
        await _audit.LogAccessAsync("REQUEST_PATIENT_ERASURE", "privacy_requests", requestId.ToString(), null, "Solicitud de supresión pendiente de revisión");
        return true;
    }

    private async Task<List<T>> QueryClientAsync<T>(string sql, int tenantId, int clientId, Func<IDataRecord,T> map)
    {
        var db = _context.Database.GetDbConnection();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        Add(cmd,"tenant",tenantId);
        Add(cmd,"client",clientId);
        if (db.State != ConnectionState.Open) await db.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<T>();
        while (await reader.ReadAsync()) list.Add(map(reader));
        return list;
    }

    private async Task<List<T>> QueryUserAsync<T>(string sql, int tenantId, int userId, Func<IDataRecord,T> map)
    {
        var db = _context.Database.GetDbConnection();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        Add(cmd,"tenant",tenantId);
        Add(cmd,"user",userId);
        if (db.State != ConnectionState.Open) await db.OpenAsync();
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<T>();
        while (await reader.ReadAsync()) list.Add(map(reader));
        return list;
    }

    private int RequireTenant() => _tenant.TenantId ?? throw new InvalidOperationException("No hay tenant autenticado.");

    private async Task EnsureClientBelongsToTenantAsync(int? clientId, int tenantId, CancellationToken ct)
    {
        if (!clientId.HasValue) return;
        var exists = await ExecuteScalarAsync<bool>(@"SELECT EXISTS(SELECT 1 FROM clients WHERE id=@client AND tenant_id=@tenant)", tenantId, cmd => { Add(cmd,"client",clientId); return cmd; }, ct);
        if (!exists) throw new KeyNotFoundException("El paciente no pertenece al tenant actual.");
    }

    private async Task<List<T>> QueryAsync<T>(string sql, int tenantId, Func<IDataRecord,T> map, CancellationToken ct)
    {
        var db = _context.Database.GetDbConnection();
        await using var cmd = db.CreateCommand(); cmd.CommandText=sql; Add(cmd,"tenant",tenantId);
        if (db.State != ConnectionState.Open) await db.OpenAsync(ct);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list=new List<T>(); while(await reader.ReadAsync(ct)) list.Add(map(reader)); return list;
    }

    private async Task<T> ExecuteScalarAsync<T>(string sql,int tenantId,Func<IDbCommand,IDbCommand> configure,CancellationToken ct)
    {
        var db=_context.Database.GetDbConnection(); await using var cmd=db.CreateCommand(); cmd.CommandText=sql; Add(cmd,"tenant",tenantId); configure(cmd);
        if(db.State!=ConnectionState.Open) await db.OpenAsync(ct); var value=await cmd.ExecuteScalarAsync(ct); return (T)(value ?? default(T)!);
    }

    private async Task<int> ExecuteNonQueryAsync(string sql,int tenantId,Func<IDbCommand,IDbCommand> configure,CancellationToken ct)
    {
        var db=_context.Database.GetDbConnection(); await using var cmd=db.CreateCommand(); cmd.CommandText=sql; Add(cmd,"tenant",tenantId); configure(cmd);
        if(db.State!=ConnectionState.Open) await db.OpenAsync(ct); return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static IDbCommand Add(IDbCommand cmd,string name,object? value){var p=cmd.CreateParameter();p.ParameterName="@"+name;p.Value=value??DBNull.Value;cmd.Parameters.Add(p);return cmd;}
    private static void ValidateRequest(string requester,string right){if(!new[]{"patient","representative","professional","other"}.Contains(requester))throw new ArgumentException("Tipo de solicitante no válido.");if(!new[]{"access","rectification","erasure","restriction","objection","portability","automated_decision"}.Contains(right))throw new ArgumentException("Derecho RGPD no válido.");}
}
