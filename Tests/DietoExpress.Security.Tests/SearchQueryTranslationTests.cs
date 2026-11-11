using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DietoExpress.Security.Tests;

public class SearchQueryTranslationTests
{
    // ToQueryString genera el SQL sin abrir conexión, así que comprueba la traducción real de EF a PostgreSQL.
    private static angulosodbContext NewContext() =>
        new(new DbContextOptionsBuilder<angulosodbContext>().UseNpgsql("Host=localhost;Database=none;Username=x;Password=x").Options);

    [Fact]
    public void FoodSearch_TranslatesToTheIndexedExpression()
    {
        using var db = NewContext();
        var pattern = "%jamon%";
        var sql = db.foods.Where(f => EF.Functions.ILike(SqlFunctions.ImmutableUnaccent(f.name), SqlFunctions.ImmutableUnaccent(pattern))).ToQueryString();

        Assert.Contains("immutable_unaccent(f.name)", sql);
        Assert.Contains("ILIKE immutable_unaccent(", sql);
    }

    [Fact]
    public void PatientSearch_TranslatesNameWithUnaccentAndOtherColumnsPlain()
    {
        using var db = NewContext();
        var pattern = "%ana%";
        var sql = db.clients.Where(c =>
            EF.Functions.ILike(SqlFunctions.ImmutableUnaccent(c.full_name), SqlFunctions.ImmutableUnaccent(pattern)) ||
            EF.Functions.ILike(c.email, pattern) ||
            EF.Functions.ILike(c.phone, pattern)).ToQueryString();

        Assert.Contains("immutable_unaccent(c.full_name)", sql);
        Assert.Contains("c.email ILIKE", sql);
        Assert.Contains("c.phone ILIKE", sql);
    }
}
