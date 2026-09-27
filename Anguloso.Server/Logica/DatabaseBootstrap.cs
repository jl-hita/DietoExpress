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
                SELECT 'googleClientId', 'TU_CLIENT_ID.apps.googleusercontent.com'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'googleClientId');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'usdaKey', 'TU_USDA_API_KEY'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'usdaKey');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'dominio', 'www.tusitio.com'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'dominio');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'smtpServer', 'smtp.example.com'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'smtpServer');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'smtpPort', '587'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'smtpPort');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'smtpEnableSsl', '1'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'smtpEnableSsl');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'smtpFromEmail', 'noreply@example.com'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'smtpFromEmail');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'smtpFromName', 'dietexpress'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'smtpFromName');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'smtpUser', 'TU_SMTP_USER'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'smtpUser');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'smtpPwd', 'TU_SMTP_PASSWORD'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'smtpPwd');
            ");

            logger.LogInformation("Estructura de tablas y configuración inicial verificadas y listas en PostgreSQL.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error al crear o verificar las tablas en la base de datos.");
            throw;
        }
    }
}
