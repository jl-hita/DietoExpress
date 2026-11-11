#nullable enable
namespace Anguloso.Server.Models;

// Campos de seguridad de la cuenta (bloqueo progresivo y segundo factor). Se mantienen fuera del archivo generado por EF Power Tools
// para que una regeneración del modelo no los elimine. El esquema lo crea DatabaseBootstrap.UpgradeAccountSecuritySchemaV1.
public partial class users
{
    /// <summary>Intentos fallidos consecutivos de contraseña o código 2FA.</summary>
    public int failed_login_count { get; set; }

    /// <summary>Mientras sea posterior a ahora, la cuenta rechaza inicios de sesión aunque la contraseña sea correcta.</summary>
    public DateTime? locked_until { get; set; }

    /// <summary>Secreto TOTP cifrado con AES-GCM (ver TwoFactorCrypto). Nunca debe serializarse hacia el cliente.</summary>
    public string? totp_secret_encrypted { get; set; }

    public bool totp_enabled { get; set; }

    public DateTime? totp_enabled_at { get; set; }

    /// <summary>Último paso TOTP aceptado; impide reutilizar un código dentro de su ventana de validez.</summary>
    public long? totp_last_step { get; set; }
}
