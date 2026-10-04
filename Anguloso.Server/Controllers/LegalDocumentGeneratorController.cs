using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Anguloso.Server.Logica.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/legal-documents/generator")]
[Authorize(Policy = "Professional")]
public sealed class LegalDocumentGeneratorController : ControllerBase
{
    private readonly IConfiguration _configuration;
    public LegalDocumentGeneratorController(IConfiguration configuration) => _configuration = configuration;

    private static readonly Dictionary<string,string> Titles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["01-aviso-legal"]="Aviso legal",["02-privacidad-dietoexpress"]="Política de privacidad de DietoExpress",
        ["03-terminos-saas"]="Términos y condiciones de DietoExpress",["04-politica-cookies"]="Política de cookies",
        ["05-dpa-encargo-tratamiento"]="Acuerdo de encargo del tratamiento",["06-informacion-privacidad-paciente"]="Información de privacidad para pacientes",
        ["07-consentimiento-informado-paciente"]="Consentimiento informado del paciente",["08-condiciones-economicas"]="Condiciones económicas",
        ["09-rat-minimo"]="Registro de Actividades de Tratamiento",["10-matriz-conservacion"]="Matriz de conservación y supresión",
        ["11-analisis-riesgos-eipd"]="Análisis de riesgos y decisión EIPD"
    };

    [HttpGet("templates")]
    public IActionResult Templates() => Ok(Titles.Select(x => new { key=x.Key, title=x.Value }));

    [HttpGet] public async Task<IActionResult> List(CancellationToken ct)
    {
        var scope=GetScope(); if(scope==null)return Unauthorized();
        await using var c=new NpgsqlConnection(ConnectionString); await c.OpenAsync(ct);
        await using var q=new NpgsqlCommand("""SELECT id,template_key,version,title,status,sha256,generated_at,content FROM legal_generated_documents WHERE scope_type=@s AND scope_id=@i ORDER BY generated_at DESC;""",c);
        q.Parameters.AddWithValue("s",scope.Value.type);q.Parameters.AddWithValue("i",scope.Value.id);
        await using var rd=await q.ExecuteReaderAsync(ct);var list=new List<object>();
        while(await rd.ReadAsync(ct)){var content=rd.GetString(7);var pending=Regex.Matches(content,@"\{\{([a-zA-Z0-9_.-]+)\}\}").Select(m=>m.Groups[1].Value).Distinct().ToArray();
            list.Add(new{id=rd.GetInt64(0),templateKey=rd.GetString(1),version=rd.GetInt32(2),title=rd.GetString(3),status=rd.GetString(4),sha256=rd.GetString(5),generatedAt=rd.GetDateTime(6),hasUnresolvedPlaceholders=pending.Length>0,unresolved=pending});}
        return Ok(list);
    }

    [HttpGet("{id:long}")] public async Task<IActionResult> Get(long id,CancellationToken ct)
    {
        var scope=GetScope();if(scope==null)return Unauthorized();
        await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync(ct);
        await using var q=new NpgsqlCommand("""SELECT id,template_key,version,title,content,status,sha256,generated_at FROM legal_generated_documents WHERE id=@id AND scope_type=@s AND scope_id=@i;""",c);
        q.Parameters.AddWithValue("id",id);q.Parameters.AddWithValue("s",scope.Value.type);q.Parameters.AddWithValue("i",scope.Value.id);
        await using var rd=await q.ExecuteReaderAsync(ct);if(!await rd.ReadAsync(ct))return NotFound();
        var content=rd.GetString(4);var pending=Regex.Matches(content,@"\{\{([a-zA-Z0-9_.-]+)\}\}").Select(m=>m.Groups[1].Value).Distinct().ToArray();
        return Ok(new{id=rd.GetInt64(0),templateKey=rd.GetString(1),version=rd.GetInt32(2),title=rd.GetString(3),content,status=rd.GetString(5),sha256=rd.GetString(6),generatedAt=rd.GetDateTime(7),unresolved=pending});
    }

    [HttpPut("{id:long}")] public async Task<IActionResult> Update(long id,[FromBody] UpdateGeneratedDocumentRequest request,CancellationToken ct)
    {
        var scope=GetScope();if(scope==null)return Unauthorized();if(request==null||string.IsNullOrWhiteSpace(request.Content))return BadRequest("El contenido es obligatorio.");
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Content))).ToLowerInvariant();
        await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync(ct);
        await using var q=new NpgsqlCommand("""UPDATE legal_generated_documents SET content=@content,sha256=@sha,updated_at=NOW(),status='draft' WHERE id=@id AND scope_type=@s AND scope_id=@i AND status<>'published' RETURNING id;""",c);
        q.Parameters.AddWithValue("id",id);q.Parameters.AddWithValue("s",scope.Value.type);q.Parameters.AddWithValue("i",scope.Value.id);q.Parameters.AddWithValue("content",request.Content);q.Parameters.AddWithValue("sha",hash);
        var result=await q.ExecuteScalarAsync(ct);return result==null?NotFound():Ok(new{id,sha256=hash,status="draft"});
    }

    [HttpPost("{id:long}/publish")] public async Task<IActionResult> Publish(long id,CancellationToken ct)
    {
        if(!User.IsInRole("superadmin"))return Forbid();
        var scope=("platform",1);
        await using var c=new NpgsqlConnection(ConnectionString);await c.OpenAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
        await using var q=new NpgsqlCommand("""SELECT template_key,title,content,version FROM legal_generated_documents WHERE id=@id AND scope_type='platform' AND scope_id=1 AND status='draft' FOR UPDATE;""",c,tx);
        q.Parameters.AddWithValue("id",id);await using var rd=await q.ExecuteReaderAsync(ct);if(!await rd.ReadAsync(ct))return NotFound();
        var key=rd.GetString(0);var title=rd.GetString(1);var content=rd.GetString(2);var sourceVersion=rd.GetInt32(3);await rd.CloseAsync();
        var unresolved=Regex.Matches(content,@"\{\{([a-zA-Z0-9_.-]+)\}\}").Select(m=>m.Groups[1].Value).Distinct().ToArray();
        if(unresolved.Length>0)return Conflict(new{message="El documento contiene placeholders sin resolver.",unresolved});
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        await using var ins=new NpgsqlCommand("""INSERT INTO legal_documents(document_key,version,title,document_type,content,status,effective_from,sha256,created_at,published_at) VALUES(@key,(SELECT COALESCE(MAX(version),0)+1 FROM legal_documents WHERE document_key=@key),@title,'legal',@content,'published',NOW(),@sha,NOW(),NOW()) RETURNING id,version;""",c,tx);
        ins.Parameters.AddWithValue("key",key);ins.Parameters.AddWithValue("title",title);ins.Parameters.AddWithValue("content",content);ins.Parameters.AddWithValue("sha",hash);
        await using var published=await ins.ExecuteReaderAsync(ct);await published.ReadAsync(ct);var publishedId=published.GetInt64(0);var publishedVersion=published.GetInt32(1);await published.CloseAsync();
        await using var mark=new NpgsqlCommand("""UPDATE legal_generated_documents SET status='published',updated_at=NOW() WHERE id=@id;""",c,tx);mark.Parameters.AddWithValue("id",id);await mark.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);return Ok(new{generatedDocumentId=id,legalDocumentId=publishedId,version=publishedVersion,sourceVersion});
    }

    private (string type,int id)? GetScope()
    {
        var uid=AuthHelpers.GetUserId(User);if(!uid.HasValue)return null;
        if(User.IsInRole("superadmin"))return("platform",1);
        var tenant=AuthHelpers.GetTenantId(User);return User.IsInRole("clinic_admin")&&tenant.HasValue?("tenant",tenant.Value):("user",uid.Value);
    }
    private string ConnectionString=>_configuration.GetConnectionString("DefaultConnection")??throw new InvalidOperationException("DefaultConnection no está configurada.");
}
public sealed record UpdateGeneratedDocumentRequest(string Content);
