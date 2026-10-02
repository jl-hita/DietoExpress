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
        bool canConnect = false;
        try { canConnect = context.Database.CanConnect(); }
        catch (Exception ex) { logger.LogError(ex, "CRÍTICO: No se puede establecer conexión con la base de datos."); throw new InvalidOperationException("No se puede conectar al servidor de base de datos PostgreSQL. Verifique que el servicio esté activo y las credenciales sean válidas.", ex); }
        if (!canConnect) { var msg = "CRÍTICO: CanConnect retornó falso para la base de datos configurada."; logger.LogError(msg); throw new InvalidOperationException(msg); }
        logger.LogInformation("Conexión con PostgreSQL establecida correctamente.");
        try
        {
            context.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS tenants (id SERIAL PRIMARY KEY, legal_name VARCHAR(200) NOT NULL, trade_name VARCHAR(200), cif_nif VARCHAR(50), slug VARCHAR(100) NOT NULL, status VARCHAR(50) DEFAULT 'active', dpo_email VARCHAR(150), contact_email VARCHAR(150), contact_phone VARCHAR(50), address VARCHAR(300), logo_url TEXT, created_at TIMESTAMPTZ DEFAULT NOW());
                CREATE TABLE IF NOT EXISTS users (id SERIAL PRIMARY KEY, username VARCHAR(50) NOT NULL UNIQUE, full_name VARCHAR(100), password_hash VARCHAR(255), created_at TIMESTAMPTZ DEFAULT NOW(), last_login TIMESTAMPTZ, role VARCHAR(20) DEFAULT 'user', email VARCHAR(150) UNIQUE, email_confirmed BOOLEAN DEFAULT FALSE, email_confirmation_token VARCHAR(255), reset_password_token VARCHAR(255), reset_token_expiration TIMESTAMPTZ, google_id VARCHAR(100), provider VARCHAR(50), country VARCHAR(100), lang VARCHAR(20), clinic_name VARCHAR(150), clinic_address VARCHAR(250), clinic_phone VARCHAR(50), clinic_logo TEXT, subscription_plan VARCHAR(50) DEFAULT 'free', subscription_status VARCHAR(50) DEFAULT 'active', license_expires_at TIMESTAMPTZ, max_clients_allowed INTEGER DEFAULT 10, tenant_id INTEGER REFERENCES tenants(id));
                CREATE TABLE IF NOT EXISTS clients (id SERIAL PRIMARY KEY, user_id INTEGER REFERENCES users(id), full_name VARCHAR(100) NOT NULL, email VARCHAR(150), phone VARCHAR(50), birth_date DATE, gender VARCHAR(20), notes TEXT, created_at TIMESTAMPTZ DEFAULT NOW(), access_token VARCHAR(64), passcode_hash VARCHAR(100), last_portal_access TIMESTAMPTZ, tenant_id INTEGER REFERENCES tenants(id));
                CREATE TABLE IF NOT EXISTS biometrics (id SERIAL PRIMARY KEY, client_id INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE, measurement_date DATE NOT NULL, weight DOUBLE PRECISION, height DOUBLE PRECISION, body_fat DOUBLE PRECISION, muscle_mass DOUBLE PRECISION, visceral_fat DOUBLE PRECISION, waist DOUBLE PRECISION, hip DOUBLE PRECISION, neck DOUBLE PRECISION, triceps DOUBLE PRECISION, abdomen DOUBLE PRECISION, thigh DOUBLE PRECISION, subscapular DOUBLE PRECISION, suprailiac DOUBLE PRECISION, biceps DOUBLE PRECISION, chest DOUBLE PRECISION, axilla DOUBLE PRECISION, calf_skinfold DOUBLE PRECISION, arm_perimeter DOUBLE PRECISION, calf_perimeter DOUBLE PRECISION, wrist_diameter DOUBLE PRECISION, femur_diameter DOUBLE PRECISION, humerus_diameter DOUBLE PRECISION, notes TEXT);
            ");
        }
        catch (Exception ex) { logger.LogError(ex, "Error inicializando el esquema base."); throw; }
        logger.LogInformation("Esquema base comprobado correctamente.");
    }

    public static void UpgradeSaaSSchema(angulosodbContext context, ILogger logger) { }
    public static void UpgradeSaaSSchemaV2(angulosodbContext context, ILogger logger) { }
    public static void UpgradeSaaSSchemaV3(angulosodbContext context, ILogger logger) { }
    public static void UpgradeSaaSSchemaV4(angulosodbContext context, ILogger logger) { }
    public static void UpgradeSaaSSchemaV5(angulosodbContext context, ILogger logger) { }
    public static void UpgradeSaaSSchemaV6(angulosodbContext context, ILogger logger) { }
    public static void UpgradeSaaSSchemaV7(angulosodbContext context, ILogger logger) { }
    public static void UpgradeSaaSSchemaV8(angulosodbContext context, ILogger logger) { }
    public static void UpgradeSaaSSchemaV9(angulosodbContext context, ILogger logger) { }
}
