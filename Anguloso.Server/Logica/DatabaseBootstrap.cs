// Bootstrap/migraciones incrementales de la base de datos. Estas rutinas permiten actualizar instalaciones existentes sin depender de que el esquema haya sido creado desde cero, por lo que cada cambio debe ser idempotente.
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
                    role VARCHAR(20) DEFAULT 'nutritionist',
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

                -- Normalización de roles heredados: el antiguo rol user siempre representó una cuenta profesional.
                -- Se ejecuta después de crear users para funcionar también en instalaciones nuevas.
                UPDATE users SET role = 'superadmin' WHERE role = 'admin';
                UPDATE users SET role = 'nutritionist' WHERE role IS NULL OR TRIM(role) = '' OR role = 'user';
                ALTER TABLE users DROP CONSTRAINT IF EXISTS ck_users_role_allowed;
                ALTER TABLE users ADD CONSTRAINT ck_users_role_allowed CHECK (role IN ('superadmin', 'nutritionist', 'clinic_admin'));

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
                CREATE TABLE IF NOT EXISTS external_api_usage (
                    id BIGSERIAL PRIMARY KEY,
                    usage_date DATE NOT NULL,
                    provider VARCHAR(80) NOT NULL,
                    operation VARCHAR(120) NOT NULL,
                    request_count INTEGER NOT NULL DEFAULT 0,
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    CONSTRAINT uq_external_api_usage_day_provider_operation
                        UNIQUE (usage_date, provider, operation),
                    CONSTRAINT ck_external_api_usage_request_count
                        CHECK (request_count >= 0)
                );

                CREATE INDEX IF NOT EXISTS idx_external_api_usage_provider_date
                    ON external_api_usage(provider, usage_date DESC);

                CREATE TABLE IF NOT EXISTS patient_conversations (
                    id BIGSERIAL PRIMARY KEY,
                    tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                    client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    archived_at TIMESTAMPTZ
                );
                CREATE UNIQUE INDEX IF NOT EXISTS uq_patient_conversations_client
                    ON patient_conversations(client_id);
                CREATE INDEX IF NOT EXISTS idx_patient_conversations_tenant
                    ON patient_conversations(tenant_id);

                CREATE TABLE IF NOT EXISTS patient_messages (
                    id BIGSERIAL PRIMARY KEY,
                    conversation_id BIGINT NOT NULL REFERENCES patient_conversations(id) ON DELETE CASCADE,
                    tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                    client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
                    sender_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
                    sender_client_id INTEGER REFERENCES clients(id) ON DELETE SET NULL,
                    body VARCHAR(5000) NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    read_at TIMESTAMPTZ,
                    edited_at TIMESTAMPTZ,
                    CONSTRAINT patient_messages_one_sender CHECK (
                        (sender_user_id IS NOT NULL AND sender_client_id IS NULL)
                        OR (sender_user_id IS NULL AND sender_client_id IS NOT NULL)
                    )
                );
                CREATE INDEX IF NOT EXISTS idx_patient_messages_conversation
                    ON patient_messages(conversation_id, created_at, id);
                CREATE INDEX IF NOT EXISTS idx_patient_messages_tenant_client
                    ON patient_messages(tenant_id, client_id, created_at);
                CREATE INDEX IF NOT EXISTS idx_patient_messages_sender_user
                    ON patient_messages(sender_user_id);

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

                -- 21. Document templates and patient documents
                CREATE TABLE IF NOT EXISTS document_templates (
                    id BIGSERIAL PRIMARY KEY,
                    tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                    name VARCHAR(200) NOT NULL,
                    description VARCHAR(1000),
                    document_type VARCHAR(50) NOT NULL DEFAULT 'other',
                    content_html TEXT,
                    file_name VARCHAR(255),
                    storage_key VARCHAR(500),
                    mime_type VARCHAR(100),
                    file_size BIGINT,
                    sha256 VARCHAR(64),
                    version INTEGER NOT NULL DEFAULT 1,
                    is_active BOOLEAN NOT NULL DEFAULT TRUE,
                    is_required_on_client_creation BOOLEAN NOT NULL DEFAULT FALSE,
                    requires_signature BOOLEAN NOT NULL DEFAULT FALSE,
                    created_by_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
                ALTER TABLE document_templates
                    ADD COLUMN IF NOT EXISTS is_required_before_consultation BOOLEAN NOT NULL DEFAULT FALSE;

                CREATE UNIQUE INDEX IF NOT EXISTS uq_document_templates_tenant_name_version
                    ON document_templates(tenant_id, name, version);
                CREATE INDEX IF NOT EXISTS idx_document_templates_tenant_active
                    ON document_templates(tenant_id, is_active);

                CREATE TABLE IF NOT EXISTS patient_documents (
                    id BIGSERIAL PRIMARY KEY,
                    tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                    client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
                    document_template_id BIGINT REFERENCES document_templates(id) ON DELETE SET NULL,
                    consultation_id BIGINT,
                    name VARCHAR(255) NOT NULL,
                    document_type VARCHAR(50) NOT NULL DEFAULT 'other',
                    status VARCHAR(30) NOT NULL DEFAULT 'pending',
                    version INTEGER NOT NULL DEFAULT 1,
                    requires_signature BOOLEAN NOT NULL DEFAULT FALSE,
                    signed_at TIMESTAMPTZ,
                    viewed_at TIMESTAMPTZ,
                    revoked_at TIMESTAMPTZ,
                    storage_key VARCHAR(500) NOT NULL,
                    original_file_name VARCHAR(255),
                    mime_type VARCHAR(100) NOT NULL,
                    file_size BIGINT NOT NULL,
                    sha256 VARCHAR(64) NOT NULL,
                    created_by_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS idx_patient_documents_tenant_client
                    ON patient_documents(tenant_id, client_id, created_at DESC);
                CREATE INDEX IF NOT EXISTS idx_patient_documents_template
                    ON patient_documents(document_template_id);
                CREATE INDEX IF NOT EXISTS idx_patient_documents_status
                    ON patient_documents(tenant_id, status);

                CREATE TABLE IF NOT EXISTS patient_document_events (
                    id BIGSERIAL PRIMARY KEY,
                    tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                    patient_document_id BIGINT NOT NULL REFERENCES patient_documents(id) ON DELETE CASCADE,
                    client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
                    event_type VARCHAR(50) NOT NULL,
                    ip_address VARCHAR(50),
                    user_agent VARCHAR(500),
                    details TEXT,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
                CREATE INDEX IF NOT EXISTS idx_patient_document_events_document
                    ON patient_document_events(patient_document_id, created_at);
                CREATE INDEX IF NOT EXISTS idx_patient_document_events_client
                    ON patient_document_events(tenant_id, client_id, created_at DESC);


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
                SELECT 'platformLegalName', '__CONFIGURE_PLATFORM_LEGAL_NAME__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'platformLegalName');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'platformLegalForm', '__CONFIGURE_PLATFORM_LEGAL_FORM__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'platformLegalForm');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'platformTaxId', '__CONFIGURE_PLATFORM_NIF_DNI__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'platformTaxId');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'platformAddress', '__CONFIGURE_PLATFORM_ADDRESS__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'platformAddress');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'platformPostalCode', '__CONFIGURE_PLATFORM_POSTAL_CODE__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'platformPostalCode');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'platformCity', '__CONFIGURE_PLATFORM_CITY__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'platformCity');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'platformProvince', '__CONFIGURE_PLATFORM_PROVINCE__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'platformProvince');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'platformCountry', '__CONFIGURE_PLATFORM_COUNTRY__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'platformCountry');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'platformContactEmail', '__CONFIGURE_PLATFORM_CONTACT_EMAIL__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'platformContactEmail');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'platformContactPhone', '__CONFIGURE_PLATFORM_CONTACT_PHONE__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'platformContactPhone');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'platformDpoEmail', '__CONFIGURE_PLATFORM_DPO_EMAIL__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'platformDpoEmail');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'platformRegistryData', '__CONFIGURE_PLATFORM_REGISTRY_DATA__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'platformRegistryData');

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

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'webPushSubject', '__CONFIGURE_WEBPUSH_SUBJECT__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'webPushSubject');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'webPushPublicKey', '__CONFIGURE_WEBPUSH_PUBLIC_KEY__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'webPushPublicKey');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'webPushPrivateKey', '__CONFIGURE_WEBPUSH_PRIVATE_KEY__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'webPushPrivateKey');
                
                INSERT INTO config (nombre_config, valor_config)
                SELECT 'geoapifyApiKey', '__CONFIGURE_GEOAPIFY_API_KEY__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'geoapifyApiKey');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'locationIqApiKey', '__CONFIGURE_LOCATIONIQ_API_KEY__'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'locationIqApiKey');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'addressPrimaryProvider', 'Geoapify'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'addressPrimaryProvider');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'addressFallbackProvider', 'LocationIQ'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'addressFallbackProvider');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'addressWarningThreshold', '0.80'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'addressWarningThreshold');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'addressFailoverThreshold', '0.90'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'addressFailoverThreshold');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'addressGeoapifyDailyLimit', '3000'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'addressGeoapifyDailyLimit');

                INSERT INTO config (nombre_config, valor_config)
                SELECT 'addressLocationIqDailyLimit', '5000'
                WHERE NOT EXISTS (SELECT 1 FROM config WHERE nombre_config = 'addressLocationIqDailyLimit');
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
    /// Repara de forma aditiva el esquema actual. No depende de schema_migrations porque una
    /// instalación histórica puede haber registrado una migración antes de completarla.
    /// Solo crea estructuras o columnas ausentes; no elimina ni modifica datos existentes.
    /// </summary>
    public static void EnsureCurrentSchema(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            -- Estructuras SaaS imprescindibles para licencias y asignaciones.
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
            CREATE INDEX IF NOT EXISTS idx_subscriptions_tenant_id ON subscriptions(tenant_id);
            CREATE INDEX IF NOT EXISTS idx_subscription_events_subscription_id ON subscription_events(subscription_id);
            CREATE INDEX IF NOT EXISTS idx_assignments_nutritionist_id ON client_nutritionist_assignments(nutritionist_id);

            -- Columnas de seguridad, archivado y onboarding que el código actual consulta.
            ALTER TABLE users ADD COLUMN IF NOT EXISTS archived_at TIMESTAMPTZ;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS email_confirmation_expires_at TIMESTAMPTZ;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS token_version INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS archived_at TIMESTAMPTZ;
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS access_token_expires_at TIMESTAMPTZ;
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS portal_token_version INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS lifecycle_status VARCHAR(40) NOT NULL DEFAULT 'pending_info';
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS lifecycle_status_changed_at TIMESTAMPTZ;
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS last_activity_at TIMESTAMPTZ;
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS onboarding_consent_at TIMESTAMPTZ;
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS onboarding_consent_version VARCHAR(40);
            ALTER TABLE clients ALTER COLUMN user_id DROP NOT NULL;
            ALTER TABLE diets ADD COLUMN IF NOT EXISTS archived_at TIMESTAMPTZ;
            ALTER TABLE recipes ADD COLUMN IF NOT EXISTS tenant_id INTEGER REFERENCES tenants(id);

            CREATE INDEX IF NOT EXISTS idx_users_archived_at ON users(archived_at);
            CREATE INDEX IF NOT EXISTS idx_clients_archived_at ON clients(archived_at);
            CREATE INDEX IF NOT EXISTS idx_clients_onboarding_consent ON clients(tenant_id, onboarding_consent_at);
            CREATE INDEX IF NOT EXISTS idx_clients_tenant_lifecycle ON clients(tenant_id, lifecycle_status, last_activity_at);
            CREATE INDEX IF NOT EXISTS idx_clients_tenant_user_id ON clients(tenant_id, user_id);

            -- Motor de automatizaciones y tareas: todas las tablas se crean de forma independiente.
            CREATE TABLE IF NOT EXISTS automation_events (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                event_type VARCHAR(120) NOT NULL,
                aggregate_type VARCHAR(80) NOT NULL,
                aggregate_id VARCHAR(120),
                payload JSONB NOT NULL DEFAULT jsonb_build_object(),
                idempotency_key VARCHAR(255) NOT NULL,
                occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_automation_events_tenant_idempotency UNIQUE (tenant_id, idempotency_key)
            );
            CREATE TABLE IF NOT EXISTS automation_jobs (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                event_id BIGINT REFERENCES automation_events(id) ON DELETE SET NULL,
                action_type VARCHAR(120) NOT NULL,
                payload JSONB NOT NULL DEFAULT jsonb_build_object(),
                scheduled_at TIMESTAMPTZ NOT NULL,
                status VARCHAR(20) NOT NULL DEFAULT 'pending',
                attempts INTEGER NOT NULL DEFAULT 0,
                max_attempts INTEGER NOT NULL DEFAULT 5,
                locked_at TIMESTAMPTZ,
                last_error VARCHAR(4000),
                idempotency_key VARCHAR(255),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                completed_at TIMESTAMPTZ
            );
            CREATE TABLE IF NOT EXISTS automation_executions (
                id BIGSERIAL PRIMARY KEY,
                job_id BIGINT NOT NULL REFERENCES automation_jobs(id) ON DELETE CASCADE,
                result VARCHAR(30) NOT NULL,
                error VARCHAR(4000),
                duration_ms BIGINT NOT NULL DEFAULT 0,
                executed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE TABLE IF NOT EXISTS professional_tasks (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                client_id INTEGER REFERENCES clients(id) ON DELETE SET NULL,
                assigned_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
                title VARCHAR(250) NOT NULL,
                description VARCHAR(4000),
                due_at TIMESTAMPTZ,
                priority VARCHAR(20) NOT NULL DEFAULT 'normal',
                status VARCHAR(20) NOT NULL DEFAULT 'open',
                source VARCHAR(120) NOT NULL DEFAULT 'manual',
                idempotency_key VARCHAR(255),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                completed_at TIMESTAMPTZ
            );
            CREATE TABLE IF NOT EXISTS automation_rules (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                rule_key VARCHAR(120) NOT NULL,
                enabled BOOLEAN NOT NULL DEFAULT TRUE,
                delay_minutes INTEGER,
                recipient_scope VARCHAR(40) NOT NULL DEFAULT 'assigned_professional',
                channels JSONB NOT NULL DEFAULT '[""in_app""]'::jsonb,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_automation_rules_tenant_key UNIQUE (tenant_id, rule_key)
            );
            CREATE TABLE IF NOT EXISTS automation_templates (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                rule_key VARCHAR(120) NOT NULL,
                patient_title VARCHAR(250),
                patient_message VARCHAR(4000),
                professional_title VARCHAR(250),
                professional_message VARCHAR(4000),
                email_subject VARCHAR(250),
                email_html TEXT,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_automation_templates_tenant_key UNIQUE (tenant_id, rule_key)
            );
            CREATE TABLE IF NOT EXISTS patient_communication_preferences (
                client_id INTEGER PRIMARY KEY REFERENCES clients(id) ON DELETE CASCADE,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                in_app_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                email_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                push_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            -- Google Calendar: si la migración original no llegó a ejecutarse, el worker debe poder arrancar.
            CREATE TABLE IF NOT EXISTS google_calendar_connections (
                id SERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                google_account_email VARCHAR(320) NOT NULL,
                calendar_id VARCHAR(500) NOT NULL DEFAULT 'primary',
                access_token_encrypted TEXT NOT NULL,
                refresh_token_encrypted TEXT,
                access_token_expires_at TIMESTAMPTZ NOT NULL,
                sync_token TEXT,
                last_synced_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_google_calendar_connection_user UNIQUE (tenant_id, user_id)
            );
            CREATE TABLE IF NOT EXISTS google_calendar_oauth_states (
                id SERIAL PRIMARY KEY,
                user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                state_hash VARCHAR(128) NOT NULL UNIQUE,
                expires_at TIMESTAMPTZ NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE TABLE IF NOT EXISTS external_calendar_events (
                id SERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                provider VARCHAR(30) NOT NULL DEFAULT 'google',
                external_event_id VARCHAR(500) NOT NULL,
                etag VARCHAR(500),
                title VARCHAR(500) NOT NULL,
                starts_at TIMESTAMPTZ NOT NULL,
                ends_at TIMESTAMPTZ NOT NULL,
                is_all_day BOOLEAN NOT NULL DEFAULT FALSE,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_external_calendar_event UNIQUE (tenant_id, user_id, provider, external_event_id)
            );

            -- Índices mínimos de cola y actividad.
            CREATE INDEX IF NOT EXISTS idx_automation_jobs_pending ON automation_jobs(status, scheduled_at, id);
            CREATE INDEX IF NOT EXISTS idx_automation_jobs_tenant ON automation_jobs(tenant_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_automation_jobs_event ON automation_jobs(event_id);
            CREATE INDEX IF NOT EXISTS idx_automation_executions_job ON automation_executions(job_id, executed_at DESC);
            CREATE INDEX IF NOT EXISTS idx_professional_tasks_tenant_status_due ON professional_tasks(tenant_id, status, due_at);
            CREATE INDEX IF NOT EXISTS idx_professional_tasks_assigned_status ON professional_tasks(assigned_user_id, status, due_at);
            CREATE INDEX IF NOT EXISTS idx_patient_communication_preferences_tenant ON patient_communication_preferences(tenant_id);
            CREATE INDEX IF NOT EXISTS idx_google_calendar_connections_tenant ON google_calendar_connections(tenant_id);
            CREATE INDEX IF NOT EXISTS idx_google_calendar_oauth_states_expiry ON google_calendar_oauth_states(expires_at);
            CREATE INDEX IF NOT EXISTS idx_external_calendar_events_block ON external_calendar_events(tenant_id, user_id, starts_at, ends_at);
        ");

        logger.LogInformation("Comprobación/reparación aditiva del esquema actual completada.");
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
            DO $$$
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
            END $$$;
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
                stripe_yearly_price_id = 'price_1UKdn20RD4LdDkcUpJchaR8Y',
                stripe_additional_monthly_price_id = 'price_1UMtJL0RD4LdDkcUxuGmtujj',
                stripe_additional_yearly_price_id = 'price_1UMtJL0RD4LdDkcUXjwqcCsG'
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
            CREATE UNIQUE INDEX IF NOT EXISTS uq_foods_external_id ON foods(external_id) WHERE external_id IS NOT NULL;
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

    /// <summary>Separa las conversaciones por etapa de asignación para evitar que un nuevo nutricionista herede el historial privado anterior.</summary>
    public static void UpgradeMessagingSchemaV2(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            ALTER TABLE patient_conversations ADD COLUMN IF NOT EXISTS assigned_nutritionist_id INTEGER REFERENCES users(id) ON DELETE SET NULL;
            ALTER TABLE patient_conversations ADD COLUMN IF NOT EXISTS closed_at TIMESTAMPTZ;

            CREATE INDEX IF NOT EXISTS idx_patient_conversations_assignment
                ON patient_conversations(client_id, assigned_nutritionist_id, closed_at);

            UPDATE patient_conversations pc
            SET assigned_nutritionist_id = a.nutritionist_id
            FROM client_nutritionist_assignments a
            WHERE pc.client_id = a.client_id
              AND a.is_active
              AND pc.closed_at IS NULL
              AND pc.assigned_nutritionist_id IS NULL;
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('messaging-v2-assignment-isolation') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración de aislamiento de conversaciones por asignación aplicada correctamente.");
    }

    /// <summary>Estado y actividad operativa del ciclo de vida de pacientes.</summary>
    // lifecycle_status es una proyección persistida para consultas rápidas; el sweep periódico puede reconstruirla
    // desde citas/check-ins/biometrías, por lo que no se trata como una fuente de verdad clínica independiente.
    public static void UpgradeAutomationSchemaV2(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS lifecycle_status VARCHAR(40) NOT NULL DEFAULT 'pending_info';
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS lifecycle_status_changed_at TIMESTAMPTZ;
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS last_activity_at TIMESTAMPTZ;
            UPDATE clients SET lifecycle_status = CASE WHEN archived_at IS NOT NULL THEN 'archived'
                WHEN birth_date IS NULL THEN 'pending_info' ELSE 'pending_first_appointment' END
                WHERE lifecycle_status IS NULL;
            CREATE INDEX IF NOT EXISTS idx_clients_tenant_lifecycle
                ON clients(tenant_id, lifecycle_status, last_activity_at);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('automation-v2-patient-lifecycle') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración de automatizaciones automation-v2-patient-lifecycle aplicada correctamente.");
    }

    /// <summary>Motor persistente de automatizaciones, scheduler y tareas profesionales.</summary>
    // PostgreSQL es la frontera final de consistencia del motor: estas restricciones siguen siendo efectivas
    // aunque dos peticiones o varios workers intenten crear el mismo evento/job simultáneamente.
    public static void UpgradeAutomationSchemaV1(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS automation_events (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                event_type VARCHAR(120) NOT NULL,
                aggregate_type VARCHAR(80) NOT NULL,
                aggregate_id VARCHAR(120),
                payload JSONB NOT NULL DEFAULT jsonb_build_object(),
                idempotency_key VARCHAR(255) NOT NULL,
                occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_automation_events_tenant_idempotency UNIQUE (tenant_id, idempotency_key)
            );

            CREATE INDEX IF NOT EXISTS idx_automation_events_tenant_occurred
                ON automation_events(tenant_id, occurred_at DESC);
            CREATE INDEX IF NOT EXISTS idx_automation_events_type_occurred
                ON automation_events(event_type, occurred_at DESC);

            CREATE TABLE IF NOT EXISTS automation_jobs (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                event_id BIGINT REFERENCES automation_events(id) ON DELETE SET NULL,
                action_type VARCHAR(120) NOT NULL,
                payload JSONB NOT NULL DEFAULT jsonb_build_object(),
                scheduled_at TIMESTAMPTZ NOT NULL,
                status VARCHAR(20) NOT NULL DEFAULT 'pending',
                attempts INTEGER NOT NULL DEFAULT 0,
                max_attempts INTEGER NOT NULL DEFAULT 5,
                locked_at TIMESTAMPTZ,
                last_error VARCHAR(4000),
                idempotency_key VARCHAR(255),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                completed_at TIMESTAMPTZ,
                CONSTRAINT automation_jobs_status_check
                    CHECK (status IN ('pending','processing','completed','failed','cancelled')),
                CONSTRAINT automation_jobs_attempts_check
                    CHECK (attempts >= 0 AND max_attempts BETWEEN 1 AND 20)
            );

            CREATE UNIQUE INDEX IF NOT EXISTS uq_automation_jobs_tenant_idempotency
                ON automation_jobs(tenant_id, idempotency_key)
                WHERE idempotency_key IS NOT NULL;
            -- El worker busca por estado y vencimiento y usa SKIP LOCKED para repartir trabajo entre instancias.
            -- Este índice mantiene esa cola acotada sin convertir la tabla completa en el punto de contención.
            CREATE INDEX IF NOT EXISTS idx_automation_jobs_pending
                ON automation_jobs(status, scheduled_at, id);
            CREATE INDEX IF NOT EXISTS idx_automation_jobs_tenant
                ON automation_jobs(tenant_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_automation_jobs_event
                ON automation_jobs(event_id);

            -- Las ejecuciones son historial independiente del estado actual del job: un job puede reintentarse
            -- varias veces y cada intento conserva su propia duración y resultado para diagnóstico/auditoría.
            CREATE TABLE IF NOT EXISTS automation_executions (
                id BIGSERIAL PRIMARY KEY,
                job_id BIGINT NOT NULL REFERENCES automation_jobs(id) ON DELETE CASCADE,
                result VARCHAR(30) NOT NULL,
                error VARCHAR(4000),
                duration_ms BIGINT NOT NULL DEFAULT 0,
                executed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_automation_executions_job
                ON automation_executions(job_id, executed_at DESC);

            CREATE TABLE IF NOT EXISTS professional_tasks (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                client_id INTEGER REFERENCES clients(id) ON DELETE SET NULL,
                assigned_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
                title VARCHAR(250) NOT NULL,
                description VARCHAR(4000),
                due_at TIMESTAMPTZ,
                priority VARCHAR(20) NOT NULL DEFAULT 'normal',
                status VARCHAR(20) NOT NULL DEFAULT 'open',
                source VARCHAR(120) NOT NULL DEFAULT 'manual',
                idempotency_key VARCHAR(255),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                completed_at TIMESTAMPTZ,
                CONSTRAINT professional_tasks_priority_check
                    CHECK (priority IN ('low','normal','high','urgent')),
                CONSTRAINT professional_tasks_status_check
                    CHECK (status IN ('open','in_progress','completed','cancelled'))
            );

            -- La tarea profesional tiene su propia barrera de idempotencia porque un reintento del worker puede
            -- llegar después de que la tarea se haya creado pero antes de que el job quede marcado como completado.
            CREATE UNIQUE INDEX IF NOT EXISTS uq_professional_tasks_tenant_idempotency
                ON professional_tasks(tenant_id, idempotency_key)
                WHERE idempotency_key IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_professional_tasks_tenant_status_due
                ON professional_tasks(tenant_id, status, due_at);
            CREATE INDEX IF NOT EXISTS idx_professional_tasks_assigned_status
                ON professional_tasks(assigned_user_id, status, due_at);
            CREATE INDEX IF NOT EXISTS idx_professional_tasks_client
                ON professional_tasks(client_id, created_at DESC);
        ");

        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('automation-v1-engine-scheduler-tasks') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración de automatizaciones automation-v1-engine-scheduler-tasks aplicada correctamente.");
    }


    /// <summary>Refuerza la integridad de versiones y del estado activo de las plantillas documentales.</summary>

    /// <summary>
    /// Infraestructura para documentos legales propios de DietoExpress y evidencias
    /// de aceptación. Los documentos se publicarán explícitamente; crear las tablas
    /// no implica que un texto pendiente de revisión jurídica pueda presentarse como
    /// condición contractual definitiva.
    /// </summary>

    /// <summary>
    /// Almacena los datos legales parametrizables de la plataforma y de cada
    /// profesional/clínica. Los valores vacíos son deliberados: no se publica
    /// ningún documento legal solo por crear esta infraestructura.
    /// </summary>
    public static void UpgradeLegalConfigurationSchemaV1(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS legal_configuration (
                id BIGSERIAL PRIMARY KEY,
                scope_type VARCHAR(20) NOT NULL,
                scope_id INTEGER NOT NULL,
                setting_key VARCHAR(120) NOT NULL,
                setting_value TEXT NOT NULL DEFAULT '',
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE (scope_type, scope_id, setting_key)
            );
            CREATE INDEX IF NOT EXISTS idx_legal_configuration_scope
                ON legal_configuration(scope_type, scope_id);
        ");

        logger.LogInformation("Esquema de configuración legal parametrizable comprobado.");
    }

    /// <summary>Provisiona documentos legales y evidencias de aceptación sin publicar automáticamente ningún texto.</summary>
    public static void UpgradeLegalComplianceSchemaV1(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS legal_documents (
                id BIGSERIAL PRIMARY KEY,
                document_key VARCHAR(100) NOT NULL,
                version INTEGER NOT NULL,
                title VARCHAR(300) NOT NULL,
                document_type VARCHAR(50) NOT NULL,
                content TEXT NOT NULL,
                status VARCHAR(20) NOT NULL DEFAULT 'draft',
                effective_from TIMESTAMPTZ NULL,
                sha256 VARCHAR(64) NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                published_at TIMESTAMPTZ NULL,
                UNIQUE (document_key, version)
            );

            CREATE INDEX IF NOT EXISTS idx_legal_documents_status
                ON legal_documents(document_key, status, version DESC);

            CREATE TABLE IF NOT EXISTS legal_acceptances (
                id BIGSERIAL PRIMARY KEY,
                user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                tenant_id INTEGER NULL REFERENCES tenants(id) ON DELETE SET NULL,
                legal_document_id BIGINT NOT NULL REFERENCES legal_documents(id) ON DELETE RESTRICT,
                document_key VARCHAR(100) NOT NULL,
                document_version INTEGER NOT NULL,
                document_sha256 VARCHAR(64) NOT NULL,
                accepted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                ip_address VARCHAR(64) NULL,
                user_agent VARCHAR(500) NULL,
                context VARCHAR(50) NOT NULL DEFAULT 'signup',
                UNIQUE (user_id, legal_document_id, document_version, context)
            );

            CREATE INDEX IF NOT EXISTS idx_legal_acceptances_user
                ON legal_acceptances(user_id, accepted_at DESC);

            CREATE INDEX IF NOT EXISTS idx_legal_acceptances_tenant
                ON legal_acceptances(tenant_id, accepted_at DESC);
        ");

        logger.LogInformation("Migración legal-compliance-v1 comprobada correctamente.");
    }

    /// <summary>Provisiona los registros operativos necesarios para tramitar derechos y brechas sin almacenar contenido clínico innecesario.</summary>
    public static void UpgradePrivacyOperationsSchemaV1(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS privacy_requests (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                requester_type VARCHAR(30) NOT NULL,
                client_id INTEGER NULL REFERENCES clients(id) ON DELETE SET NULL,
                right_type VARCHAR(30) NOT NULL,
                status VARCHAR(30) NOT NULL DEFAULT 'received',
                received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                due_at TIMESTAMPTZ NULL,
                resolved_at TIMESTAMPTZ NULL,
                decision VARCHAR(100) NULL,
                notes TEXT NULL,
                created_by INTEGER NULL REFERENCES users(id) ON DELETE SET NULL,
                updated_by INTEGER NULL REFERENCES users(id) ON DELETE SET NULL,
                CONSTRAINT privacy_requests_requester_check CHECK (requester_type IN ('patient','representative','professional','other')),
                CONSTRAINT privacy_requests_right_check CHECK (right_type IN ('access','rectification','erasure','restriction','objection','portability','automated_decision')),
                CONSTRAINT privacy_requests_status_check CHECK (status IN ('received','verifying','in_progress','awaiting_client','resolved','rejected','cancelled'))
            );
            CREATE INDEX IF NOT EXISTS idx_privacy_requests_tenant_status ON privacy_requests(tenant_id, status, received_at DESC);
            CREATE INDEX IF NOT EXISTS idx_privacy_requests_tenant_client ON privacy_requests(tenant_id, client_id, received_at DESC);

            CREATE TABLE IF NOT EXISTS privacy_incidents (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                status VARCHAR(30) NOT NULL DEFAULT 'detected',
                detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                occurred_from TIMESTAMPTZ NULL,
                occurred_to TIMESTAMPTZ NULL,
                systems_affected VARCHAR(500) NULL,
                data_categories VARCHAR(500) NULL,
                subject_categories VARCHAR(500) NULL,
                description TEXT NOT NULL,
                containment TEXT NULL,
                risk_assessment TEXT NULL,
                communications TEXT NULL,
                corrective_actions TEXT NULL,
                closed_at TIMESTAMPTZ NULL,
                created_by INTEGER NULL REFERENCES users(id) ON DELETE SET NULL,
                updated_by INTEGER NULL REFERENCES users(id) ON DELETE SET NULL,
                CONSTRAINT privacy_incidents_status_check CHECK (status IN ('detected','contained','assessing','notified','remediating','closed','false_positive'))
            );
            CREATE INDEX IF NOT EXISTS idx_privacy_incidents_tenant_status ON privacy_incidents(tenant_id, status, detected_at DESC);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('privacy-operations-v1') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración privacy-operations-v1 aplicada/comprobada correctamente.");
    }

    /// <summary>Provisiona RAT, evaluación de riesgos y decisión de EIPD por ámbito/tenant.</summary>
    /// <summary>Provisiona el almacenamiento versionado de documentos legales generados sin publicar automáticamente ningún texto.</summary>
    public static void UpgradeLegalGeneratedDocumentsSchemaV1(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS legal_generated_documents (
                id BIGSERIAL PRIMARY KEY,
                scope_type VARCHAR(20) NOT NULL,
                scope_id INTEGER NOT NULL,
                template_key VARCHAR(100) NOT NULL,
                version INTEGER NOT NULL DEFAULT 1,
                title VARCHAR(300) NOT NULL,
                content TEXT NOT NULL,
                status VARCHAR(20) NOT NULL DEFAULT 'draft',
                sha256 VARCHAR(64) NOT NULL,
                generated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_legal_generated_documents_scope_template_version
                    UNIQUE (scope_type, scope_id, template_key, version)
            );

            CREATE INDEX IF NOT EXISTS idx_legal_generated_documents_scope
                ON legal_generated_documents(scope_type, scope_id);

            CREATE INDEX IF NOT EXISTS idx_legal_generated_documents_template
                ON legal_generated_documents(scope_type, scope_id, template_key);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('legal-generated-documents-v1') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración legal-generated-documents-v1 aplicada/comprobada correctamente.");
    }

    public static void UpgradeLegalGovernanceSchemaV1(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS legal_rat_activities (
                id BIGSERIAL PRIMARY KEY,
                scope_type VARCHAR(20) NOT NULL,
                scope_id INTEGER NOT NULL,
                name VARCHAR(200) NOT NULL,
                purpose TEXT NOT NULL,
                role VARCHAR(50) NOT NULL,
                legal_basis TEXT,
                subject_categories TEXT,
                data_categories TEXT,
                special_categories TEXT,
                recipients TEXT,
                international_transfers TEXT,
                retention TEXT,
                security_measures TEXT,
                notes TEXT,
                status VARCHAR(20) NOT NULL DEFAULT 'draft',
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT legal_rat_role_check CHECK (role IN ('controller','processor','joint_controller')),
                CONSTRAINT legal_rat_status_check CHECK (status IN ('draft','active','archived'))
            );
            CREATE INDEX IF NOT EXISTS idx_legal_rat_scope ON legal_rat_activities(scope_type, scope_id);

            CREATE TABLE IF NOT EXISTS legal_risk_assessments (
                id BIGSERIAL PRIMARY KEY,
                scope_type VARCHAR(20) NOT NULL,
                scope_id INTEGER NOT NULL,
                name VARCHAR(200) NOT NULL,
                risk_description TEXT NOT NULL,
                likelihood INTEGER NOT NULL,
                impact INTEGER NOT NULL,
                measures TEXT,
                residual_risk TEXT,
                owner TEXT,
                review_date DATE,
                status VARCHAR(20) NOT NULL DEFAULT 'open',
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT legal_risk_likelihood_check CHECK (likelihood BETWEEN 1 AND 5),
                CONSTRAINT legal_risk_impact_check CHECK (impact BETWEEN 1 AND 5),
                CONSTRAINT legal_risk_status_check CHECK (status IN ('open','accepted','mitigated','closed'))
            );
            CREATE INDEX IF NOT EXISTS idx_legal_risk_scope ON legal_risk_assessments(scope_type, scope_id);

            CREATE TABLE IF NOT EXISTS legal_eipd_decisions (
                id BIGSERIAL PRIMARY KEY,
                scope_type VARCHAR(20) NOT NULL,
                scope_id INTEGER NOT NULL,
                decision VARCHAR(30) NOT NULL,
                justification TEXT NOT NULL,
                additional_measures TEXT,
                decided_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                review_date DATE,
                document_reference TEXT,
                created_by INTEGER REFERENCES users(id) ON DELETE SET NULL,
                CONSTRAINT legal_eipd_decision_check CHECK (decision IN ('required','not_required','pending'))
            );
            CREATE INDEX IF NOT EXISTS idx_legal_eipd_scope ON legal_eipd_decisions(scope_type, scope_id);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('legal-governance-v1') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración legal-governance-v1 aplicada/comprobada correctamente.");
    }

    public static void UpgradeDocumentTemplateSchemaV1(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            -- Si una instalación anterior tiene varias versiones activas de la misma plantilla,
            -- conserva activa únicamente la versión más reciente antes de crear la restricción.
            WITH ranked AS (
                SELECT id,
                       ROW_NUMBER() OVER (
                           PARTITION BY tenant_id, LOWER(name)
                           ORDER BY version DESC, id DESC
                       ) AS rn
                FROM document_templates
                WHERE is_active = TRUE
            )
            UPDATE document_templates dt
            SET is_active = FALSE,
                updated_at = NOW()
            FROM ranked r
            WHERE dt.id = r.id
              AND r.rn > 1;

            -- Una sola versión activa por plantilla lógica.
            CREATE UNIQUE INDEX IF NOT EXISTS uq_document_templates_tenant_name_active_ci
                ON document_templates(tenant_id, LOWER(name))
                WHERE is_active = TRUE;
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('document-templates-v1-integrity') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración documental document-templates-v1-integrity aplicada correctamente.");
    }

    /// <summary>Datos mínimos de onboarding y consentimiento explícito del paciente.</summary>
    public static void UpgradeAutomationSchemaV4(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS onboarding_consent_at TIMESTAMPTZ;
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS onboarding_consent_version VARCHAR(40);
            CREATE INDEX IF NOT EXISTS idx_clients_onboarding_consent
                ON clients(tenant_id, onboarding_consent_at);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('automation-v4-patient-onboarding') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración de automatizaciones automation-v4-patient-onboarding aplicada correctamente.");
    }

    /// <summary>Configuración por tenant de reglas y tiempos de automatización.</summary>
    public static void UpgradeAutomationSchemaV5(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS automation_rules (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                rule_key VARCHAR(120) NOT NULL,
                enabled BOOLEAN NOT NULL DEFAULT TRUE,
                delay_minutes INTEGER,
                recipient_scope VARCHAR(40) NOT NULL DEFAULT 'assigned_professional',
                channels JSONB NOT NULL DEFAULT '[""in_app""]'::jsonb,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT automation_rules_delay_check CHECK (delay_minutes IS NULL OR delay_minutes BETWEEN 0 AND 525600),
                CONSTRAINT automation_rules_recipient_check CHECK (recipient_scope IN ('assigned_professional','clinic_admin','patient','both')),
                CONSTRAINT uq_automation_rules_tenant_key UNIQUE (tenant_id, rule_key)
            );
            CREATE INDEX IF NOT EXISTS idx_automation_rules_tenant
                ON automation_rules(tenant_id, enabled);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('automation-v5-configurable-rules') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración de automatizaciones automation-v5-configurable-rules aplicada correctamente.");
    }

    /// <summary>Plantillas personalizables por tenant para el contenido de las automatizaciones.</summary>
    public static void UpgradeAutomationSchemaV6(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS automation_templates (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                rule_key VARCHAR(120) NOT NULL,
                patient_title VARCHAR(250),
                patient_message VARCHAR(4000),
                professional_title VARCHAR(250),
                professional_message VARCHAR(4000),
                email_subject VARCHAR(250),
                email_html TEXT,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_automation_templates_tenant_key UNIQUE (tenant_id, rule_key)
            );
            CREATE INDEX IF NOT EXISTS idx_automation_templates_tenant
                ON automation_templates(tenant_id, rule_key);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('automation-v6-templates') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración de automatizaciones automation-v6-templates aplicada correctamente.");
    }

    /// <summary>Preferencias de comunicación de cada paciente. Los valores por defecto mantienen el comportamiento actual.</summary>
    public static void UpgradeAutomationSchemaV7(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS patient_communication_preferences (
                client_id INTEGER PRIMARY KEY REFERENCES clients(id) ON DELETE CASCADE,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                in_app_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                email_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                push_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_patient_communication_preferences_tenant
                ON patient_communication_preferences(tenant_id);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('automation-v7-patient-communication-preferences') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración de automatizaciones automation-v7-patient-communication-preferences aplicada correctamente.");
    }

    /// <summary>Amplía el check-in semanal con variables estructuradas de seguimiento.</summary>
    public static void UpgradeAutomationSchemaV8(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            ALTER TABLE patient_checkins ADD COLUMN IF NOT EXISTS energy INTEGER;
            ALTER TABLE patient_checkins ADD COLUMN IF NOT EXISTS sleep_quality INTEGER;
            ALTER TABLE patient_checkins ADD COLUMN IF NOT EXISTS sleep_hours DOUBLE PRECISION;
            ALTER TABLE patient_checkins ADD COLUMN IF NOT EXISTS training INTEGER;
            CREATE INDEX IF NOT EXISTS idx_patient_checkins_client_submitted
                ON patient_checkins(client_id, submitted_at DESC);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('automation-v8-structured-followup') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración de automatizaciones automation-v8-structured-followup aplicada correctamente.");
    }

    /// <summary>Configuración persistente del panel de seguimiento por profesional.</summary>
    public static void UpgradeAutomationSchemaV9(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS professional_followup_settings (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                selected_metrics JSONB NOT NULL DEFAULT '[""adherence"",""hunger"",""energy"",""sleep_quality"",""sleep_hours"",""training"",""weight""]'::jsonb,
                period_weeks INTEGER NOT NULL DEFAULT 4,
                thresholds JSONB NOT NULL DEFAULT jsonb_build_object(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_professional_followup_settings UNIQUE (tenant_id, user_id),
                CONSTRAINT professional_followup_period_check CHECK (period_weeks BETWEEN 2 AND 12)
            );
            CREATE INDEX IF NOT EXISTS idx_professional_followup_settings_tenant
                ON professional_followup_settings(tenant_id);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('automation-v9-followup-settings') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración de automatizaciones automation-v9-followup-settings aplicada correctamente.");
    }

    /// <summary>Persistencia del flujo guiado de consulta asociado a una cita.</summary>
    public static void UpgradeAutomationSchemaV10(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS professional_consultations (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                appointment_id INTEGER NOT NULL REFERENCES patient_appointments(id) ON DELETE CASCADE,
                client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
                professional_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                consultation_type VARCHAR(20) NOT NULL DEFAULT 'follow_up',
                status VARCHAR(20) NOT NULL DEFAULT 'in_progress',
                current_step VARCHAR(60) NOT NULL DEFAULT 'summary',
                progress JSONB NOT NULL DEFAULT jsonb_build_object(),
                completed_steps JSONB NOT NULL DEFAULT jsonb_build_array(),
                started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                completed_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_professional_consultation_appointment UNIQUE (tenant_id, appointment_id),
                CONSTRAINT professional_consultation_type_check CHECK (consultation_type IN ('first','follow_up','quick')),
                CONSTRAINT professional_consultation_status_check CHECK (status IN ('in_progress','completed','cancelled'))
            );
            CREATE INDEX IF NOT EXISTS idx_professional_consultations_client
                ON professional_consultations(tenant_id, client_id, started_at DESC);
            CREATE INDEX IF NOT EXISTS idx_professional_consultations_professional
                ON professional_consultations(tenant_id, professional_id, status, started_at DESC);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('automation-v10-guided-consultations') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración de automatizaciones automation-v10-guided-consultations aplicada correctamente.");
    }

    /// <summary>Integración OAuth y sincronización bidireccional con Google Calendar.</summary>
    public static void UpgradeGoogleCalendarSchemaV1(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS google_calendar_connections (
                id SERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                google_account_email VARCHAR(320) NOT NULL,
                calendar_id VARCHAR(500) NOT NULL DEFAULT 'primary',
                access_token_encrypted TEXT NOT NULL,
                refresh_token_encrypted TEXT,
                access_token_expires_at TIMESTAMPTZ NOT NULL,
                sync_token TEXT,
                last_synced_at TIMESTAMPTZ,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_google_calendar_connection_user UNIQUE (tenant_id, user_id)
            );
            CREATE INDEX IF NOT EXISTS idx_google_calendar_connections_tenant
                ON google_calendar_connections(tenant_id);

            CREATE TABLE IF NOT EXISTS google_calendar_oauth_states (
                id SERIAL PRIMARY KEY,
                user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                state_hash VARCHAR(128) NOT NULL UNIQUE,
                expires_at TIMESTAMPTZ NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_google_calendar_oauth_states_expiry
                ON google_calendar_oauth_states(expires_at);

            CREATE TABLE IF NOT EXISTS external_calendar_events (
                id SERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                provider VARCHAR(30) NOT NULL DEFAULT 'google',
                external_event_id VARCHAR(500) NOT NULL,
                etag VARCHAR(500),
                title VARCHAR(500) NOT NULL,
                starts_at TIMESTAMPTZ NOT NULL,
                ends_at TIMESTAMPTZ NOT NULL,
                is_all_day BOOLEAN NOT NULL DEFAULT FALSE,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_external_calendar_event UNIQUE (tenant_id, user_id, provider, external_event_id)
            );
            CREATE INDEX IF NOT EXISTS idx_external_calendar_events_block
                ON external_calendar_events(tenant_id, user_id, starts_at, ends_at);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('google-calendar-v1') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración Google Calendar google-calendar-v1 aplicada correctamente.");
    }

    /// <summary>Estado de revisión profesional de los check-ins semanales.</summary>
    // La unicidad por paciente y semana hace que reintentos del portal o eventos duplicados no generen dos check-ins
    // para el mismo periodo; la automatización puede tratar esa restricción como parte de su idempotencia.
    public static void UpgradeAutomationSchemaV3(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS patient_checkins (
                id SERIAL PRIMARY KEY,
                client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                week_start DATE NOT NULL,
                submitted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                weight DOUBLE PRECISION,
                adherence INTEGER,
                hunger INTEGER,
                difficulties TEXT,
                notes TEXT,
                reviewed_at TIMESTAMPTZ,
                reviewed_by_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
                CONSTRAINT patient_checkins_client_week_key UNIQUE (client_id, week_start)
            );
            CREATE INDEX IF NOT EXISTS idx_patient_checkins_tenant_id ON patient_checkins(tenant_id);
            CREATE INDEX IF NOT EXISTS idx_patient_checkins_client_id ON patient_checkins(client_id);
            ALTER TABLE patient_checkins ADD COLUMN IF NOT EXISTS reviewed_at TIMESTAMPTZ;
            ALTER TABLE patient_checkins ADD COLUMN IF NOT EXISTS reviewed_by_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL;
            ALTER TABLE patient_checkins ADD COLUMN IF NOT EXISTS reviewed_by_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL;
            CREATE INDEX IF NOT EXISTS idx_patient_checkins_pending_review
                ON patient_checkins(tenant_id, reviewed_at, submitted_at);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('automation-v3-checkin-review') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración de automatizaciones automation-v3-checkin-review aplicada correctamente.");
    }



    /// <summary>Campos estructurados de domicilio de pacientes y coordenadas opcionales.</summary>
    public static void UpgradeClientAddressSchemaV1(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS address VARCHAR(300);
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS postal_code VARCHAR(20);
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS city VARCHAR(120);
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS province VARCHAR(120);
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS country VARCHAR(120);
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS latitude DOUBLE PRECISION;
            ALTER TABLE clients ADD COLUMN IF NOT EXISTS longitude DOUBLE PRECISION;
            CREATE INDEX IF NOT EXISTS idx_clients_tenant_city
                ON clients(tenant_id, city);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('client-address-v1') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración de domicilio de pacientes client-address-v1 aplicada/comprobada correctamente.");
    }

    /// <summary>Campos y restricciones del perfil público del directorio de profesionales.</summary>
    public static void UpgradeDirectorySchemaV1(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            ALTER TABLE users ADD COLUMN IF NOT EXISTS directory_enabled BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS online_consultations BOOLEAN NOT NULL DEFAULT FALSE;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS directory_city VARCHAR(120);
            ALTER TABLE users ADD COLUMN IF NOT EXISTS directory_bio VARCHAR(2000);
            ALTER TABLE users ADD COLUMN IF NOT EXISTS directory_specialties VARCHAR(500);
            ALTER TABLE users ADD COLUMN IF NOT EXISTS directory_slug VARCHAR(120);

            CREATE UNIQUE INDEX IF NOT EXISTS uq_users_directory_slug
                ON users(directory_slug)
                WHERE directory_slug IS NOT NULL;

            CREATE INDEX IF NOT EXISTS idx_users_directory_search
                ON users(directory_enabled, directory_city);
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('directory-v1') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración de directorio directory-v1 aplicada/comprobada correctamente.");
    }


    /// <summary>
    /// Crea el catálogo base de especializaciones y su configuración por tenant.
    /// Las reglas se almacenan como JSON para que futuras especializaciones puedan añadir
    /// parámetros sin convertir el generador de dietas en una colección de condicionales.
    /// </summary>
    public static void UpgradeSpecializationsSchemaV1(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS specializations (
                id SERIAL PRIMARY KEY,
                code VARCHAR(80) NOT NULL UNIQUE,
                name VARCHAR(160) NOT NULL,
                category VARCHAR(40) NOT NULL,
                description VARCHAR(1000),
                active BOOLEAN NOT NULL DEFAULT TRUE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS tenant_specializations (
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                specialization_id INTEGER NOT NULL REFERENCES specializations(id) ON DELETE CASCADE,
                enabled BOOLEAN NOT NULL DEFAULT TRUE,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                PRIMARY KEY (tenant_id, specialization_id)
            );
            CREATE INDEX IF NOT EXISTS idx_tenant_specializations_tenant
                ON tenant_specializations(tenant_id, enabled);

            CREATE TABLE IF NOT EXISTS client_specializations (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
                specialization_id INTEGER NOT NULL REFERENCES specializations(id) ON DELETE RESTRICT,
                notes VARCHAR(2000),
                created_by_user_id INTEGER REFERENCES users(id) ON DELETE SET NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_client_specialization UNIQUE (tenant_id, client_id, specialization_id)
            );
            CREATE INDEX IF NOT EXISTS idx_client_specializations_client
                ON client_specializations(tenant_id, client_id);
            -- Perfil estructurado por paciente/especialización. La configuración clínica no se
            -- mezcla con notas libres para poder evolucionarla sin acoplar el núcleo del paciente.
            CREATE TABLE IF NOT EXISTS client_specialization_profiles (
                id BIGSERIAL PRIMARY KEY,
                tenant_id INTEGER NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
                client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
                specialization_id INTEGER NOT NULL REFERENCES specializations(id) ON DELETE CASCADE,
                configuration JSONB NOT NULL DEFAULT '{}'::jsonb,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_client_specialization_profile UNIQUE (tenant_id, client_id, specialization_id)
            );
            CREATE INDEX IF NOT EXISTS idx_client_specialization_profiles_client
                ON client_specialization_profiles(tenant_id, client_id);


            -- Refuerza a nivel de base de datos que una asignación nunca pueda
            -- combinar un cliente con el tenant equivocado.
            CREATE UNIQUE INDEX IF NOT EXISTS uq_clients_tenant_id_id
                ON clients(tenant_id, id);

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM pg_constraint
                    WHERE conname = 'fk_client_specializations_client_tenant'
                ) THEN
                    ALTER TABLE client_specializations
                        ADD CONSTRAINT fk_client_specializations_client_tenant
                        FOREIGN KEY (tenant_id, client_id)
                        REFERENCES clients(tenant_id, id)
                        ON DELETE CASCADE;
                END IF;
            END $$;

            CREATE TABLE IF NOT EXISTS specialization_rules (
                id BIGSERIAL PRIMARY KEY,
                specialization_id INTEGER NOT NULL REFERENCES specializations(id) ON DELETE CASCADE,
                rule_code VARCHAR(120) NOT NULL,
                rule_type VARCHAR(50) NOT NULL,
                configuration JSONB NOT NULL DEFAULT '{{}}'::jsonb,
                priority INTEGER NOT NULL DEFAULT 100,
                active BOOLEAN NOT NULL DEFAULT TRUE,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                CONSTRAINT uq_specialization_rule_code UNIQUE (specialization_id, rule_code)
            );
            CREATE INDEX IF NOT EXISTS idx_specialization_rules_specialization
                ON specialization_rules(specialization_id, active, priority);

            INSERT INTO specializations(code,name,category,description)
            VALUES
                ('vegan','Vegana','dietary','Patrón alimentario sin alimentos de origen animal.'),
                ('vegetarian','Vegetariana','dietary','Patrón alimentario vegetariano configurable por el profesional.'),
                ('flexitarian','Flexitariana','dietary','Patrón predominantemente vegetal con consumo ocasional de alimentos animales.'),
                ('pescatarian','Pescetariana','dietary','Patrón sin carne terrestre con consumo de pescado y marisco.'),
                ('sports_nutrition','Nutrición deportiva','sports','Especialización para objetivos relacionados con entrenamiento y rendimiento.'),
                ('weight_management','Pérdida de peso y obesidad','clinical','Especialización completa para manejo del peso, composición corporal, pérdida de grasa y seguimiento ponderal individualizado.'),
                ('diabetes','Diabetes','clinical','Condición clínica que requiere criterios específicos definidos y revisados por el profesional.'),
                ('hypertension','Hipertensión','clinical','Condición clínica que puede requerir parámetros dietéticos específicos.'),
                ('dyslipidemia','Dislipemia','clinical','Condición clínica relacionada con el perfil lipídico.'),
                ('celiac','Enfermedad celíaca','clinical','Condición que requiere exclusión estricta de gluten según criterio profesional.'),
                ('lactose_intolerance','Intolerancia a la lactosa','clinical','Intolerancia alimentaria con adaptación individual de alimentos y cantidades.'),
                ('tree_nut_allergy','Alergia a frutos secos','allergy','Alergia alimentaria que requiere exclusión de frutos secos según criterio profesional.'),
                ('peanut_allergy','Alergia al cacahuete','allergy','Alergia alimentaria que requiere exclusión de cacahuete según criterio profesional.'),
                ('soy_allergy','Alergia a la soja','allergy','Alergia alimentaria que requiere exclusión de soja según criterio profesional.'),
                ('fodmap','Enfoque bajo FODMAP','clinical','Protocolo dietético configurable para síntomas digestivos, bajo supervisión profesional.'),
                ('renal','Enfermedad renal','clinical','Condición clínica que puede requerir restricciones y objetivos individualizados.'),
                ('pregnancy','Embarazo','life_stage','Especialización para seguimiento nutricional durante el embarazo.'),
                ('lactation','Lactancia','life_stage','Especialización para seguimiento nutricional durante la lactancia.'),
                ('pediatric','Nutrición pediátrica','life_stage','Especialización para población infantil y adolescente.')
            ON CONFLICT (code) DO UPDATE SET
                name = EXCLUDED.name,
                category = EXCLUDED.category,
                description = EXCLUDED.description,
                active = TRUE;

    INSERT INTO specialization_rules(specialization_id, rule_code, rule_type, configuration, priority, active)
            SELECT s.id, v.rule_code, v.rule_type, v.configuration::jsonb, v.priority, TRUE
            FROM specializations s
            JOIN (VALUES
                ('vegan','exclude_food_keywords','food_exclusion','{{""required_flags"":[""animal""],""keywords"":[""carne"",""pollo"",""pavo"",""cerdo"",""ternera"",""vacuno"",""cordero"",""jamon"",""jamón"",""embutido"",""salchicha"",""chorizo"",""atun"",""atún"",""salmon"",""salmón"",""pescado"",""marisco"",""gamba"",""camaron"",""camarón"",""mejillon"",""mejillón"",""huevo"",""leche"",""queso"",""yogur"",""yogurt"",""nata"",""mantequilla"",""miel"",""gelatina""]}}',10),
                ('vegetarian','exclude_food_keywords','food_exclusion','{{""required_flags"":[""meat"",""fish"",""gelatin""],""keywords"":[""carne"",""pollo"",""pavo"",""cerdo"",""ternera"",""vacuno"",""cordero"",""jamon"",""jamón"",""embutido"",""salchicha"",""chorizo"",""atun"",""atún"",""salmon"",""salmón"",""pescado"",""marisco"",""gamba"",""camaron"",""camarón"",""mejillon"",""mejillón"",""gelatina""]}}',10),
                ('pescatarian','exclude_food_keywords','food_exclusion','{{""required_flags"":[""meat"",""gelatin""],""keywords"":[""carne"",""pollo"",""pavo"",""cerdo"",""ternera"",""vacuno"",""cordero"",""jamon"",""jamón"",""embutido"",""salchicha"",""chorizo"",""gelatina""]}}',10),
                ('celiac','exclude_food_gluten','food_exclusion','{{""required_flags"":[""gluten""],""keywords"":[""gluten"",""trigo"",""cebada"",""centeno"",""espelta"",""avena""]}}',10),
                ('lactose_intolerance','exclude_food_lactose','food_exclusion','{{""required_flags"":[""lactose""],""keywords"":[""lactosa"",""leche"",""suero"",""lácteo"",""lacteo""]}}',10),
                ('tree_nut_allergy','exclude_food_tree_nuts','food_exclusion','{{""required_flags"":[""tree_nut""],""keywords"":[""almendra"",""almendras"",""nuez"",""nueces"",""avellana"",""avellanas"",""anacardo"",""anacardos"",""pistacho"",""pistachos"",""pacana"",""pacanas"",""macadamia"",""macadamias"",""nuez de brasil""]}}',10),
                ('peanut_allergy','exclude_food_peanut','food_exclusion','{{""required_flags"":[""peanut""],""keywords"":[""cacahuete"",""cacahuetes"",""maní"",""mani"",""peanut"",""peanuts""]}}',10),
                ('soy_allergy','exclude_food_soy','food_exclusion','{{""required_flags"":[""soy""],""keywords"":[""soja"",""soya"",""soy"",""tofu"",""tempeh"",""edamame""]}}',10),
                ('sports_nutrition','sports_default_protein','nutrition_profile','{{""protein_g_per_kg"":1.6,""protein_min_g_per_kg"":1.4,""protein_max_g_per_kg"":2.0}}',10),
                ('sports_nutrition','sports_guidance','clinical_guidance','{{""message"":""Nutrición deportiva: usar 1,4-2,0 g de proteína/kg/día como rango de referencia inicial en personas activas y ajustar según deporte, volumen de entrenamiento, composición corporal y objetivo."" }}',20),
                ('sports_nutrition','sports_carbohydrate_guidance','clinical_guidance','{{""message"":""Nutrición deportiva: individualizar hidratos según disciplina, volumen, intensidad, fase de entrenamiento y objetivo. Priorizar disponibilidad suficiente de carbohidratos alrededor de sesiones exigentes cuando proceda."" }}',21),
                ('sports_nutrition','sports_hydration_guidance','clinical_guidance','{{""message"":""Nutrición deportiva: individualizar hidratación según peso, duración, ambiente, tasa de sudoración y pérdidas de sodio; evitar pautas rígidas cuando falten datos."" }}',22),
                ('weight_management','weight_management_guidance','clinical_guidance','{{""message"":""Manejo del peso: priorizar un déficit energético sostenible, preservar masa muscular, monitorizar evolución y ajustar según respuesta. Evitar objetivos automáticos extremos y adaptar el plan al contexto clínico."" }}',20),
                ('weight_management','weight_management_protein','nutrition_profile','{{""protein_g_per_kg"":1.6,""protein_min_g_per_kg"":1.2,""protein_max_g_per_kg"":2.0}}',10),
                ('diabetes','diabetes_guidance','clinical_guidance','{{""message"":""Diabetes: individualizar energía, cantidad y distribución de hidratos y revisar medicación, glucemia y objetivos clínicos. Priorizar calidad de los hidratos, fibra y limitar azúcares libres."" }}',20),
                ('hypertension','hypertension_guidance','clinical_guidance','{{""message"":""Hipertensión: priorizar un patrón bajo en sodio/sal y rico en alimentos poco procesados, verduras y frutas. El objetivo de sodio debe individualizarse según criterio clínico."" }}',20),
                ('dyslipidemia','dyslipidemia_guidance','clinical_guidance','{{""message"":""Dislipemia: priorizar grasas insaturadas, fibra y alimentos mínimamente procesados; limitar grasas saturadas y trans y adaptar el plan al perfil lipídico."" }}',20),
                ('renal','renal_guidance','clinical_guidance','{{""message"":""Enfermedad renal: no aplicar automáticamente objetivos de proteína, sodio, potasio o fósforo. Individualizar según función renal, estadio, tratamiento y criterio clínico."" }}',20),
                ('pregnancy','pregnancy_guidance','clinical_guidance','{{""message"":""Embarazo: individualizar energía y micronutrientes según etapa gestacional y seguimiento profesional. No usar restricciones automáticas que puedan comprometer la adecuación nutricional."" }}',20),
                ('lactation','lactation_guidance','clinical_guidance','{{""message"":""Lactancia: individualizar energía, hidratación y micronutrientes según demanda y situación clínica. Evitar restricciones innecesarias que comprometan la adecuación nutricional."" }}',20),
                ('pediatric','pediatric_guidance','clinical_guidance','{{""message"":""Nutrición pediátrica: individualizar por edad, crecimiento, desarrollo y situación clínica. No aplicar objetivos de adulto de forma automática."" }}',20)
            ) AS v(code,rule_code,rule_type,configuration,priority)
              ON s.code=v.code
            ON CONFLICT (specialization_id, rule_code) DO UPDATE SET
                rule_type=EXCLUDED.rule_type,
                configuration=EXCLUDED.configuration,
                priority=EXCLUDED.priority,
                active=TRUE;

                    -- Los tenants existentes reciben inicialmente todo el catálogo activo.
            -- Las filas explícitas permiten posteriormente desactivar especializaciones
            -- sin afectar al catálogo global ni a otros tenants.
            INSERT INTO tenant_specializations(tenant_id, specialization_id, enabled)
            SELECT t.id, s.id, TRUE
            FROM tenants t
            CROSS JOIN specializations s
            WHERE s.active
            ON CONFLICT (tenant_id, specialization_id) DO NOTHING;
        ");

        // Clasificación dietética estructurada de alimentos. Se mantiene en la tabla de alimentos
        // para que las especializaciones no dependan exclusivamente de coincidencias de texto.
        context.Database.ExecuteSqlRaw(@"
            ALTER TABLE foods ADD COLUMN IF NOT EXISTS dietary_flags TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[];
            CREATE INDEX IF NOT EXISTS idx_foods_dietary_flags_gin
                ON foods USING GIN (dietary_flags);

            CREATE OR REPLACE FUNCTION classify_food_dietary_flags()
            RETURNS trigger AS $$
            DECLARE
                text_to_classify TEXT;
                flags TEXT[] := ARRAY[]::TEXT[];
            BEGIN
                text_to_classify := lower(coalesce(NEW.name,'') || ' ' || coalesce(NEW.category,''));

                IF text_to_classify ~* '(^|[^[:alnum:]])(carne|cerdo|jam[oó]n|embutido|salchicha|chorizo|pollo|pavo|ternera|vacuno|cordero|cabrito|conejo|buey|hamburguesa|bacon|tocino|salami|mortadela)($|[^[:alnum:]])' THEN
                    flags := array_append(flags, 'meat');
                END IF;
                IF text_to_classify ~* '(^|[^[:alnum:]])(pescado|at[uú]n|salm[oó]n|merluza|bacalao|sardina|caballa|dorada|lubina|trucha|anchoa|marisco|gamba|camar[oó]n|mejill[oó]n|almeja|calamar|pulpo|sepia)($|[^[:alnum:]])' THEN
                    flags := array_append(flags, 'fish');
                END IF;
                IF text_to_classify ~* '(^|[^[:alnum:]])(huevo|huevos|clara|yema|ovoproducto|mayonesa)($|[^[:alnum:]])' THEN
                    flags := array_append(flags, 'egg');
                END IF;
                IF text_to_classify ~* '(^|[^[:alnum:]])(leche|queso|yogur|yogurt|nata|mantequilla|suero|k[eé]fir|l[aá]cteo|l[aá]cteos|case[ií]na|whey)($|[^[:alnum:]])' THEN
                    flags := array_append(flags, 'dairy');
                END IF;
                IF text_to_classify ~* '(^|[^[:alnum:]])miel($|[^[:alnum:]])' THEN
                    flags := array_append(flags, 'honey');
                END IF;
                IF text_to_classify ~* '(^|[^[:alnum:]])gelatina($|[^[:alnum:]])' THEN
                    flags := array_append(flags, 'gelatin');
                END IF;
                IF text_to_classify ~* '(^|[^[:alnum:]])(trigo|harina de trigo|cebada|centeno|espelta|gluten|pan|pasta|cusc[uú]s|galleta|galletas|bizcocho|bizcochos)($|[^[:alnum:]])' THEN
                    flags := array_append(flags, 'gluten');
                END IF;
                IF text_to_classify ~* '(^|[^[:alnum:]])(lactosa|leche|l[aá]cteo|l[aá]cteos|suero l[aá]cteo|case[ií]na|nata|yogur|yogurt)($|[^[:alnum:]])' THEN
                    flags := array_append(flags, 'lactose');
                END IF;
                IF text_to_classify ~* '(^|[^[:alnum:]])(almendra|almendras|nuez|nueces|avellana|avellanas|anacardo|anacardos|pistacho|pistachos|pacana|pacanas|macadamia|macadamias|nuez de brasil)($|[^[:alnum:]])' THEN
                    flags := array_append(flags, 'tree_nut');
                END IF;
                IF text_to_classify ~* '(^|[^[:alnum:]])(cacahuete|cacahuetes|man[ií]|peanut|peanuts)($|[^[:alnum:]])' THEN
                    flags := array_append(flags, 'peanut');
                END IF;
                IF text_to_classify ~* '(^|[^[:alnum:]])(soja|soya|soy|tofu|tempeh|edamame)($|[^[:alnum:]])' THEN
                    flags := array_append(flags, 'soy');
                END IF;

                -- Solo las señales inequívocamente animales convierten el alimento en 'animal'.
                -- Las flags de gluten, lactosa y alérgenos vegetales no deben hacerlo.
                IF flags && ARRAY['meat','fish','egg','dairy','honey','gelatin']::TEXT[] THEN
                    flags := array_append(flags, 'animal');
                END IF;
                NEW.dietary_flags := flags;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            DROP TRIGGER IF EXISTS trg_foods_dietary_flags ON foods;
            CREATE TRIGGER trg_foods_dietary_flags
                BEFORE INSERT OR UPDATE OF name, category ON foods
                FOR EACH ROW EXECUTE FUNCTION classify_food_dietary_flags();

            -- Recalcular las banderas existentes haciendo que el trigger ejecute la misma clasificación
            -- que se aplicará automáticamente a los nuevos alimentos y a los cambios de nombre/categoría.
            UPDATE foods SET name = name WHERE dietary_flags = ARRAY[]::TEXT[];
        ");

        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('specializations-v1') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración de especializaciones specializations-v1 aplicada correctamente.");
    }

    /// <summary>Extiende las citas existentes con modalidad y metadatos de videollamada.</summary>
    public static void UpgradeOnlineConsultationSchemaV1(angulosodbContext context, ILogger logger)
    {
        context.Database.ExecuteSqlRaw(@"
            ALTER TABLE patient_appointments
                ADD COLUMN IF NOT EXISTS modality VARCHAR(20) NOT NULL DEFAULT 'in_person',
                ADD COLUMN IF NOT EXISTS video_provider VARCHAR(30),
                ADD COLUMN IF NOT EXISTS video_room_name VARCHAR(100),
                ADD COLUMN IF NOT EXISTS video_room_url VARCHAR(500),
                ADD COLUMN IF NOT EXISTS video_expires_at TIMESTAMPTZ;

            ALTER TABLE patient_appointments
                DROP CONSTRAINT IF EXISTS patient_appointments_modality_check;

            ALTER TABLE patient_appointments
                ADD CONSTRAINT patient_appointments_modality_check
                CHECK (modality IN ('in_person','online'));

            CREATE INDEX IF NOT EXISTS idx_patient_appointments_online
                ON patient_appointments(tenant_id, nutritionist_id, starts_at)
                WHERE modality = 'online' AND status IN ('requested','confirmed');

            CREATE TABLE IF NOT EXISTS schema_migrations (
                id VARCHAR(200) PRIMARY KEY,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
        ");
        context.Database.ExecuteSqlRaw("INSERT INTO schema_migrations(id) VALUES ('online-consultation-v1') ON CONFLICT (id) DO NOTHING;");
        logger.LogInformation("Migración de consulta online online-consultation-v1 aplicada correctamente.");
    }

}
