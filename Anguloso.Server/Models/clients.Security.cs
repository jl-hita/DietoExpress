#nullable enable
namespace Anguloso.Server.Models;

// Protección frente a fuerza bruta del PIN del portal. Fuera del archivo generado por EF Power Tools para que no se pierda al regenerarlo.
// El esquema lo crea DatabaseBootstrap.UpgradeAccountSecuritySchemaV1.
public partial class clients
{
    /// <summary>Fallos consecutivos de PIN desde el último acceso correcto.</summary>
    public int portal_failed_attempts { get; set; }

    /// <summary>Mientras sea posterior a ahora, el acceso con PIN se rechaza aunque el PIN sea correcto.</summary>
    public DateTime? portal_locked_until { get; set; }
}
