namespace Anguloso.Server.Logica.Utils;

/// <summary>Utilidades para búsquedas de texto con ILIKE.</summary>
public static class SqlSearch
{
    /// <summary>
    /// Escapa los comodines de LIKE/ILIKE (<c>\</c>, <c>%</c> y <c>_</c>) para que el texto escrito por el usuario se busque
    /// literalmente. Sin esto, un usuario que escribe «%» o «_» fuerza coincidencias con todo y evita los índices.
    /// </summary>
    public static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>Patrón «contiene» seguro: <c>%término%</c> con los comodines del usuario escapados.</summary>
    public static string Contains(string term) => $"%{EscapeLike(term.Trim())}%";
}
