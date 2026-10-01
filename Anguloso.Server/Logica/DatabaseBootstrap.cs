using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anguloso.Server.Logica;

public static class DatabaseBootstrap
{
    public static void InitializeDatabaseAsync(angulosodbContext context, ILogger logger)
    {
        logger.LogInformation("Iniciando comprobación y arranque de base de datos...");

        // 1. Comprobar conectividad con la BBDD
        bool canConnect = false;
        try
        {
            canConnect = context.Database.CanConnect();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CRÍTICO: No se puede establecer conexión con la base de datos.");
            throw new InvalidOperationException("No se puede conectar al servidor de base de datos PostgreSQL. Verifique que el servicio esté activo y las credenciales sean válidas.", ex);
        }

        if (!canConnect)
        {
            var msg = "CRÍTICO: CanConnect retornó falso para la base de datos configurada.";
            logger.LogError(msg);
            throw new InvalidOperationException(msg);
        }

        logger.LogInformation("Conexión con PostgreSQL establecida correctamente.");

        // 2. Comprobación y creación de todas las tablas con su esquema completo si no existen
        try
        {
            // Ejecutamos DDL idempotente en orden de dependencias de foreign keys
            context.Database.ExecuteSqlRaw(@"
                -- 1. Tenants (Clínicas / Organizaciones)
                CREATE TABLE IF NOT EXISTS tenants (
                    id SERIAL PRIMARY KEY,
                    legal_name VARCHAR(200) NOT NULL,
                    trade_name VARCHAR(200),
                    cif_nif VARCHAR(50),
                    slug VARCHAR(100) NOT NULL,
                    status VARCHAR(50) DEFAULT 'active',
                    dpo_email VARCHAR(150),
                    contact_email VARCHAR(150),
                    contact_phone VARCHAR(50),
                    address VARCHAR(300),
                    logo_url TEXT,
                    created_at TIMESTAMPTZ DEFAULT NOW()
                );

                -- 2. Users (Nutricionistas, Administradores, Personal)
                CREATE TABLE IF NOT EXISTS users (
                    id SERIAL PRIMARY KEY,
                    username VARCHAR(50) NOT NULL UNIQUE,
                    full_name VARCHAR(100),
                    password_hash VARCHAR(255),
                    created_at TIMESTAMPTZ DEFAULT NOW(),
                    last_login TIMESTAMPTZ,
                    role VARCHAR(20) DEFAULT 'user',
                    email VARCHAR(150) UNIQUE,
                    email_confirmed BOOLEAN DEFAULT FALSE,
                    email_confirmation_token VARCHAR(255),
                    reset_password_token VARCHAR(255),
                    reset_token_expiration TIMESTAMPTZ,
                    google_id VARCHAR(100),
                    provider VARCHAR(50),
                    country VARCHAR(100),
                    lang VARCHAR(20),
                    clinic_name VARCHAR(150),
                    clinic_address VARCHAR(250),
                    clinic_phone VARCHAR(50),
                    clinic_logo TEXT,
                    subscription_plan VARCHAR(50) DEFAULT 'free',
                    subscription_status VARCHAR(50) DEFAULT 'active',
                    license_expires_at TIMESTAMPTZ,
                    max_clients_allowed INTEGER DEFAULT 10,
                    tenant_id INTEGER REFERENCES tenants(id)
                );

                -- 3. Clients (Pacientes)
                CREATE TABLE IF NOT EXISTS clients (
                    id SERIAL PRIMARY KEY,
                    user_id INTEGER NOT NULL REFERENCES users(id),
                    full_name VARCHAR(100) NOT NULL,
                    email VARCHAR(150),
                    phone VARCHAR(50),
                    birth_date DATE,
                    gender VARCHAR(20),
                    notes TEXT,
                    created_at TIMESTAMPTZ DEFAULT NOW(),
                    access_token VARCHAR(64),
                    passcode_hash VARCHAR(100),
                    last_portal_access TIMESTAMPTZ,
                    tenant_id INTEGER REFERENCES tenants(id)
                );

                -- 4. Biometrics (Mediciones y Bioimpedancia)
                CREATE TABLE IF NOT EXISTS biometrics (
                    id SERIAL PRIMARY KEY,
                    client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
                    measurement_date DATE NOT NULL,
                    weight DOUBLE PRECISION,
                    height DOUBLE PRECISION,
                    body_fat DOUBLE PRECISION,
                    muscle_mass DOUBLE PRECISION,
                    visceral_fat DOUBLE PRECISION,
                    waist DOUBLE PRECISION,
                    hip DOUBLE PRECISION,
                    neck DOUBLE PRECISION,
                    triceps DOUBLE PRECISION,
                    abdomen DOUBLE PRECISION,
                    thigh DOUBLE PRECISION,
                    subscapular DOUBLE PRECISION,
                    suprailiac DOUBLE PRECISION,
                    biceps DOUBLE PRECISION,
                    chest DOUBLE PRECISION,
                    axilla DOUBLE PRECISION,
                    calf_skinfold DOUBLE PRECISION,
                    arm_perimeter DOUBLE PRECISION,
                    calf_perimeter DOUBLE PRECISION,
                    wrist_diameter DOUBLE PRECISION,
                    femur_diameter DOUBLE PRECISION,
                    humerus_diameter DOUBLE PRECISION,
                    notes TEXT
                );

                -- 5. Food Exchange Groups (Grupos de Intercambio)
                CREATE TABLE IF NOT EXISTS food_exchange_groups (
                    id SERIAL PRIMARY KEY,
                    name VARCHAR(100) NOT NULL,
                    kcal NUMERIC(6, 2) NOT NULL DEFAULT 0,
                    protein NUMERIC(6, 2) NOT NULL DEFAULT 0,
                    carbs NUMERIC(6, 2) NOT NULL DEFAULT 0,
                    fat NUMERIC(6, 2) NOT NULL DEFAULT 0
                );

                -- 6. Food Sources (Fuentes de alimentos BEDCA, USDA, etc.)
                CREATE TABLE IF NOT EXISTS food_sources (
                    id SERIAL PRIMARY KEY,
                    source_name VARCHAR(100) NOT NULL,
                    url VARCHAR(200)
                );

                -- 7. Foods (Catálogo de alimentos)
                CREATE TABLE IF NOT EXISTS foods (
                    id SERIAL PRIMARY KEY,
                    name VARCHAR(200) NOT NULL,
                    brands VARCHAR(200),
                    category VARCHAR(200),
                    nutriscore VARCHAR(5),
                    kcal DOUBLE PRECISION,
                    protein DOUBLE PRECISION,
                    carbs DOUBLE PRECISION,
                    fat DOUBLE PRECISION,
                    saturated_fat DOUBLE PRECISION,
                    fiber DOUBLE PRECISION,
                    sugar DOUBLE PRECISION,
                    salt DOUBLE PRECISION,
                    vitamin_a_ug DOUBLE PRECISION,
                    vitamin_c_mg DOUBLE PRECISION,
                    vitamin_d_ug DOUBLE PRECISION,
                    vitamin_e_mg DOUBLE PRECISION,
                    vitamin_b12_ug DOUBLE PRECISION,
                    folate_ug DOUBLE PRECISION,
                    calcium_mg DOUBLE PRECISION,
                    iron_mg DOUBLE PRECISION,
                    magnesium_mg DOUBLE PRECISION,
                    potassium_mg DOUBLE PRECISION,
                    zinc_mg DOUBLE PRECISION,
                    serving_size DOUBLE PRECISION,
                    serving_size_unit VARCHAR(10),
                    serving_size_text VARCHAR(50),
                    default_grams NUMERIC(6, 2) DEFAULT 100,
                    source VARCHAR(50) DEFAULT 'local',
                    external_id VARCHAR(200),
                    last_synced_at TIMESTAMPTZ,
                    created_at TIMESTAMPTZ DEFAULT NOW(),
                    exchange_group_id INTEGER REFERENCES food_exchange_groups(id) ON DELETE SET NULL,
                    grams_per_exchange NUMERIC(6, 2)
                );

                -- 8. Diets (Planes dietéticos y plantillas)
                CREATE TABLE IF NOT EXISTS diets (
                    id SERIAL PRIMARY KEY,
                    name VARCHAR(200) NOT NULL,
                    target_kcal NUMERIC(6, 2),
                    target_protein NUMERIC(6, 2),
                    target_carbs NUMERIC(6, 2),
                    target_fat NUMERIC(6, 2),
                    notes TEXT,
                    created_at TIMESTAMPTZ DEFAULT NOW(),
                    user_id INTEGER REFERENCES users(id),
                    tenant_id INTEGER REFERENCES tenants(id),
                    is_shared BOOLEAN DEFAULT FALSE,
                    is_template BOOLEAN DEFAULT FALSE
                );

                -- 9. Client Diets (Asignación de dietas a pacientes)
                CREATE TABLE IF NOT EXISTS client_diets (
                    id SERIAL PRIMARY KEY,
                    client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
                    diet_id INTEGER NOT NULL REFERENCES diets(id) ON DELETE CASCADE,
                    assigned_at TIMESTAMPTZ DEFAULT NOW(),
                    start_date DATE NOT NULL DEFAULT CURRENT_DATE,
                    end_date DATE,
                    is_active BOOLEAN DEFAULT TRUE,
                    notes TEXT
                );

                -- 10. Diet Days (Días estructurados dentro de una dieta)
                CREATE TABLE IF NOT EXISTS diet_days (
                    id SERIAL PRIMARY KEY,
                    diet_id INTEGER NOT NULL REFERENCES diets(id) ON DELETE CASCADE,
                    day_index INTEGER NOT NULL,
                    CONSTRAINT diet_days_diet_id_day_index_key UNIQUE (diet_id, day_index)
                );

                -- 11. Meals (Tomas/Comidas dentro del día)
                CREATE TABLE IF NOT EXISTS meals (
                    id SERIAL PRIMARY KEY,
                    diet_day_id INTEGER NOT NULL REFERENCES diet_days(id) ON DELETE CASCADE,
                    name VARCHAR(100) NOT NULL,
                    meal_index INTEGER NOT NULL,
                    CONSTRAINT meals_diet_day_id_meal_index_key UNIQUE (diet_day_id, meal_index)
                );

                -- 12. Meal Items (Ingredientes / Alimentos / Intercambios de la comida)
                CREATE TABLE IF NOT EXISTS meal_items (
                    id SERIAL PRIMARY KEY,
                    meal_id INTEGER NOT NULL REFERENCES meals(id) ON DELETE CASCADE,
                    food_id INTEGER REFERENCES foods(id) ON DELETE SET NULL,
                    grams NUMERIC(6, 2),
                    kcal NUMERIC(6, 2),
                    protein NUMERIC(6, 2),
                    carbs NUMERIC(6, 2),
                    fat NUMERIC(6, 2),
                    exchange_group_id INTEGER REFERENCES food_exchange_groups(id) ON DELETE SET NULL,
                    exchange_count NUMERIC(6, 2)
                );

                -- 13. Recipes (Recetario del profesional)
                CREATE TABLE IF NOT EXISTS recipes (
                    id SERIAL PRIMARY KEY,
                    user_id INTEGER NOT NULL REFERENCES users(id),
                    name VARCHAR(200) NOT NULL,
                    instructions TEXT,
                    created_at TIMESTAMPTZ DEFAULT NOW()
                );

                -- 14. Recipe Items (Ingredientes de las recetas)
                CREATE TABLE IF NOT EXISTS recipe_items (
                    id SERIAL PRIMARY KEY,
                    recipe_id INTEGER NOT NULL REFERENCES recipes(id) ON DELETE CASCADE,
                    food_id INTEGER NOT NULL REFERENCES foods(id),
                    grams NUMERIC(6, 2) NOT NULL
                );

                -- 15. Anamnesis: Medical History
                CREATE TABLE IF NOT EXISTS medical_history (
                    id SERIAL PRIMARY KEY,
                    client_id INTEGER NOT NULL UNIQUE REFERENCES clients(id) ON DELETE CASCADE,
                    diabetes BOOLEAN DEFAULT FALSE,
                    hypertension BOOLEAN DEFAULT FALSE,
                    hypothyroidism BOOLEAN DEFAULT FALSE,
                    surgeries TEXT,
                    routine_medication TEXT,
                    other_pathologies TEXT
                );

                -- 16. Anamnesis: Digestive Health
                CREATE TABLE IF NOT EXISTS digestive_health (
                    id SERIAL PRIMARY KEY,
                    client_id INTEGER NOT NULL UNIQUE REFERENCES clients(id) ON DELETE CASCADE,
                    intestinal_habits TEXT,
                    bloating BOOLEAN DEFAULT FALSE,
                    heartburn BOOLEAN DEFAULT FALSE,
                    gluten_intolerance BOOLEAN DEFAULT FALSE,
                    lactose_intolerance BOOLEAN DEFAULT FALSE,
                    fodmaps_intolerance BOOLEAN DEFAULT FALSE,
                    other_intolerances TEXT,
                    notes TEXT
                );

                -- 17. Anamnesis: Food Preferences
                CREATE TABLE IF NOT EXISTS food_preferences (
                    id SERIAL PRIMARY KEY,
                    client_id INTEGER NOT NULL UNIQUE REFERENCES clients(id) ON DELETE CASCADE,
                    preferred_foods TEXT,
                    disliked_foods TEXT,
                    allergies TEXT
                );

                -- 18. Anamnesis: Lifestyle History
                CREATE TABLE IF NOT EXISTS lifestyle_history (
                    id SERIAL PRIMARY KEY,
                    client_id INTEGER NOT NULL UNIQUE REFERENCES clients(id) ON DELETE CASCADE,
                    work_schedule TEXT,
                    sleep_habits TEXT,
                    water_consumption TEXT,
                    alcohol_consumption TEXT,
                    tobacco_consumption TEXT
                );

                -- 19. Config
                CREATE TABLE IF NOT EXISTS config (
                    id SERIAL PRIMARY KEY,
                    nombre_config VARCHAR(255) NOT NULL,
                    valor_config VARCHAR(255) NOT NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS uq_config_nombre_config ON config(nombre_config);

                -- 20. Audit Logs (Trazabilidad clínica y RGPD)
                CREATE TABLE IF NOT EXISTS audit_logs (
                    id BIGSERIAL PRIMARY KEY,
                    tenant_id INTEGER REFERENCES tenants(id),
                    user_id INTEGER REFERENCES users(id),
                    user_role VARCHAR(50),
                    action VARCHAR(100) NOT NULL,
                    entity_name VARCHAR(100) NOT NULL,
                    entity_id VARCHAR(100),
                    client_id INTEGER REFERENCES clients(id),
                    ip_address VARCHAR(50),
                    user_agent VARCHAR(300),
                    details TEXT,
                    created_at TIMESTAMPTZ DEFAULT NOW()
                );

                -- Índices de rendimiento
                CREATE INDEX IF NOT EXISTS idx_clients_user_id ON clients(user_id);
                CREATE INDEX IF NOT EXISTS idx_clients_tenant_id ON clients(tenant_id);
                -- Invariant: a client can have at most one active diet assignment.
                -- Keep this at database level as a final guard against races between requests.
                CREATE UNIQUE INDEX IF NOT EXISTS idx_unique_active_client_diet
                    ON client_diets(client_id) WHERE is_active = TRUE;
                CREATE INDEX IF NOT EXISTS idx_diets_user_id ON diets(user_id);
                CREATE INDEX IF NOT EXISTS idx_diets_tenant_id ON diets(tenant_id);
                CREATE INDEX IF NOT EXISTS idx_biometrics_client_id ON biometrics(client_id);
                CREATE INDEX IF NOT EXISTS idx_audit_logs_client_id ON audit_logs(client_id);
                CREATE INDEX IF NOT EXISTS idx_audit_logs_tenant_id ON audit_logs(tenant_id);

                -- Índices de users para el dashboard de administración y consultas frecuentes
                CREATE INDEX IF NOT EXISTS idx_users_created_at ON users(created_at DESC, id DESC);
                CREATE INDEX IF NOT EXISTS idx_users_license_expires_at ON users(license_expires_at);
                CREATE INDEX IF NOT EXISTS idx_users_subscription_status ON users(subscription_status);
                CREATE INDEX IF NOT EXISTS idx_users_tenant_id ON users(tenant_id);
                -- Integridad de identidad: evita duplicados aunque dos peticiones
                -- lleguen simultáneamente y el control de aplicación falle.
                CREATE UNIQUE INDEX IF NOT EXISTS uq_users_username ON users(username);
                CREATE UNIQUE INDEX IF NOT EXISTS uq_users_username_ci ON users(LOWER(username));
                CREATE UNIQUE INDEX IF NOT EXISTS uq_users_email ON users(email) WHERE email IS NOT NULL;
                -- La identidad de login por email es case-insensitive. Impedir variantes
                -- de mayúsculas/minúsculas evita dos cuentas que Auth no podría distinguir.
                CREATE UNIQUE INDEX IF NOT EXISTS uq_users_email_ci ON users(LOWER(email)) WHERE email IS NOT NULL;
                CREATE UNIQUE INDEX IF NOT EXISTS uq_users_google_id ON users(google_id) WHERE google_id IS NOT NULL;

                -- Búsquedas de texto del dashboard (ILIKE/Contains sobre nombre y clínica)
                CREATE EXTENSION IF NOT EXISTS pg_trgm;
                CREATE INDEX IF NOT EXISTS idx_users_full_name_trgm
                    ON users USING gin (LOWER(full_name) gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS idx_users_clinic_name_trgm
                    ON users USING gin (LOWER(clinic_name) gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS idx_users_username_trgm
                    ON users USING gin (LOWER(username) gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS idx_users_email_trgm
                    ON users USING gin (LOWER(email) gin_trgm_ops);
            ");

            // Configuración inicial idempotente. No se sobrescriben valores existentes.
            context.Database.ExecuteSqlRaw(@"
                INSERT INTO config (nombre_config, valor_config)
                SELECT 'googleClientId', '__CONFIGURE_GOOGLE_CLIENT_ID__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'googleClientId');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'dominio', '__CONFIGURE_DOMAIN__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'dominio');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'frontendUrl', '__CONFIGURE_FRONTEND_URL__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'frontendUrl');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'smtpServer', '__CONFIGURE_SMTP_SERVER__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'smtpServer');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'smtpPort', '__CONFIGURE_SMTP_PORT__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'smtpPort');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'smtpEnableSsl', '__CONFIGURE_SMTP_ENABLE_SSL__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'smtpEnableSsl');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'smtpFromEmail', '__CONFIGURE_SMTP_FROM_EMAIL__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'smtpFromEmail');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'smtpFromName', '__CONFIGURE_SMTP_FROM_NAME__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'smtpFromName');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'smtpUser', '__CONFIGURE_SMTP_USER__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'smtpUser');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'smtpPwd', '__CONFIGURE_SMTP_PASSWORD__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'smtpPwd');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'usdaApiKey', '__CONFIGURE_USDA_API_KEY__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'usdaApiKey');
            ");

            logger.LogInformation("Estructura de tablas y configuración inicial verificadas y listas en PostgreSQL.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al crear o verificar las tablas en la base de datos.");
            throw;
        }
    }

    /// <summary>
    /// Evoluciones incrementales de la BBDD SaaS. Es idempotente y se ejecuta al arrancar,
    /// por lo que una instalación existente no necesita recrearse.
    /// </summary>
    public static void UpgradeSaaSSchema(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS schema_migrations (
                id VARCHAR(100) PRIMARY KEY,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
        ");

        var alreadyApplied = context.Database
            .SqlQueryRaw<int>("SELECT 1 AS \"Value\" FROM schema_migrations WHERE id = 'saas-v1' LIMIT 1")
            .FirstOrDefault() == 1;

        if (alreadyApplied)
        {
            logger.LogDebug("Migración SaaS saas-v1 ya aplicada.");
            return;
        }

        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS subscription_plans (
                id SERIAL PRIMARY KEY,
                code VARCHAR(50) NOT NULL UNIQUE,
                name VARCHAR(150) NOT NULL,
                description TEXT,
                monthly_price NUMERIC(10,2) NOT NULL DEFAULT 0,
                yearly_price NUMERIC(10,2) NOT NULL DEFAULT 0,
                stripe_product_id VARCHAR(255),
                stripe_monthly_price_id VARCHAR(255),
                stripe_yearly_price_id VARCHAR(255),
                max_nutritionists INTEGER,
                max_clients_per_nutritionist INTEGER,
                max_total_clients INTEGER,
                trial_days INTEGER,
                active BOOLEAN NOT NULL DEFAULT TRUE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS subscription_plan_features (
                id SERIAL PRIMARY KEY,
                plan_id INTEGER NOT NULL REFERENCES subscription_plans(id) ON DELETE CASCADE,
                feature_code VARCHAR(100) NOT NULL,
                enabled BOOLEAN NOT NULL DEFAULT TRUE,
                CONSTRAINT subscription_plan_features_unique UNIQUE(plan_id, feature_code)
            );

            CREATE TABLE IF NOT EXISTS subscriptions (
                id SERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                plan_id INTEGER NOT NULL REFERENCES subscription_plans(id) ON DELETE RESTRICT,
                status VARCHAR(50) NOT NULL DEFAULT 'active',
                started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                expires_at TIMESTAMPTZ,
                cancelled_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS subscription_events (
                id BIGSERIAL PRIMARY KEY,
                subscription_id INTEGER NOT NULL REFERENCES subscriptions(id) ON DELETE CASCADE,
                event_type VARCHAR(100) NOT NULL,
                old_plan_id INTEGER REFERENCES subscription_plans(id) ON DELETE SET NULL,
                new_plan_id INTEGER REFERENCES subscription_plans(id) ON DELETE SET NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                details TEXT
            );

            CREATE TABLE IF NOT EXISTS client_nutritionist_assignments (
                id SERIAL PRIMARY KEY,
                client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
                nutritionist_id INTEGER NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
                assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                assigned_by_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
                unassigned_at TIMESTAMPTZ,
                is_active BOOLEAN NOT NULL DEFAULT TRUE
            );

            ALTER TABLE recipes ADD COLUMN IF NOT EXISTS tenant_id INTEGER REFERENCES tenants(id);
            CREATE INDEX IF NOT EXISTS idx_recipes_tenant_id ON recipes(tenant_id);
            CREATE INDEX IF NOT EXISTS idx_subscriptions_tenant_id ON subscriptions(tenant_id);
            CREATE INDEX IF NOT EXISTS idx_subscription_events_subscription_id ON subscription_events(subscription_id);
            CREATE INDEX IF NOT EXISTS idx_assignments_nutritionist_id ON client_nutritionist_assignments(nutritionist_id);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_assignments_active_client ON client_nutritionist_assignments(client_id) WHERE is_active = TRUE;

            -- Convertimos usuarios existentes sin tenant en tenants individuales.
            DO $$
            DECLARE u RECORD; new_tenant_id INTEGER; base_slug TEXT; candidate_slug TEXT; suffix INTEGER;
            BEGIN
                FOR u IN SELECT id, username, full_name, email, clinic_name FROM users WHERE role <> 'superadmin' AND tenant_id IS NULL LOOP
                    base_slug := regexp_replace(lower(coalesce(nullif(u.username,''), 'tenant-' || u.id::text)), '[^a-z0-9]+', '-', 'g');
                    base_slug := trim(both '-' from base_slug);
                    IF base_slug = '' THEN base_slug := 'tenant-' || u.id::text; END IF;
                    candidate_slug := base_slug; suffix := 0;
                    WHILE EXISTS (SELECT 1 FROM tenants WHERE slug = candidate_slug) LOOP
                        suffix := suffix + 1; candidate_slug := base_slug || '-' || suffix::text;
                    END LOOP;
                    INSERT INTO tenants(legal_name, trade_name, slug, contact_email, status)
                    VALUES (coalesce(nullif(u.clinic_name,''), nullif(u.full_name,''), u.username), coalesce(nullif(u.clinic_name,''), u.full_name), candidate_slug, u.email, 'active')
                    RETURNING id INTO new_tenant_id;
                    UPDATE users SET tenant_id = new_tenant_id WHERE id = u.id;
                END LOOP;
            END $$;
            UPDATE clients c SET tenant_id = u.tenant_id
            FROM users u WHERE c.user_id = u.id AND c.tenant_id IS NULL;

            UPDATE diets d SET tenant_id = u.tenant_id
            FROM users u WHERE d.user_id = u.id AND d.tenant_id IS NULL;

            UPDATE recipes r SET tenant_id = u.tenant_id
            FROM users u WHERE r.user_id = u.id AND r.tenant_id IS NULL;

            -- Seed de planes comerciales. Son editables desde el SuperAdmin.
            INSERT INTO subscription_plans(code,name,description,monthly_price,yearly_price,max_nutritionists,max_clients_per_nutritionist,max_total_clients,trial_days,active)
            VALUES
                ('free','Cuenta gratuita','Cuenta de acceso sin capacidad profesional',0,0,1,0,0,NULL,TRUE),
                ('demo_nutri','Demo nutricionista','Acceso profesional temporal concedido por SuperAdmin',0,0,1,100,100,14,TRUE),
                ('nutri_full','Nutri Full','Licencia profesional individual',29.90,299,1,100,100,NULL,TRUE),
                ('clinic_full','Clínica Full','Licencia para clínicas con varios nutricionistas',79.90,799,5,100,500,NULL,TRUE)
            ON CONFLICT(code) DO NOTHING;

            -- Price IDs de Stripe Test Mode para los planes comerciales.
            -- Se mantienen en BBDD para que Checkout no dependa de valores hardcodeados en el código.
            UPDATE subscription_plans
            SET stripe_monthly_price_id = 'price_1UKdhz0RD4LdDkcUqFaqkFL2',
                stripe_yearly_price_id = 'price_1UKdko0RD4LdDkcUceVNvurx'
            WHERE code = 'nutri_full';

            UPDATE subscription_plans
            SET stripe_monthly_price_id = 'price_1UKdmV0RD4LdDkcU7ueOlu1B',
                stripe_yearly_price_id = 'price_1UKdn20RD4LdDkcUpJchaR8Y'
            WHERE code = 'clinic_full';

            INSERT INTO subscription_plan_features(plan_id,feature_code,enabled)
            SELECT p.id, f.feature_code, TRUE
            FROM subscription_plans p
            CROSS JOIN (VALUES
                ('CLIENT_PORTAL'),('PDF_EXPORT'),('PDF_BRANDING'),('GOOGLE_LOGIN'),
                ('RECIPES'),('DIET_TEMPLATES'),('SHARED_DIETS'),('MULTI_NUTRITIONIST'),
                ('CLINIC_DASHBOARD'),('CLIENT_ASSIGNMENT'),('AUDIT_LOGS')
            ) f(feature_code)
            WHERE p.code IN ('demo_nutri','nutri_full','clinic_full')
            ON CONFLICT(plan_id,feature_code) DO NOTHING;

            -- La cuenta FREE no tiene funciones profesionales; la DEMO sí las tiene.
            UPDATE subscription_plan_features SET enabled = FALSE
            WHERE plan_id = (SELECT id FROM subscription_plans WHERE code='free');

            -- Asignamos una suscripción a cada tenant existente si todavía no tiene ninguna.
            INSERT INTO subscriptions(tenant_id,plan_id,status,started_at,expires_at)
            SELECT t.id,
                   CASE
                     WHEN u.subscription_plan IN ('enterprise','clinic_full') THEN (SELECT id FROM subscription_plans WHERE code='clinic_full')
                     WHEN u.subscription_plan IN ('professional','nutri_full') THEN (SELECT id FROM subscription_plans WHERE code='nutri_full')
                     ELSE (SELECT id FROM subscription_plans WHERE code='free')
                   END,
                   CASE WHEN coalesce(u.subscription_status,'active') IN ('suspended','cancelled','past_due') THEN coalesce(u.subscription_status,'active') ELSE 'active' END,
                   coalesce(u.created_at,NOW()),
                   CASE
                     WHEN u.license_expires_at IS NOT NULL THEN u.license_expires_at
                     WHEN coalesce(u.subscription_plan,'free') = 'demo_nutri' THEN NOW() + INTERVAL '14 days'
                     ELSE NULL
                   END
            FROM tenants t
            JOIN LATERAL (SELECT * FROM users ux WHERE ux.tenant_id=t.id ORDER BY CASE WHEN ux.role='clinic_admin' THEN 0 ELSE 1 END, ux.id LIMIT 1) u ON TRUE
            WHERE NOT EXISTS (SELECT 1 FROM subscriptions s WHERE s.tenant_id=t.id);

            -- Integridad final: una tenant solo puede tener una suscripción.
            -- El backfill anterior ya crea como máximo una por tenant; si una base
            -- histórica contiene duplicados, el despliegue debe detenerse en lugar
            -- de dejar una condición de carrera permanente.
            -- A tenant may keep cancelled subscription history, but only one
            -- non-cancelled subscription can exist at a time.
            CREATE UNIQUE INDEX IF NOT EXISTS idx_subscriptions_tenant_active_unique
                ON subscriptions(tenant_id)
                WHERE status NOT IN ('cancelled', 'canceled');

            -- El usuario propietario de una clínica existente pasa a ser clinic_admin.
            UPDATE users u SET role='clinic_admin'
            WHERE u.tenant_id IS NOT NULL
              AND u.role IN ('user','nutritionist')
              AND EXISTS (SELECT 1 FROM subscriptions s JOIN subscription_plans p ON p.id=s.plan_id WHERE s.tenant_id=u.tenant_id AND p.code='clinic_full')
              AND u.id = (SELECT min(u2.id) FROM users u2 WHERE u2.tenant_id=u.tenant_id AND u2.role IN ('user','nutritionist'));

            -- La asignación actual se conserva en el historial nuevo.
            INSERT INTO client_nutritionist_assignments(client_id,nutritionist_id,assigned_at,is_active)
            SELECT c.id,c.user_id,coalesce(c.created_at,NOW()),TRUE
            FROM clients c
            WHERE NOT EXISTS (SELECT 1 FROM client_nutritionist_assignments a WHERE a.client_id=c.id AND a.is_active);

            -- Mantener el plan antiguo por compatibilidad temporal con el código existente.
            UPDATE users u SET subscription_plan=p.code,
                                subscription_status=s.status,
                                max_clients_allowed=COALESCE(p.max_clients_per_nutritionist,0)
            FROM subscriptions s JOIN subscription_plans p ON p.id=s.plan_id
            WHERE s.tenant_id=u.tenant_id;
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('saas-v1') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración SaaS saas-v1 aplicada correctamente.");
    }

    /// <summary>
    /// Evolución del modelo de acceso público: cuenta gratuita y demos concedidas
    /// exclusivamente desde SuperAdmin.
    /// </summary>
    public static void UpgradeSaaSSchemaV2(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            INSERT INTO subscription_plans
                (code,name,description,monthly_price,yearly_price,max_nutritionists,max_clients_per_nutritionist,max_total_clients,trial_days,active)
            VALUES
                ('free','Cuenta gratuita','Cuenta de acceso sin capacidad profesional',0,0,1,0,0,NULL,TRUE),
                ('demo_nutri','Demo nutricionista','Acceso profesional temporal concedido por SuperAdmin',0,0,1,100,100,14,TRUE)
            ON CONFLICT(code) DO NOTHING;

            UPDATE subscription_plans
            SET name='Demo nutricionista',
                description='Acceso profesional temporal concedido por SuperAdmin',
                monthly_price=0,
                yearly_price=0,
                max_nutritionists=1,
                max_clients_per_nutritionist=100,
                max_total_clients=100,
                trial_days=14,
                active=TRUE
            WHERE code='trial_nutri';

            UPDATE subscription_plans
            SET active=FALSE
            WHERE code='trial_nutri';

            UPDATE subscription_plans
            SET name='Cuenta gratuita',
                description='Cuenta de acceso sin capacidad profesional',
                monthly_price=0,
                yearly_price=0,
                max_nutritionists=1,
                max_clients_per_nutritionist=0,
                max_total_clients=0,
                trial_days=NULL,
                active=TRUE
            WHERE code='free';

            INSERT INTO subscription_plan_features(plan_id,feature_code,enabled)
            SELECT p.id, f.feature_code, TRUE
            FROM subscription_plans p
            CROSS JOIN (VALUES
                ('CLIENT_PORTAL'),('PDF_EXPORT'),('PDF_BRANDING'),('GOOGLE_LOGIN'),
                ('RECIPES'),('DIET_TEMPLATES'),('SHARED_DIETS'),('MULTI_NUTRITIONIST'),
                ('CLINIC_DASHBOARD'),('CLIENT_ASSIGNMENT'),('AUDIT_LOGS')
            ) f(feature_code)
            WHERE p.code='demo_nutri'
            ON CONFLICT(plan_id,feature_code) DO UPDATE SET enabled=EXCLUDED.enabled;

            UPDATE users u
            SET subscription_plan='free',
                subscription_status='active',
                max_clients_allowed=0
            WHERE u.subscription_plan IN ('trial_nutri');

            UPDATE subscriptions s
            SET plan_id=(SELECT id FROM subscription_plans WHERE code='free'),
                status='active',
                expires_at=NULL
            WHERE EXISTS (
                SELECT 1 FROM users u
                WHERE u.tenant_id=s.tenant_id
                  AND u.subscription_plan='free'
                  AND u.role <> 'superadmin'
            );

            INSERT INTO subscriptions(tenant_id,plan_id,status,started_at,expires_at)
            SELECT t.id,(SELECT id FROM subscription_plans WHERE code='free'),'active',NOW(),NULL
            FROM tenants t
            JOIN users u ON u.tenant_id=t.id AND u.role <> 'superadmin'
            WHERE u.subscription_plan='free'
              AND NOT EXISTS (SELECT 1 FROM subscriptions s WHERE s.tenant_id=t.id);

            UPDATE users u
            SET max_clients_allowed=COALESCE(p.max_clients_per_nutritionist,0)
            FROM subscriptions s
            JOIN subscription_plans p ON p.id=s.plan_id
            WHERE s.tenant_id=u.tenant_id
              AND u.role <> 'superadmin'
              AND u.subscription_plan=p.code;
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('saas-v2-access-model') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración SaaS saas-v2-access-model aplicada correctamente.");
    }

    /// <summary>
    /// Evolución de la cuenta FREE: prueba funcional de 7 días,
    /// con un cliente y una dieta como límite.
    /// </summary>
    public static void UpgradeSaaSSchemaV3(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            UPDATE subscription_plans
            SET max_clients_per_nutritionist = 1,
                max_total_clients = 1,
                trial_days = 7,
                description = 'Cuenta gratuita para probar DietoExpress durante 7 días'
            WHERE code = 'free';

            -- PostgreSQL no permite referenciar el alias de la tabla objetivo
            -- dentro del JOIN del FROM. La relación con el plan se expresa en WHERE.
            UPDATE subscriptions s
            SET expires_at = u.created_at + INTERVAL '7 days'
            FROM users u, subscription_plans p
            WHERE s.tenant_id = u.tenant_id
              AND p.id = s.plan_id
              AND p.code = 'free'
              AND u.role <> 'superadmin'
              AND s.expires_at IS NULL;

            UPDATE users u
            SET subscription_status = CASE
                    WHEN s.expires_at IS NOT NULL AND s.expires_at <= NOW() THEN 'expired'
                    ELSE 'active'
                END,
                max_clients_allowed = 1
            FROM subscriptions s, subscription_plans p
            WHERE s.tenant_id = u.tenant_id
              AND p.id = s.plan_id
              AND p.code = 'free'
              AND u.role <> 'superadmin';
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('saas-v3-free-trial') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración SaaS saas-v3-free-trial aplicada correctamente.");
    }

    /// <summary>
    /// Añade el aislamiento de alimentos personalizados por tenant/usuario.
    /// Los alimentos existentes no atribuibles quedan editables únicamente por SuperAdmin.
    /// </summary>
    public static void UpgradeSaaSSchemaV4(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            ALTER TABLE foods ADD COLUMN IF NOT EXISTS tenant_id INTEGER REFERENCES tenants(id) ON DELETE SET NULL;
            ALTER TABLE foods ADD COLUMN IF NOT EXISTS created_by_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL;

            CREATE INDEX IF NOT EXISTS idx_foods_tenant_id ON foods(tenant_id);
            CREATE INDEX IF NOT EXISTS idx_foods_created_by_user_id ON foods(created_by_user_id);
        ");

        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('saas-v4-food-ownership') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración SaaS saas-v4-food-ownership aplicada correctamente.");
    }


    /// <summary>
    /// Añade expiración a los enlaces mágicos del portal de pacientes.
    /// Los tokens existentes quedan sin expiración hasta que el nutricionista los regenere.
    /// </summary>
    public static void UpgradeSaaSSchemaV5(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS access_token_expires_at TIMESTAMPTZ;
        ");

        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('saas-v5-portal-token-expiration') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración SaaS saas-v5-portal-token-expiration aplicada correctamente.");
    }


    /// <summary>Introduce archivado lógico para expedientes y dietas.</summary>
    public static void UpgradeSaaSSchemaV6(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS archived_at TIMESTAMPTZ;
            ALTER TABLE diets ADD COLUMN IF NOT EXISTS archived_at TIMESTAMPTZ;
            CREATE INDEX IF NOT EXISTS idx_clients_archived_at ON clients(archived_at);
            CREATE INDEX IF NOT EXISTS idx_diets_archived_at ON diets(archived_at);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('saas-v6-soft-delete') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración SaaS saas-v6-soft-delete aplicada correctamente.");
    }
    /// <summary>Permite conservar pacientes sin asignar y archivar nutricionistas sin perder trazabilidad.</summary>
    public static void UpgradeSaaSSchemaV8(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            ALTER TABLE users ADD COLUMN IF NOT EXISTS archived_at TIMESTAMPTZ;
            ALTER TABLE clients ALTER COLUMN user_id DROP NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_users_archived_at ON users(archived_at);
            CREATE INDEX IF NOT EXISTS idx_clients_tenant_user_id ON clients(tenant_id, user_id);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('saas-v8-nutritionist-archive-unassigned') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración SaaS saas-v8-nutritionist-archive-unassigned aplicada correctamente.");
    }

    /// <summary>Tabla de favoritos de alimentos por usuario.</summary>
    public static void UpgradeSaaSSchemaV7(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS food_favorites (
                id SERIAL PRIMARY KEY,
                user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                food_id INTEGER NOT NULL REFERENCES foods(id) ON DELETE CASCADE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                CONSTRAINT uq_food_favorites_user_food UNIQUE(user_id, food_id)
            );
            CREATE INDEX IF NOT EXISTS idx_food_favorites_user_id ON food_favorites(user_id);
            CREATE INDEX IF NOT EXISTS idx_food_favorites_food_id ON food_favorites(food_id);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('saas-v7-food-favorites') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración SaaS saas-v7-food-favorites aplicada correctamente.");
    }



    /// <summary>Expiración de confirmación de email y revocación de sesiones mediante versión de seguridad.</summary>
    public static void UpgradeSaaSSchemaV9(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            ALTER TABLE users ADD COLUMN IF NOT EXISTS email_confirmation_expires_at TIMESTAMPTZ;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS token_version INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS portal_token_version INTEGER NOT NULL DEFAULT 1;

            UPDATE users
            SET email_confirmation_expires_at = COALESCE(created_at, NOW()) + INTERVAL '24 hours'
            WHERE email_confirmation_token IS NOT NULL
              AND email_confirmation_expires_at IS NULL;

            UPDATE users
            SET token_version = 1
            WHERE token_version IS NULL;
        ");

        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('saas-v9-security-tokens') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración SaaS saas-v9-security-tokens aplicada correctamente.");
    }

}
