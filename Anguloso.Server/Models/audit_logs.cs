using System;

namespace Anguloso.Server.Models;

public partial class audit_logs
{
    public long id { get; set; }

    public int? tenant_id { get; set; }

    public int? user_id { get; set; }

    public string? user_role { get; set; }

    public string action { get; set; } = string.Empty; // e.g. READ_RECORD, CREATE, UPDATE, DELETE, EXPORT_PDF

    public string entity_name { get; set; } = string.Empty; // e.g. clients, medical_history, biometrics

    public string? entity_id { get; set; }

    public int? client_id { get; set; } // Paciente afectado para trazabilidad médica

    public string? ip_address { get; set; }

    public string? user_agent { get; set; }

    public string? details { get; set; }

    public DateTime created_at { get; set; } = DateTime.UtcNow;

    public virtual tenants? tenant { get; set; }

    public virtual users? user { get; set; }

    public virtual clients? client { get; set; }
}
