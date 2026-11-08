using System.Text.Json;
using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class ExerciseCatalogAdditionsTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;
            return directory!.FullName;
        }
    }

    private static List<ExerciseCatalogAdditionsSeeder.Entry> Load() =>
        JsonSerializer.Deserialize<List<ExerciseCatalogAdditionsSeeder.Entry>>(File.ReadAllText(
            Path.Combine(RepoRoot, "Anguloso.Server", "ExerciseCuration", ExerciseCatalogAdditionsSeeder.FileName)))!;

    [Fact]
    public void AdditionsFile_EveryEntryIsValidForTheGeneratorAndIdsAreUnique()
    {
        var entries = Load();
        Assert.NotEmpty(entries);
        foreach (var e in entries)
            Assert.True(ExerciseCatalogAdditionsSeeder.Validate(e) == null, $"{e.Id}: {ExerciseCatalogAdditionsSeeder.Validate(e)}");
        Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void AdditionsFile_CoversTheEssentialMissingExercises()
    {
        var ids = Load().Select(e => e.Id).ToHashSet();
        foreach (var essential in new[] { "dumbbell-curl", "parallel-bar-dips", "plank", "walking-lunge", "sumo-deadlift", "close-grip-bench-press", "lying-leg-raise" })
            Assert.Contains(essential, ids);
    }

    [Fact]
    public void AdditionsFile_DoesNotCollideWithRepDbIdsAlreadyInTheCatalog()
    {
        // Los ids existentes en el catálogo RepDB del servidor; una colisión haría que el upsert no insertase la fila.
        var existing = new HashSet<string> { "barbell-curl", "cable-curl", "hammer-curl", "push-up", "side-plank", "reverse-lunge", "deadlift", "bench-press", "hanging-knee-raise" };
        Assert.DoesNotContain(Load(), e => existing.Contains(e.Id));
    }

    [Fact]
    public void Validate_RejectsBadIdsMuscleVocabularyAndTooFewInstructions()
    {
        var good = Load()[0];
        Assert.Null(ExerciseCatalogAdditionsSeeder.Validate(good));
        Assert.Contains("id", ExerciseCatalogAdditionsSeeder.Validate(Clone(good, e => e.Id = "Bad Id"))!);
        Assert.Contains("músculo", ExerciseCatalogAdditionsSeeder.Validate(Clone(good, e => e.PrimaryMuscles = new[] { "latissimus_dorsi" }))!);
        Assert.Contains("instrucciones", ExerciseCatalogAdditionsSeeder.Validate(Clone(good, e => e.InstructionsEs = new[] { "Solo una" }))!);
        Assert.Contains("objetivo", ExerciseCatalogAdditionsSeeder.Validate(Clone(good, e => e.SuitableGoals = new[] { "endurance" }))!);
    }

    private static ExerciseCatalogAdditionsSeeder.Entry Clone(ExerciseCatalogAdditionsSeeder.Entry source, Action<ExerciseCatalogAdditionsSeeder.Entry> change)
    {
        var copy = JsonSerializer.Deserialize<ExerciseCatalogAdditionsSeeder.Entry>(JsonSerializer.Serialize(source))!;
        change(copy);
        return copy;
    }
}
