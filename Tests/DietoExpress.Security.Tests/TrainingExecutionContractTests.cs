using System.Text.Json;
using Anguloso.Server.Controllers;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class TrainingExecutionContractTests
{
    [Fact]
    public void PatientTrainingExerciseFeedback_PreservesPerSetLoadRepsRirAndSetKind()
    {
        var feedback = new PatientTrainingExerciseFeedback(
            "barbell-bench-press",
            "Press de banca",
            PlannedSets: 2,
            CompletedSets: 2,
            Sets:
            [
                new PatientTrainingSetFeedback(0, "warmup", 20m, 12, 5m, true),
                new PatientTrainingSetFeedback(1, "working", 60m, 10, 2m, true),
                new PatientTrainingSetFeedback(2, "working", 60m, 9, 1m, true)
            ]);

        var json = JsonSerializer.Serialize(feedback, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"plannedSets\":2", json);
        Assert.Contains("\"kind\":\"warmup\"", json);
        Assert.Contains("\"weightKg\":60", json);
        Assert.Contains("\"repetitions\":10", json);
        Assert.Contains("\"rir\":2", json);
        Assert.Contains("\"completed\":true", json);
    }

    [Fact]
    public void TrainingPortal_ValidatesSetMetadataBeforePersistingJsonbFeedback()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Anguloso.Server", "Program.cs")))
            root = root.Parent;
        Assert.NotNull(root);

        var controller = File.ReadAllText(Path.Combine(root!.FullName, "Anguloso.Server", "Controllers", "TrainingPortalController.cs"));
        Assert.Contains("[Idempotent]", controller);
        Assert.Contains("CurrentTransaction?.GetDbTransaction()", controller);
        Assert.Contains("PatientTrainingSetFeedback", controller);
        Assert.Contains("set.WeightKg is < 0 or > 1000", controller);
        Assert.Contains("set.Repetitions is < 0 or > 1000", controller);
        Assert.Contains("set.Rir is < 0 or > 10", controller);
        Assert.Contains("completedWorkingSets != exercise.CompletedSets", controller);
        Assert.Contains("exercise_feedback = COALESCE(EXCLUDED.exercise_feedback", controller);
    }
}
