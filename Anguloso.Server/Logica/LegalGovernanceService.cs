using System.Data;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

public sealed record LegalRatActivityDto(long Id,string Name,string Purpose,string Role,string? LegalBasis,string? SubjectCategories,string? DataCategories,string? SpecialCategories,string? Recipients,string? InternationalTransfers,string? Retention,string? SecurityMeasures,string? Notes,string Status);
public sealed record LegalRiskAssessmentDto(long Id,string Name,string RiskDescription,int Likelihood,int Impact,string? Measures,string? ResidualRisk,string? Owner,DateTime? ReviewDate,string Status);
public sealed record LegalEipdDecisionDto(long Id,string Decision,string Justification,string? AdditionalMeasures,DateTime DecidedAt,DateTime? ReviewDate,string? DocumentReference);

public sealed record SaveLegalRatActivity(string Name,string Purpose,string Role,string? LegalBasis,string? SubjectCategories,string? DataCategories,string? SpecialCategories,string? Recipients,string? InternationalTransfers,string? Retention,string? SecurityMeasures,string? Notes,string? Status);
public sealed record SaveLegalRisk(string Name,string RiskDescription,int Likelihood,int Impact,string? Measures,string? ResidualRisk,string? Owner,DateTime? ReviewDate,string Status);
public sealed record SaveLegalEipd(string Decision,string Justification,string? AdditionalMeasures,DateTime? ReviewDate,string? DocumentReference);

/// <summary>Gestiona el RAT y la evaluación de riesgos del ámbito legal actual.</summary>
public sealed class LegalGovernanceService
{
    private readonly angulosodbContext _db;
    private readonly ITenantContextService _tenant;
    private readonly IAuditLogService _audit;
    private readonly IHttpContextAccessor _http;
    public LegalGovernanceService(angulosodbContext db,ITenantContextService tenant,IAuditLogService audit,IHttpContextAccessor http){_db=db;_tenant=tenant;_audit=audit;_http=http;}

    public async Task<IReadOnlyList<LegalRatActivityDto>> ListRatAsync(CancellationToken ct)
        => await QueryAsync(@"SELECT id,name,purpose,role,legal_basis,subject_categories,data_categories,special_categories,recipients,international_transfers,retention,security_measures,notes,status FROM legal_rat_activities WHERE scope_type=@scope AND scope_id=@id ORDER BY id",
            MapRat,ct);
    public async Task<long> CreateRatAsync(SaveLegalRatActivity r,CancellationToken ct)
    {
        ValidateRat(r); var id = await ExecuteScalarAsync(@"INSERT INTO legal_rat_activities(scope_type,scope_id,name,purpose,role,legal_basis,subject_categories,data_categories,special_categories,recipients,international_transfers,retention,security_measures,notes,status) VALUES(@scope,@id,@name,@purpose,@role,@basis,@subjects,@data,@special,@recipients,@transfers,@retention,@security,@notes,@status) RETURNING id",r,ct);
        await _audit.LogAccessAsync("CREATE_LEGAL_RAT_ACTIVITY","legal_rat_activities",id.ToString(),null,$"RAT: {r.Name}");
        return id;
    }
    public async Task<bool> UpdateRatAsync(long id,SaveLegalRatActivity r,CancellationToken ct)
    {
        ValidateRat(r); var updated=await ExecuteNonQueryAsync(@"UPDATE legal_rat_activities SET name=@name,purpose=@purpose,role=@role,legal_basis=@basis,subject_categories=@subjects,data_categories=@data,special_categories=@special,recipients=@recipients,international_transfers=@transfers,retention=@retention,security_measures=@security,notes=@notes,status=@status,updated_at=NOW() WHERE id=@row AND scope_type=@scope AND scope_id=@id",r,id,ct)>0;
        if(updated) await _audit.LogAccessAsync("UPDATE_LEGAL_RAT_ACTIVITY","legal_rat_activities",id.ToString(),null,$"RAT: {r.Name}");
        return updated;
    }
    public async Task<bool> DeleteRatAsync(long id,CancellationToken ct)
    {
        var updated=await ExecuteNonQueryAsync(@"UPDATE legal_rat_activities SET status='archived',updated_at=NOW() WHERE id=@row AND scope_type=@scope AND scope_id=@id",null,id,ct)>0;
        if(updated) await _audit.LogAccessAsync("ARCHIVE_LEGAL_RAT_ACTIVITY","legal_rat_activities",id.ToString());
        return updated;
    }

    public async Task<IReadOnlyList<LegalRiskAssessmentDto>> ListRisksAsync(CancellationToken ct)
        => await QueryAsync(@"SELECT id,name,risk_description,likelihood,impact,measures,residual_risk,owner,review_date,status FROM legal_risk_assessments WHERE scope_type=@scope AND scope_id=@id ORDER BY id",MapRisk,ct);
    public async Task<long> CreateRiskAsync(SaveLegalRisk r,CancellationToken ct){ValidateRisk(r);var id=await ExecuteScalarAsync(@"INSERT INTO legal_risk_assessments(scope_type,scope_id,name,risk_description,likelihood,impact,measures,residual_risk,owner,review_date,status) VALUES(@scope,@id,@name,@description,@likelihood,@impact,@measures,@residual,@owner,@review,@status) RETURNING id",r,ct);await _audit.LogAccessAsync("CREATE_LEGAL_RISK","legal_risk_assessments",id.ToString(),null,$"Riesgo: {r.Name}");return id;}
    public async Task<bool> UpdateRiskAsync(long id,SaveLegalRisk r,CancellationToken ct){ValidateRisk(r);var updated=await ExecuteNonQueryAsync(@"UPDATE legal_risk_assessments SET name=@name,risk_description=@description,likelihood=@likelihood,impact=@impact,measures=@measures,residual_risk=@residual,owner=@owner,review_date=@review,status=@status,updated_at=NOW() WHERE id=@row AND scope_type=@scope AND scope_id=@id",r,id,ct)>0;if(updated)await _audit.LogAccessAsync("UPDATE_LEGAL_RISK","legal_risk_assessments",id.ToString(),null,$"Riesgo: {r.Name}");return updated;}

    public async Task<IReadOnlyList<LegalEipdDecisionDto>> ListEipdAsync(CancellationToken ct)
        => await QueryAsync(@"SELECT id,decision,justification,additional_measures,decided_at,review_date,document_reference FROM legal_eipd_decisions WHERE scope_type=@scope AND scope_id=@id ORDER BY decided_at DESC",MapEipd,ct);
    public async Task<long> CreateEipdAsync(SaveLegalEipd r,CancellationToken ct)
    {
        if(!new[]{"required","not_required","pending"}.Contains(r.Decision))throw new ArgumentException("Decisión EIPD no válida.");
        if(string.IsNullOrWhiteSpace(r.Justification))throw new ArgumentException("La justificación de la decisión EIPD es obligatoria.");
        var (scope, scopeId) = Scope();
        var userId = _tenant.UserId;
        var db = _db.Database.GetDbConnection();
        await using var command = db.CreateCommand();
        command.CommandText = @"INSERT INTO legal_eipd_decisions(scope_type,scope_id,decision,justification,additional_measures,review_date,document_reference,created_by) VALUES(@scope,@id,@decision,@justification,@measures,@review,@reference,@user) RETURNING id";
        Add(command,"scope",scope); Add(command,"id",scopeId); Add(command,"decision",r.Decision); Add(command,"justification",r.Justification); Add(command,"measures",r.AdditionalMeasures); Add(command,"review",r.ReviewDate); Add(command,"reference",r.DocumentReference); Add(command,"user",(object?)userId ?? DBNull.Value);
        if(db.State!=ConnectionState.Open) await db.OpenAsync(ct);
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        await _audit.LogAccessAsync("CREATE_LEGAL_EIPD_DECISION","legal_eipd_decisions",id.ToString(),null,$"EIPD: {r.Decision}");
        return id;
    }

    private (string scope,int id) Scope(){if(_http.HttpContext?.User.IsInRole("superadmin")==true)return("platform",1);if(_tenant.TenantId is int t)return("tenant",t);if(_tenant.UserId is int u)return("user",u);throw new InvalidOperationException("No hay ámbito legal autenticado.");}
    private async Task<List<T>> QueryAsync<T>(string sql,Func<IDataRecord,T> map,CancellationToken ct){var(s,id)=Scope();var db=_db.Database.GetDbConnection();await using var c=db.CreateCommand();c.CommandText=sql;Add(c,"scope",s);Add(c,"id",id);if(db.State!=ConnectionState.Open)await db.OpenAsync(ct);await using var rd=await c.ExecuteReaderAsync(ct);var list=new List<T>();while(await rd.ReadAsync(ct))list.Add(map(rd));return list;}
    private async Task<long> ExecuteScalarAsync<T>(string sql,T? r,CancellationToken ct){var(s,id)=Scope();var db=_db.Database.GetDbConnection();await using var c=db.CreateCommand();c.CommandText=sql;Add(c,"scope",s);Add(c,"id",id);if(r is not null)Bind(c,r);if(db.State!=ConnectionState.Open)await db.OpenAsync(ct);return Convert.ToInt64(await c.ExecuteScalarAsync(ct));}
    private async Task<int> ExecuteNonQueryAsync(string sql,object? r,long row,CancellationToken ct){var(s,id)=Scope();var db=_db.Database.GetDbConnection();await using var c=db.CreateCommand();c.CommandText=sql;Add(c,"scope",s);Add(c,"id",id);Add(c,"row",row);if(r is not null)Bind(c,r);if(db.State!=ConnectionState.Open)await db.OpenAsync(ct);return await c.ExecuteNonQueryAsync(ct);}
    private static void Bind(IDbCommand c,object r){foreach(var p in r.GetType().GetProperties()){var n=p.Name switch{"RiskDescription"=>"description","LegalBasis"=>"basis","SubjectCategories"=>"subjects","DataCategories"=>"data","SpecialCategories"=>"special","InternationalTransfers"=>"transfers","SecurityMeasures"=>"security","ResidualRisk"=>"residual","AdditionalMeasures"=>"measures","DocumentReference"=>"reference","ReviewDate"=>"review","Likelihood"=>"likelihood","Impact"=>"impact","Name"=>"name","Purpose"=>"purpose","Role"=>"role","Recipients"=>"recipients","Retention"=>"retention","Notes"=>"notes","Status"=>"status","Decision"=>"decision","Justification"=>"justification","Owner"=>"owner",_=>p.Name};var q=c.CreateParameter();q.ParameterName="@"+n;q.Value=p.GetValue(r)??DBNull.Value;c.Parameters.Add(q);} }
    private static IDbCommand Add(IDbCommand c,string n,object? v){var p=c.CreateParameter();p.ParameterName="@"+n;p.Value=v??DBNull.Value;c.Parameters.Add(p);return c;}
    private static LegalRatActivityDto MapRat(IDataRecord r)=>new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),N(r,4),N(r,5),N(r,6),N(r,7),N(r,8),N(r,9),N(r,10),N(r,11),N(r,12),r.GetString(13));
    private static LegalRiskAssessmentDto MapRisk(IDataRecord r)=>new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetInt32(4),N(r,5),N(r,6),N(r,7),r.IsDBNull(8)?null:r.GetDateTime(8),r.GetString(9));
    private static LegalEipdDecisionDto MapEipd(IDataRecord r)=>new(r.GetInt64(0),r.GetString(1),r.GetString(2),N(r,3),r.GetDateTime(4),r.IsDBNull(5)?null:r.GetDateTime(5),N(r,6));
    private static string? N(IDataRecord r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
    private static void ValidateRat(SaveLegalRatActivity r){if(string.IsNullOrWhiteSpace(r.Name)||string.IsNullOrWhiteSpace(r.Purpose))throw new ArgumentException("Nombre y finalidad son obligatorios.");if(!new[]{"controller","processor","joint_controller"}.Contains(r.Role))throw new ArgumentException("Rol de tratamiento no válido.");}
    private static void ValidateRisk(SaveLegalRisk r){if(string.IsNullOrWhiteSpace(r.Name)||string.IsNullOrWhiteSpace(r.RiskDescription))throw new ArgumentException("Nombre y descripción del riesgo son obligatorios.");if(r.Likelihood is <1 or >5||r.Impact is <1 or >5)throw new ArgumentException("Probabilidad e impacto deben estar entre 1 y 5.");}
}