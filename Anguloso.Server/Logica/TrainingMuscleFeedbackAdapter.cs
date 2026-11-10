namespace Anguloso.Server.Logica;

/// <summary>
/// Turns the lifter's per-exercise answers after a workout (muscle stimulus, joint discomfort, how
/// recovered the muscle was when the session started) into volume decisions. Pure and deterministic.
///
/// Rules, in priority order (internal product values, not guideline thresholds):
///  1. Strong discomfort in the latest exposure of an exercise -> <c>PauseExercise</c> (safety; a person reviews).
///  2. Mild discomfort in 2 of the last 3 exposures of an exercise -> <c>SuggestSwap</c> (informational).
///  3. Per muscle, over its last 2 sessions: stimulus LOW in both, always recovered, no discomfort, and
///     the planned weekly sets still below the ceiling -> <c>AddSet</c> (+1 set to one exercise).
///  4. Per muscle, over its last 3 sessions: stimulus HIGH in at least 2 and "still loaded" in at least 2
///     -> <c>ReduceSet</c> (-1 set to one exercise that keeps at least 1).
///  5. Otherwise nothing: a plan that works is not touched. Missing answers never count as "adequate"
///     evidence for a change, and one session alone never decides.
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
        var decisions = new List<TrainingMuscleFeedbackDecision>();
        var blockedExercises = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in chronological.GroupBy(item => item.ExerciseId, StringComparer.Ordinal))
        {
            var recent = group.OrderBy(item => item.Date).TakeLast(3).ToArray();
            if (recent[^1].Discomfort == "strong")
            {
                blockedExercises.Add(group.Key);
                decisions.Add(new(TrainingMuscleFeedbackAction.PauseExercise, null, group.Key,
                    "Has indicado molestias fuertes en este ejercicio. Se pausa su progresión y debe revisarlo una persona antes de seguir."));
            }
            else if (recent.Count(item => item.Discomfort == "mild") >= 2)
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
            var sessions = muscleGroup.GroupBy(pair => pair.Item.Date).OrderBy(group => group.Key).Select(group => group.Select(pair => pair.Item).ToArray()).ToArray();
            var planned = plannedWeeklySetsByMuscle.GetValueOrDefault(muscleGroup.Key);
            var ceiling = weeklyCeilingByMuscle.GetValueOrDefault(muscleGroup.Key);

            bool Low(TrainingMuscleFeedbackExposure[] day) => day.Any(x => x.Stimulus == "low") && !day.Any(x => x.Stimulus == "high");
            bool HighAndLoaded(TrainingMuscleFeedbackExposure[] day) =>
                day.Any(x => x.Stimulus == "high" && x.PreFatigue == "loaded");
            bool Recovered(TrainingMuscleFeedbackExposure[] day) => day.All(x => x.PreFatigue == "recovered");
            bool Discomfort(TrainingMuscleFeedbackExposure[] day) => day.Any(x => x.Discomfort is "mild" or "strong");

            var last2 = sessions.TakeLast(2).ToArray();
            if (last2.Length == 2 && last2.All(Low) && last2.All(Recovered) && !last2.Any(Discomfort) && planned < ceiling)
            {
                // The muscle-level signal can aggregate several exercises, but the extra set must
                // go to an exercise that actually reported low stimulus in the latest exposure.
                var pick = last2[^1].Where(x => x.Stimulus == "low" && x.Sets < MaximumSetsPerExercise)
                    .OrderBy(x => x.Sets).ThenBy(x => x.ExerciseId, StringComparer.Ordinal).FirstOrDefault();
                if (pick is not null)
                    decisions.Add(new(TrainingMuscleFeedbackAction.AddSet, muscleGroup.Key, pick.ExerciseId,
                        $"Dos sesiones seguidas con poco estímulo en {muscleGroup.Key}, bien recuperado y sin molestias, y el volumen semanal aún tiene margen. Se añade una serie al ejercicio que registró poco estímulo."));
                continue;
            }

            var last3 = sessions.TakeLast(3).ToArray();
            // High stimulus and feeling still loaded must coincide in the same exposure. Require
            // two such sessions and a continuing signal in the latest one; separate signals on
            // different days are not enough to reduce an exercise.
            if (last3.Length >= 2 && last3.Count(HighAndLoaded) >= 2 && HighAndLoaded(last3[^1]))
            {
                var pick = last3[^1]
                    .Where(x => x.Stimulus == "high" && x.PreFatigue == "loaded" && x.Sets > 1)
                    .OrderByDescending(x => x.Sets).ThenBy(x => x.ExerciseId, StringComparer.Ordinal).FirstOrDefault();
                if (pick is not null)
                    decisions.Add(new(TrainingMuscleFeedbackAction.ReduceSet, muscleGroup.Key, pick.ExerciseId,
                        $"Estímulo alto y sensación de músculo aún cargado coinciden en al menos dos sesiones recientes de {muscleGroup.Key}, incluida la última. Se quita una serie al ejercicio que registró ambas señales."));
            }
        }
        return decisions;
    }
}

public enum TrainingMuscleFeedbackAction { AddSet, ReduceSet, SuggestSwap, PauseExercise }

/// <param name="Stimulus">low | adequate | high (null = not answered).</param>
/// <param name="Discomfort">none | mild | strong.</param>
/// <param name="PreFatigue">recovered | loaded.</param>
public sealed record TrainingMuscleFeedbackExposure(
    DateOnly Date, string ExerciseId, IReadOnlyList<string> PrimaryMuscles, int Sets,
    string? Stimulus, string? Discomfort, string? PreFatigue);

public sealed record TrainingMuscleFeedbackDecision(
    TrainingMuscleFeedbackAction Action, string? Muscle, string ExerciseId, string Explanation);
