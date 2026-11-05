using System.IO;
using System.Text.Json.Nodes;
using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class TrainingAdaptationReviewWorkflowTests
{
    [Fact]
    public void ApprovedVolumeReductionChangesStructuredAndLegacyPrescriptionTogether()
    {
        const string json = """{"engineVersion":"0.6.0","exercises":[{"order":1,"exerciseId":"squat","catalogVersion":"v1","exerciseNameSnapshot":"Sentadilla","sets":3,"minRepetitions":8,"maxRepetitions":12,"targetRepetitionsInReserve":2,"restSeconds":120,"progressionRule":1,"primaryMuscleGroups":[11,10],"secondaryMuscleGroups":[12]}]}""";
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryReduceSets(json,
            new HashSet<string> { "squat" },
            out var updatedJson, out var legacy));
        Assert.Equal(2, JsonNode.Parse(updatedJson)!["exercises"]![0]!["sets"]!.GetValue<int>());
        Assert.Contains("2 × 8-12 reps", legacy);
        Assert.Contains("Sentadilla", legacy);
    }

    [Fact]
    public void VolumeReductionNeverDropsAnExerciseBelowOneSet()
    {
        const string json = """{"exercises":[{"order":1,"exerciseId":"plank","exerciseNameSnapshot":"Plancha","sets":1,"minRepetitions":8,"maxRepetitions":12,"restSeconds":60,"primaryMuscleGroups":[7],"secondaryMuscleGroups":[]}]}""";
        Assert.False(TrainingAdaptationPrescriptionAdjuster.TryReduceSets(json,
            new HashSet<string> { "plank" }, out _, out _));
    }

    [Fact]
    public void EmptyMuscleSelectionNeverAppliesGlobalVolumeReduction()
    {
        const string json = """{"exercises":[{"exerciseId":"bench","sets":3,"primaryMuscleGroups":[0],"secondaryMuscleGroups":[]}]}""";
        Assert.False(TrainingAdaptationPrescriptionAdjuster.TryReduceSets(json,
            new HashSet<string>(), out var updated, out _));
        Assert.Equal(json, updated);
    }

    [Fact]
    public void AnExerciseNotSelectedByStableIdIsNeverChanged()
    {
        const string json = """{"exercises":[{"order":1,"exerciseId":"bench","exerciseNameSnapshot":"Press banca","sets":3,"minRepetitions":8,"maxRepetitions":12,"restSeconds":120}]}""";
        Assert.False(TrainingAdaptationPrescriptionAdjuster.TryReduceSets(json,
            new HashSet<string> { "incline" }, out var updated, out _));
        Assert.Equal(json, updated);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"exercises\":[{\"sets\":\"three\"}]}")]
    [InlineData("not-json")]
    public void InvalidOrUnsupportedPrescriptionIsNeverChanged(string json)
    {
        Assert.False(TrainingAdaptationPrescriptionAdjuster.TryReduceSets(json,
            new HashSet<TrainingMuscleGroup> { TrainingMuscleGroup.Chest }, out var updated, out _));
        Assert.Equal(json, updated);
    }

    [Fact]
    public void ReviewWorkflowRequiresExplicitProfessionalDecisionAndOnlyTargetsFutureUnrecordedSessions()
    {
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "TrainingPeriodizationController.cs"));
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));
        var component = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "training-periodization", "training-periodization.component.ts"));

        Assert.Contains("[HttpPost(\"adaptation-proposals/{proposalId:long}/review\")]", controller);
        Assert.Contains("request.Decision is not (\"approved\" or \"rejected\")", controller);
        Assert.Contains("s.scheduled_date > CURRENT_DATE AND s.status = 'planned'", controller);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM training_session_records r WHERE r.session_id = s.id)", controller);
        Assert.Contains("TrainingAdaptationPrescriptionAdjuster.TryReduceSets", controller);
        Assert.Contains("training-periodization-v8-adaptation-review", bootstrap);
        Assert.Contains("Revisión de adaptación por feedback", component);
        Assert.Contains("reviewAdaptationProposal(proposal, 'approved')", component);
        Assert.Contains("reviewAdaptationProposal(proposal, 'rejected')", component);
    }

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }
}
