using System.Net;
using Npgsql;

namespace Anguloso.Server.Logica;

public sealed class SupportEnhancementService
{
    private readonly string _cs;
    private readonly EmailServ _email;
    private readonly ILogger<SupportEnhancementService> _logger;

    public SupportEnhancementService(IConfiguration c, EmailServ email, ILogger<SupportEnhancementService> logger)
    { _cs=c.GetConnectionString("DefaultConnection")??throw new InvalidOperationException("DefaultConnection no está configurada."); _email=email; _logger=logger; }

    public async Task NotifyAsync(long ticketId,int actor,bool internalNote)
    {
        if(internalNote)return;
        try{
            await using var c=new NpgsqlConnection(_cs);await c.OpenAsync();
            await using var q=new NpgsqlCommand("""SELECT t.subject,t.created_by_user_id,t.assigned_to_user_id,t.tenant_id,m.id,m.body FROM support_tickets t JOIN LATERAL(SELECT id,body FROM support_messages WHERE ticket_id=t.id AND is_internal=FALSE ORDER BY id DESC LIMIT 1)m ON TRUE WHERE t.id=@t;""",c);
            q.Parameters.AddWithValue("t",ticketId);await using var r=await q.ExecuteReaderAsync();if(!await r.ReadAsync())return;
            var subject=r.GetString(0);var creator=r.GetInt32(1);var assigned=r.IsDBNull(2)?(int?)null:r.GetInt32(2);var tenantId=r.GetInt32(3);var mid=r.GetInt64(4);var body=r.GetString(5);await r.CloseAsync();
            var ids=new List<int>();
            if(actor==creator)
            {
                if(assigned.HasValue) ids.Add(assigned.Value);
                else
                {
                    await using var clinic=new NpgsqlCommand("SELECT id FROM users WHERE role='clinic_admin' AND tenant_id=@tenant AND archived_at IS NULL;",c);
                    clinic.Parameters.AddWithValue("tenant",tenantId);
                    await using var cr=await clinic.ExecuteReaderAsync();
                    while(await cr.ReadAsync()) ids.Add(cr.GetInt32(0));
                    await cr.CloseAsync();
                    if(ids.Count==0)
                    {
                        await using var a=new NpgsqlCommand("SELECT id FROM users WHERE role='superadmin' AND archived_at IS NULL AND id<>@u;",c);
                        a.Parameters.AddWithValue("u",actor);
                        await using var ar=await a.ExecuteReaderAsync();
                        while(await ar.ReadAsync()) ids.Add(ar.GetInt32(0));
                    }
                }
            }
            else ids.Add(creator);
            foreach(var id in ids.Distinct()){
                var title=actor==creator?"Nuevo mensaje de soporte":"Nueva respuesta de soporte";var msg=actor==creator?$"Hay actividad nueva en «{subject}».":$"El equipo de DietoExpress ha respondido a «{subject}».";
                var key=$"support:{mid}:{id}";
                await using var n=new NpgsqlCommand("""INSERT INTO support_notifications(recipient_user_id,ticket_id,type,title,message,action_url,idempotency_key) VALUES(@u,@t,'support_message',@title,@m,@url,@key) ON CONFLICT(recipient_user_id,idempotency_key) DO NOTHING RETURNING id;""",c);
                n.Parameters.AddWithValue("u",id);n.Parameters.AddWithValue("t",ticketId);n.Parameters.AddWithValue("title",title);n.Parameters.AddWithValue("m",msg);n.Parameters.AddWithValue("url",$"/support?ticket={ticketId}");n.Parameters.AddWithValue("key",key);
                if(await n.ExecuteScalarAsync()!=null){
                    await using var e=new NpgsqlCommand("SELECT COALESCE(email,'') FROM users WHERE id=@u;",c);e.Parameters.AddWithValue("u",id);var email=Convert.ToString(await e.ExecuteScalarAsync());
                    if(!string.IsNullOrWhiteSpace(email))await _email.SendEmailAsync(email,$"{title}: {subject}",$"<p>{WebUtility.HtmlEncode(msg)}</p><p>{WebUtility.HtmlEncode(body)}</p>",key);
                }
            }
        }catch(Exception ex){_logger.LogWarning(ex,"No se pudo notificar soporte.");}
    }

    public async Task<int> UnreadAsync(int userId){await using var c=new NpgsqlConnection(_cs);await c.OpenAsync();await using var q=new NpgsqlCommand("SELECT COUNT(*) FROM support_notifications WHERE recipient_user_id=@u AND read_at IS NULL;",c);q.Parameters.AddWithValue("u",userId);return Convert.ToInt32(await q.ExecuteScalarAsync());}
    public async Task<IReadOnlyList<SupportNotificationDto>> ListAsync(int userId){var x=new List<SupportNotificationDto>();await using var c=new NpgsqlConnection(_cs);await c.OpenAsync();await using var q=new NpgsqlCommand("SELECT id,ticket_id,type,title,message,action_url,created_at,read_at FROM support_notifications WHERE recipient_user_id=@u ORDER BY created_at DESC,id DESC LIMIT 50;",c);q.Parameters.AddWithValue("u",userId);await using var r=await q.ExecuteReaderAsync();while(await r.ReadAsync())x.Add(new(){Id=r.GetInt64(0),TicketId=r.GetInt64(1),Type=r.GetString(2),Title=r.GetString(3),Message=r.GetString(4),ActionUrl=r.IsDBNull(5)?null:r.GetString(5),CreatedAt=r.GetDateTime(6),ReadAt=r.IsDBNull(7)?null:r.GetDateTime(7)});return x;}
    public async Task ReadAsync(int userId,long id){await using var c=new NpgsqlConnection(_cs);await c.OpenAsync();await using var q=new NpgsqlCommand("UPDATE support_notifications SET read_at=COALESCE(read_at,NOW()) WHERE id=@id AND recipient_user_id=@u;",c);q.Parameters.AddWithValue("id",id);q.Parameters.AddWithValue("u",userId);await q.ExecuteNonQueryAsync();}

    public async Task<IReadOnlyList<SupportAuditDto>> GetAuditAsync(long ticketId)
    {
        var result = new List<SupportAuditDto>();
        await using var c = new NpgsqlConnection(_cs); await c.OpenAsync();
        await using var q = new NpgsqlCommand("SELECT a.id,a.actor_user_id,u.full_name,a.action,a.old_value,a.new_value,a.created_at FROM support_ticket_audit a JOIN users u ON u.id=a.actor_user_id WHERE a.ticket_id=@t ORDER BY a.created_at,a.id;", c);
        q.Parameters.AddWithValue("t", ticketId);
        await using var r = await q.ExecuteReaderAsync();
        while (await r.ReadAsync()) result.Add(new SupportAuditDto { Id=r.GetInt64(0), ActorUserId=r.GetInt32(1), ActorName=r.GetString(2), Action=r.GetString(3), OldValue=r.IsDBNull(4)?null:r.GetString(4), NewValue=r.IsDBNull(5)?null:r.GetString(5), CreatedAt=r.GetDateTime(6) });
        return result;
    }

    public async Task NotifyTicketChangeAsync(long ticketId,int actorUserId,SupportTicketChangeResult change)
    {
        if(!change.StatusChanged&&!change.PriorityChanged&&!change.AssignmentChanged)return;
        try{
            await using var c=new NpgsqlConnection(_cs);await c.OpenAsync();
            await using var q=new NpgsqlCommand("SELECT subject,created_by_user_id FROM support_tickets WHERE id=@t;",c);q.Parameters.AddWithValue("t",ticketId);
            await using var r=await q.ExecuteReaderAsync();if(!await r.ReadAsync())return;
            var subject=r.GetString(0);var creator=r.GetInt32(1);await r.CloseAsync();
            var recipients=new HashSet<int>{creator};if(change.AssignedToUserId.HasValue)recipients.Add(change.AssignedToUserId.Value);
            foreach(var recipient in recipients.Where(x=>x!=actorUserId)){
                var title=recipient==change.AssignedToUserId?"Ticket asignado":"Ticket actualizado";
                var key="support-change:"+ticketId+":"+recipient+":"+DateTime.UtcNow.ToString("yyyyMMddHHmm");
                await using var n=new NpgsqlCommand("INSERT INTO support_notifications(recipient_user_id,ticket_id,type,title,message,action_url,idempotency_key) VALUES(@u,@t,'support_ticket_change',@title,@m,@url,@key) ON CONFLICT(recipient_user_id,idempotency_key) DO NOTHING;",c);
                n.Parameters.AddWithValue("u",recipient);n.Parameters.AddWithValue("t",ticketId);n.Parameters.AddWithValue("title",title);
                n.Parameters.AddWithValue("m","El ticket «"+subject+"» ha sido actualizado.");n.Parameters.AddWithValue("url","/support?ticket="+ticketId);n.Parameters.AddWithValue("key",key);await n.ExecuteNonQueryAsync();
            }
        }catch(Exception ex){_logger.LogWarning(ex,"No se pudo notificar un cambio de ticket.");}
    }

    public async Task ReopenAsync(long ticketId,int userId,int tenantId){await using var c=new NpgsqlConnection(_cs);await c.OpenAsync();await using var q=new NpgsqlCommand("UPDATE support_tickets SET status='open',closed_at=NULL,updated_at=NOW() WHERE id=@id AND tenant_id=@tenant AND created_by_user_id=@u AND status IN('resolved','closed') RETURNING id;",c);q.Parameters.AddWithValue("id",ticketId);q.Parameters.AddWithValue("tenant",tenantId);q.Parameters.AddWithValue("u",userId);if(await q.ExecuteScalarAsync()==null)throw new KeyNotFoundException();await using var a=new NpgsqlCommand("INSERT INTO support_ticket_audit(ticket_id,actor_user_id,action,old_value,new_value) VALUES(@t,@u,'reopened','resolved/closed','open');",c);a.Parameters.AddWithValue("t",ticketId);a.Parameters.AddWithValue("u",userId);await a.ExecuteNonQueryAsync();}
    public async Task AuditAsync(long ticketId,int userId,string action,string? oldValue,string? newValue){await using var c=new NpgsqlConnection(_cs);await c.OpenAsync();await using var q=new NpgsqlCommand("INSERT INTO support_ticket_audit(ticket_id,actor_user_id,action,old_value,new_value) VALUES(@t,@u,@a,@o,@n);",c);q.Parameters.AddWithValue("t",ticketId);q.Parameters.AddWithValue("u",userId);q.Parameters.AddWithValue("a",action);q.Parameters.AddWithValue("o",(object?)oldValue??DBNull.Value);q.Parameters.AddWithValue("n",(object?)newValue??DBNull.Value);await q.ExecuteNonQueryAsync();}
}
public sealed class SupportNotificationDto{public long Id{get;set;}public long TicketId{get;set;}public string Type{get;set;}="";public string Title{get;set;}="";public string Message{get;set;}="";public string? ActionUrl{get;set;}public DateTime CreatedAt{get;set;}public DateTime? ReadAt{get;set;}}
public sealed class SupportAuditDto { public long Id{get;set;} public int ActorUserId{get;set;} public string ActorName{get;set;}=""; public string Action{get;set;}=""; public string? OldValue{get;set;} public string? NewValue{get;set;} public DateTime CreatedAt{get;set;} }
