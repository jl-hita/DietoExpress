using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Stores the professional's chosen weekly split independently from periodization model and session content.
/// </summary>
public static class TrainingPlanSplitSchema
{
    public static void Initialize(Models.angulosodbContext context)
    {
        context.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS training_plan_split_preferences (
                plan_id INTEGER PRIMARY KEY REFERENCES training_plans(id) ON DELETE CASCADE,
                split_type SMALLINT NOT NULL DEFAULT 0 CHECK (split_type BETWEEN 0 AND 3),
                training_days SMALLINT[] NOT NULL DEFAULT ARRAY[1,3,5]::smallint[],
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT ck_training_plan_split_days CHECK (
                    cardinality(training_days) BETWEEN 2 AND 6
                    AND (SELECT COUNT(DISTINCT day) FROM unnest(training_days) AS day) = cardinality(training_days)
                    AND 0 <= ALL(training_days) AND 6 >= ALL(training_days)
                )
            );
            """);
    }
}
