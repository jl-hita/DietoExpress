namespace Anguloso.Server.Models;

/// <summary>
/// Funciones SQL propias para consultas EF. <c>unaccent</c> de PostgreSQL no es IMMUTABLE y por eso no se puede indexar;
/// <c>immutable_unaccent</c> (creada en DatabaseBootstrap.UpgradePerformanceSchemaV1) la envuelve para poder usarla en índices.
/// </summary>
public static class SqlFunctions
{
    /// <summary>Quita las tildes (jamón → jamon). Solo se traduce a SQL; no se puede llamar desde C#.</summary>
    public static string ImmutableUnaccent(string value) =>
        throw new NotSupportedException("immutable_unaccent solo se puede usar dentro de una consulta EF.");
}
