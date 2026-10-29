using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Persiste la planificación de macrociclos independientemente de los bloques de rutinas.
/// La inicialización es idempotente y copia los mesociclos históricos una sola vez.
/// </summary>
public static class TrainingMacrocycleSchema
{
    public static void Initialize(Models.angulosodbContext context)
    {
        context.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS training_macrocycles (
                id BIGSERIAL PRIMARY KEY,
                plan_id INTEGER NOT NULL UNIQUE REFERENCES training_plans(id) ON DELETE CASCADE,
                name VARCHAR(160) NOT NULL DEFAULT 'Macrociclo principal',
                periodization_model VARCHAR(40) NOT NULL DEFAULT 'custom',
                start_date DATE NOT NULL,
                end_date DATE,
                notes VARCHAR(2000),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT ck_training_macrocycle_dates CHECK (end_date IS NULL OR end_date >= start_date)
            );
            CREATE TABLE IF NOT EXISTS training_macrocycle_phases (
                id BIGSERIAL PRIMARY KEY,
                macrocycle_id BIGINT NOT NULL REFERENCES training_macrocycles(id) ON DELETE CASCADE,
                name VARCHAR(160) NOT NULL,
                sequence_number INTEGER NOT NULL CHECK (sequence_number > 0),
                start_date DATE NOT NULL,
                end_date DATE NOT NULL,
                objective VARCHAR(500),
                phase_type VARCHAR(30) NOT NULL DEFAULT 'general'
                    CHECK (phase_type IN ('accumulation','intensification','deload','realization','general')),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT ck_training_macrocycle_phase_dates CHECK (end_date >= start_date),
                CONSTRAINT uq_training_macrocycle_phase_sequence UNIQUE (macrocycle_id, sequence_number)
            );
            CREATE INDEX IF NOT EXISTS idx_training_macrocycle_phases_dates
                ON training_macrocycle_phases(macrocycle_id, start_date, end_date);

            """);

        var legacyBackfillApplied = context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int AS \"Value\" FROM schema_migrations WHERE id = 'training-macrocycle-v1-independent-plan'")
            .Single() > 0;
        if (!legacyBackfillApplied)
        {
            context.Database.ExecuteSqlRaw("""
                INSERT INTO training_macrocycles(plan_id, name, periodization_model, start_date, end_date)
                SELECT p.id, 'Macrociclo principal', p.periodization_model, p.start_date, p.end_date
                FROM training_plans p
                WHERE EXISTS (SELECT 1 FROM training_mesocycles m WHERE m.plan_id = p.id)
                ON CONFLICT (plan_id) DO NOTHING;

                INSERT INTO training_macrocycle_phases(macrocycle_id, name, sequence_number, start_date, end_date, objective, phase_type)
                SELECT mc.id, m.name, m.sequence_number, m.start_date,
                       COALESCE(m.end_date, m.start_date), m.objective,
                       CASE
                         WHEN LOWER(m.name || ' ' || COALESCE(m.objective, '')) ~ 'descarga|recuperaci[oó]n|deload|descanso' THEN 'deload'
                         WHEN LOWER(m.name || ' ' || COALESCE(m.objective, '')) ~ 'realizaci[oó]n|puesta a punto|competici[oó]n|peaking|pico' THEN 'realization'
                         WHEN LOWER(m.name || ' ' || COALESCE(m.objective, '')) ~ 'intensificaci[oó]n|fuerza m[aá]xima|intensidad' THEN 'intensification'
                         WHEN LOWER(m.name || ' ' || COALESCE(m.objective, '')) ~ 'acumulaci[oó]n|base|volumen|hipertrofia' THEN 'accumulation'
                         ELSE 'general'
                       END
                FROM training_mesocycles m
                JOIN training_macrocycles mc ON mc.plan_id = m.plan_id
                ON CONFLICT (macrocycle_id, sequence_number) DO NOTHING;
                INSERT INTO schema_migrations(id) VALUES ('training-macrocycle-v1-independent-plan')
                ON CONFLICT (id) DO NOTHING;
                """);
        }
    }
}
