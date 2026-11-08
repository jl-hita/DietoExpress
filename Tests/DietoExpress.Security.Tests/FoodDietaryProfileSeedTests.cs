using System.Text.Json;
using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class FoodDietaryProfileSeedTests
{
    private static string SeedPath
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;
            return Path.Combine(directory!.FullName, "Anguloso.Server", "FoodCuration", FoodDietaryProfileSeeder.FileName);
        }
    }

    [Fact]
    public void SeedFile_EveryEntryHonoursTheProfileContractAndKeysAreUnique()
    {
        var entries = JsonSerializer.Deserialize<List<FoodDietaryProfileSeeder.Entry>>(File.ReadAllText(SeedPath))!;
        Assert.NotEmpty(entries);
        foreach (var e in entries)
            Assert.True(FoodDietaryProfileSeeder.Validate(e) == null, $"{e.Source}/{e.ExternalId}: {FoodDietaryProfileSeeder.Validate(e)}");
        Assert.Equal(entries.Count, entries.Select(e => (e.Source, e.ExternalId)).Distinct().Count());
    }

    [Theory]
    [InlineData(true, false, "", "vegano implica vegetariano")]
    [InlineData(true, true, "lacteos", "vegano incompatible con alérgenos de origen animal")]
    [InlineData(false, true, "inventado", "alérgeno desconocido 'inventado'")]
    public void Validate_RejectsContradictoryEntries(bool vegan, bool vegetarian, string allergen, string expected)
    {
        var e = new FoodDietaryProfileSeeder.Entry { Source = "bedca", ExternalId = "x", Vegan = vegan, Vegetarian = vegetarian, Allergens = allergen == "" ? Array.Empty<string>() : new[] { allergen } };
        Assert.Equal(expected, FoodDietaryProfileSeeder.Validate(e));
    }
}
