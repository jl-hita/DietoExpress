using System.Text.Json;
using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class ExerciseCatalogCorrectionsTests
{
    private static string SeedPath
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;
            return Path.Combine(directory!.FullName, "Anguloso.Server", "ExerciseCuration", ExerciseCatalogCorrectionsSeeder.FileName);
        }
    }

    [Fact]
    public void CorrectionsFile_EveryEntryIsValidForTheGeneratorAndIdsAreUnique()
    {
        var entries = JsonSerializer.Deserialize<List<ExerciseCatalogCorrectionsSeeder.Entry>>(File.ReadAllText(SeedPath))!;
        Assert.NotEmpty(entries);
        foreach (var e in entries)
            Assert.True(ExerciseCatalogCorrectionsSeeder.Validate(e) == null, $"{e.Id}: {ExerciseCatalogCorrectionsSeeder.Validate(e)}");
        Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void RawSourceVocabulary_IsInvalidForTheGenerator_AndTheCorrectedVocabularyIsValid()
    {
        // Valores reales del servidor antes de la corrección: el generador los descartaba en silencio.
        Assert.False(ExerciseCatalogCorrectionsSeeder.MetadataIsValidForGenerator(
            "HorizontalPull", "[\"latissimus_dorsi\",\"rhomboids\"]", "[\"biceps_brachii\"]", "[\"hypertrophy\",\"strength\"]"));
        Assert.False(ExerciseCatalogCorrectionsSeeder.MetadataIsValidForGenerator(
            "Cardio", "[\"Lats\"]", "[]", "[\"GeneralFitness\"]"));
        Assert.True(ExerciseCatalogCorrectionsSeeder.MetadataIsValidForGenerator(
            "HorizontalPull", "[\"Lats\",\"UpperBack\"]", "[\"Biceps\",\"Deltoids\"]", "[\"Hypertrophy\",\"Strength\"]"));
    }

    [Fact]
    public void BrokenCatalogEntries_AreFixedByTheCorrections()
    {
        var entries = JsonSerializer.Deserialize<List<ExerciseCatalogCorrectionsSeeder.Entry>>(File.ReadAllText(SeedPath))!
            .ToDictionary(e => e.Id);
        foreach (var id in new[] { "barbell-row", "inverted-row", "cable-reverse-fly" })
        {
            var e = entries[id];
            Assert.True(ExerciseCatalogCorrectionsSeeder.MetadataIsValidForGenerator(
                e.MovementPattern, JsonSerializer.Serialize(e.PrimaryMuscles), JsonSerializer.Serialize(e.SecondaryMuscles), JsonSerializer.Serialize(e.SuitableGoals)), id);
        }
    }

    [Theory]
    [InlineData("latissimus_dorsi", "Lats", "músculo")]
    public void Validate_RejectsUnknownMuscles(string muscle, string _, string expected)
    {
        var e = new ExerciseCatalogCorrectionsSeeder.Entry { Id = "x", PrimaryMuscles = new[] { muscle } };
        Assert.Contains(expected, ExerciseCatalogCorrectionsSeeder.Validate(e));
    }

    [Fact]
    public void Validate_RequiresFullMetadataToApproveAndRejectsBadValues()
    {
        Assert.Contains("aprobación", ExerciseCatalogCorrectionsSeeder.Validate(new() { Id = "x", ReviewStatus = "approved", MovementPattern = "Accessory" }));
        Assert.Contains("patrón", ExerciseCatalogCorrectionsSeeder.Validate(new() { Id = "x", MovementPattern = "Cardio" }));
        Assert.Contains("objetivo", ExerciseCatalogCorrectionsSeeder.Validate(new() { Id = "x", SuitableGoals = new[] { "endurance" } }));
        Assert.Null(ExerciseCatalogCorrectionsSeeder.Validate(new() { Id = "x", NameEs = "Solo nombre" }));
    }
}
