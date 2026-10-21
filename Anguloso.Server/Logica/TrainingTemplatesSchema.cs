using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

public static class TrainingTemplatesSchema
{
    public static void Initialize(Models.angulosodbContext context)
    {
        context.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS training_plan_templates (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                created_by_user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                name VARCHAR(160) NOT NULL,
                periodization_model VARCHAR(40) NOT NULL DEFAULT 'custom',
                notes VARCHAR(2000),
                template_data JSONB NOT NULL DEFAULT '{{}}'::jsonb,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT ck_training_template_model CHECK (periodization_model IN ('linear','undulating','block','reverse','continuous_competition','autoregulatory','custom'))
            );
            CREATE INDEX IF NOT EXISTS idx_training_plan_templates_tenant_owner
                ON training_plan_templates(tenant_id, created_by_user_id, updated_at DESC);
            CREATE TABLE IF NOT EXISTS training_session_templates (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER REFERENCES tenants(id) ON DELETE CASCADE,
                created_by_user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                name VARCHAR(160) NOT NULL,
                session_type VARCHAR(30) NOT NULL CHECK (session_type IN ('endurance','strength','mobility','recovery','health','general','other')),
                duration_minutes INTEGER CHECK (duration_minutes IS NULL OR duration_minutes BETWEEN 1 AND 1440),
                intensity_target VARCHAR(120),
                instructions VARCHAR(4000),
                exercise_prescription VARCHAR(6000),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_training_session_templates_tenant_owner
                ON training_session_templates(tenant_id, created_by_user_id, updated_at DESC);
            """);
    }
}
