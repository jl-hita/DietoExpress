namespace Anguloso.Server.Logica;

/// <summary>
/// Turns the lifter's per-exercise answers after a workout (muscle stimulus, joint discomfort, how
/// recovered the muscle was when the session started) into volume decisions. Pure and deterministic.
///
/// Rules, in priority order (internal product values, not guideline thresholds):
///  1. Strong discomfort in the latest exposure of an exercise -> <c>PauseExercise</c> (safety; a person reviews).
///  2. Mild discomfort in 2 of the last 3 exposures of an exercise -> <c>SuggestSwap</c> (informational).
///  3. Per muscle, over its last 2 sessions: every prescribed exercise has explicit LOW stimulus,
///     recovery and no discomfort, and the planned weekly sets remain below the ceiling -> <c>AddSet</c>.
///  4. Per muscle, over its last 3 sessions: HIGH stimulus and pre-session fatigue occur together
///     on an exercise in at least 2 sessions, and the latest session is complete -> <c>ReduceSet</c>.
///  5. Otherwise nothing: missing feedback and incomplete sessions are unknown, not positive evidence.
/// Decisions are per muscle (aggregating its exercises) so two exercises of the same muscle cannot
/// produce contradictory changes.
/// </summary>
public static class TrainingMuscleFeedbackAdapter
{
    public const int MaximumSetsPerExercise = 5;

    public static IReadOnlyList<TrainingMuscleFeedbackDecision> Evaluate(
        IReadOnlyList<TrainingMuscleFeedbackExposure> chronological,
        IReadOnlyDictionary<string, int> plannedWeeklySetsByMuscle,
        IReadOnlyDictionary<string, int> weeklyCeilingByMuscle)
    {
        ArgumentNullException.ThrowIfNull(chronological);
        ArgumentNullException.ThrowIfNull(plannedWeeklySetsByMuscle);
        ArgumentNullException.ThrowIfNull(weeklyCeilingByMuscle);
        var decisions = new List<TrainingMuscleFeedbackDecision>();
        var blockedExercises = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in chronological.GroupBy(item => item.ExerciseId, StringComparer.Ordinal))
        {
            var recent = group.OrderBy(item => item.Date).TakeLast(3).ToArray();
            if (recent.Length == 0)
                continue;

            if (EqualsValue(recent[^1].Discomfort, "strong"))
            {
                blockedExercises.Add(group.Key);
                decisions.Add(new(TrainingMuscleFeedbackAction.PauseExercise, null, group.Key,
                    "Has indicado molestias fuertes en este ejercicio. Se pausa su progresión y debe revisarlo una persona antes de seguir."));
            }
            else if (recent.Count(item => EqualsValue(item.Discomfort, "mild")) >= 2)
            {
                blockedExercises.Add(group.Key);
                decisions.Add(new(TrainingMuscleFeedbackAction.SuggestSwap, null, group.Key,
                    "Has notado molestias leves en este ejercicio en varias sesiones. Prueba una alternativa equivalente con «Cambiar ejercicio»."));
            }
        }

        var byMuscle = chronological
            .Where(item => !blockedExercises.Contains(item.ExerciseId))
            .SelectMany(item => item.PrimaryMuscles.Select(muscle => (Muscle: muscle, Item: item)))
            .GroupBy(pair => pair.Muscle, StringComparer.Ordinal);

        foreach (var muscleGroup in byMuscle.OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var sessions = muscleGroup
                .GroupBy(pair => pair.Item.Date)
                .OrderBy(group => group.Key)
                .Select(group => group.Select(pair => pair.Item).ToArray())
                .ToArray();
            var planned = plannedWeeklySetsByMuscle.GetValueOrDefault(muscleGroup.Key);
            var ceiling = weeklyCeilingByMuscle.GetValueOrDefault(muscleGroup.Key);

            // A muscle is not considered under-stimulated if another exercise for it was adequate,
            // high, unanswered or incomplete. The mapper supplies neutral exposures for missing answers.
            bool Low(TrainingMuscleFeedbackExposure[] day) =>
                day.Length > 0 && day.All(item => item.SessionCompleted && EqualsValue(item.Stimulus, "low"));
            bool Recovered(TrainingMuscleFeedbackExposure[] day) =>
                day.Length > 0 && day.All(item => item.SessionCompleted && EqualsValue(item.PreFatigue, "recovered"));
            bool NoDiscomfort(TrainingMuscleFeedbackExposure[] day) =>
                day.Length > 0 && day.All(item => item.SessionCompleted && EqualsValue(item.Discomfort, "none"));
            bool HighAndLoaded(TrainingMuscleFeedbackExposure[] day) =>
                day.Any(item => item.SessionCompleted &&
                    EqualsValue(item.Stimulus, "high") && EqualsValue(item.PreFatigue, "loaded"));

            var last2 = sessions.TakeLast(2).ToArray();
            if (last2.Length == 2 &&
                last2.All(Low) &&
                last2.All(Recovered) &&
                last2.All(NoDiscomfort) &&
                planned < ceiling)
            {
                var pick = last2[^1]
                    .Where(item => item.Sets < MaximumSetsPerExercise)
                    .OrderBy(item => item.Sets)
                    .ThenBy(item => item.ExerciseId, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (pick is not null)
                    decisions.Add(new(TrainingMuscleFeedbackAction.AddSet, muscleGroup.Key, pick.ExerciseId,
                        $"Dos sesiones seguidas con poco estímulo en {muscleGroup.Key}, bien recuperado y sin molestias en todos los ejercicios registrados, y el volumen semanal aún tiene margen. Se añade una serie."));
                continue;
            }

            var last3 = sessions.TakeLast(3).ToArray();
            if (last3.Length >= 2 &&
                last3[^1].All(item => item.SessionCompleted) &&
                last3.Count(HighAndLoaded) >= 2)
            {
                var pick = last3[^1]
                    .Where(item => item.SessionCompleted && item.Sets > 1)
                    .OrderByDescending(item => item.Sets)
                    .ThenBy(item => item.ExerciseId, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (pick is not null)
                    decisions.Add(new(TrainingMuscleFeedbackAction.ReduceSet, muscleGroup.Key, pick.ExerciseId,
                        $"Estímulo alto y fatiga acumulada repetidos en {muscleGroup.Key} en el mismo ejercicio. Se quita una serie para recuperar mejor."));
            }
        }
        return decisions;
    }

    private static bool EqualsValue(string? actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}

public enum TrainingMuscleFeedbackAction { AddSet, ReduceSet, SuggestSwap, PauseExercise }

/// <param name="Stimulus">low | adequate | high (null = not answered).</param>
/// <param name="Discomfort">none | mild | strong.</param>
/// <param name="PreFatigue">recovered | loaded.</param>
/// <param name="SessionCompleted">False for partial or not-completed sessions; these never count as volume evidence.</param>
public sealed record TrainingMuscleFeedbackExposure(
    DateOnly Date, string ExerciseId, IReadOnlyList<string> PrimaryMuscles, int Sets,
    string? Stimulus, string? Discomfort, string? PreFatigue, bool SessionCompleted = true);

public sealed record TrainingMuscleFeedbackDecision(
    TrainingMuscleFeedbackAction Action, string? Muscle, string ExerciseId, string Explanation);
