using System;
using System.Collections.Generic;
using System.Linq;
using Anguloso.Server.Models;

namespace Anguloso.Server.Logica;

/// <summary>
/// Motor determinista de reglas dietoterapéuticas. No sustituye el criterio clínico:
/// convierte datos estructurados del paciente en objetivos y señales de optimización
/// que el profesional puede revisar. Las restricciones de seguridad siguen resolviéndose
/// antes de esta capa.
/// Lugar en el flujo: se ejecuta al inicio de <see cref="DietGeneratorService"/> (entrada), antes de elegir alimentos.
/// Produce un <see cref="DietTherapyProfile"/> con (a) energía estimada, (b) pesos de nutrientes que sesgan la selección
/// de alimentos (valor positivo penaliza, negativo favorece) y (c) frecuencias semanales objetivo para el motor de menús.
/// Las reglas por patología son criterio de producto (señales blandas), no prescripciones clínicas; los pesos
/// numéricos tienen origen no documentado y el texto de <c>Rules</c> es lo que se muestra al profesional.
/// </summary>
public sealed class DietTherapyRuleEngine
{
    /// <summary>
    /// Construye el perfil dietoterapéutico. La energía solo se estima si no hay objetivo explícito. Las condiciones del
    /// paciente solo añaden pesos y avisos; nunca excluyen alimentos (eso lo hacen las restricciones de seguridad).
    /// </summary>
    public DietTherapyProfile BuildProfile(
        clients client,
        medical_history? medical,
        lifestyle_history? lifestyle,
        biometrics? latestBiometrics,
        string dietType,
        double explicitOrDefaultKcal,
        bool hasExplicitKcal)
    {
        var profile = new DietTherapyProfile();

        if (!hasExplicitKcal)
            profile.TargetKcal = EstimateEnergy(client, latestBiometrics, dietType, explicitOrDefaultKcal);

        if (medical?.diabetes == true)
        {
            profile.Rules.Add("Diabetes: priorizar distribución regular de hidratos, fibra y alimentos mínimamente procesados.");
            profile.NutrientWeights["sugar"] = 1.6;
            profile.NutrientWeights["fiber"] = -0.8;
            profile.MealDistributionWeight = 1.25;
        }

        if (medical?.hypertension == true)
        {
            profile.Rules.Add("Hipertensión: reducir la densidad de sodio y favorecer alimentos frescos.");
            profile.NutrientWeights["salt"] = 2.0;
            profile.NutrientWeights["potassium"] = -0.25;
        }

        if (medical?.hypothyroidism == true)
            profile.Rules.Add("Hipotiroidismo: mantener una pauta nutricional equilibrada; no aplicar exclusiones automáticas sin indicación profesional.");

        if (!string.IsNullOrWhiteSpace(medical?.other_pathologies))
            profile.Rules.Add("Otras patologías declaradas: revisar y validar el plan con las reglas clínicas configuradas por el profesional.");

        if (dietType.Contains("deport", StringComparison.OrdinalIgnoreCase) ||
            dietType.Contains("fitness", StringComparison.OrdinalIgnoreCase) ||
            dietType.Contains("alta", StringComparison.OrdinalIgnoreCase))
        {
            profile.Rules.Add("Rendimiento: distribuir proteína y carbohidratos entre las comidas para facilitar disponibilidad energética.");
            profile.MealDistributionWeight = Math.Max(profile.MealDistributionWeight, 1.15);
        }

        if (dietType.Contains("peso", StringComparison.OrdinalIgnoreCase) ||
            dietType.Contains("adelgaz", StringComparison.OrdinalIgnoreCase) ||
            dietType.Contains("obes", StringComparison.OrdinalIgnoreCase))
        {
            profile.Rules.Add("Control de peso: priorizar densidad nutricional, saciedad y adherencia sobre alimentos de baja calidad nutricional.");
            profile.NutrientWeights["fiber"] = -0.6;
            profile.NutrientWeights["sugar"] = Math.Max(profile.NutrientWeights.GetValueOrDefault("sugar"), 0.7);
        }

        if (!string.IsNullOrWhiteSpace(lifestyle?.work_schedule))
            profile.Rules.Add("Horario laboral considerado para mantener una distribución de comidas compatible con la rutina.");

        // Frecuencias semanales objetivo (por día para fruta/verdura; por semana /7 para el resto). Criterio de producto.
        // Hoy el motor de menús solo consume "legumbre" y "pescado_marisco"; el resto es informativo.
        profile.FrequencyTargets["fruta"] = 1;
        profile.FrequencyTargets["verdura"] = 2;
        profile.FrequencyTargets["legumbre"] = 3.0 / 7.0;
        profile.FrequencyTargets["pescado_marisco"] = 2.0 / 7.0;
        profile.FrequencyTargets["frutos_secos"] = 4.0 / 7.0;

        // Umbral de grasa corporal > 35 %: criterio de producto, origen no documentado. Solo genera un aviso.
        if (latestBiometrics?.body_fat is > 35)
            profile.Rules.Add("Composición corporal: priorizar saciedad y densidad nutricional; revisar el objetivo energético antes de prescribir.");

        return profile;
    }

    /// <summary>
    /// Aplica el déficit configurado sin exceder el ritmo de pérdida semanal indicado.
    /// La conversión usa una aproximación operativa de 7.700 kcal/kg; el resultado siempre
    /// respeta el mínimo configurado y debe ser revisado por el profesional.
    /// </summary>
    /// <summary>
    /// Suelo energético del generador (kcal/día). Un objetivo calculado automáticamente nunca baja de aquí aunque el
    /// déficit lo permita: que una cifra sea matemáticamente válida no la hace una prescripción razonable.
    /// Coincide con el rango operativo que valida <c>DietSemanticValidationService</c>.
    /// </summary>
    public const double MinimumSafeKcal = 1200;
    // Techo operativo del generador (mismo rango que valida la validación semántica). Criterio de producto.
    // Nota: ApplyWeightManagementTarget acota con 5000 (su propio techo), no con esta constante.
    public const double MaximumKcal = 4500;

    public static double ApplyWeightManagementTarget(
        double maintenanceKcal,
        double? deficitPercent,
        double? targetRateKgPerWeek,
        double? minimumKcal)
    {
        if (!double.IsFinite(maintenanceKcal) || maintenanceKcal <= 0)
            maintenanceKcal = 2000;

        // Déficit por defecto 15 % y máximo 20 % (criterio de producto; el profesional puede fijar el mínimo en el perfil).
        var percent = Math.Clamp(deficitPercent ?? 15, 0, 20);
        var percentDeficit = maintenanceKcal * percent / 100.0;
        var rate = targetRateKgPerWeek.GetValueOrDefault();
        var rateDeficit = double.IsFinite(rate) && rate > 0
            ? rate * 7700.0 / 7.0
            : double.PositiveInfinity;

        // Aplicar el más conservador de los dos límites: déficit porcentual y ritmo objetivo.
        var target = maintenanceKcal - Math.Min(percentDeficit, rateDeficit);
        if (minimumKcal.HasValue && double.IsFinite(minimumKcal.Value) && minimumKcal.Value > 0)
            target = Math.Max(target, minimumKcal.Value);

        // El suelo es el mayor entre el mínimo de seguridad del generador y el mínimo del perfil del profesional.
        var floor = Math.Min(5000, Math.Max(MinimumSafeKcal, minimumKcal.HasValue && double.IsFinite(minimumKcal.Value) ? minimumKcal.Value : 0));
        return Math.Round(Math.Clamp(target, floor, 5000), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Estima el gasto energético diario: metabolismo basal x factor de actividad fijo. Con % de grasa válido (3-70) usa una
    /// ecuación basada en masa magra (forma de Cunningham: 370 + 21,6 x masa magra); si no, Mifflin-St Jeor
    /// (10 peso + 6,25 talla - 5 edad + 5 o -161 según sexo). Sin peso y talla devuelve <paramref name="fallbackKcal"/>.
    /// </summary>
    public static double EstimateEnergy(
        clients client,
        biometrics? biometric,
        string dietType,
        double fallbackKcal)
    {
        if (biometric?.weight is not > 0 || biometric.height is not > 0)
            return fallbackKcal;

        var age = client.birth_date.HasValue
            // Edad mínima 18 (la estimación está pensada para adultos); sin fecha de nacimiento se asume 40. Criterio de producto.
            ? Math.Max(18, (DateTime.UtcNow.Date - client.birth_date.Value.ToDateTime(TimeOnly.MinValue)).TotalDays / 365.2425)
            : 40;

        var weight = biometric.weight.Value;
        var height = biometric.height.Value;

        double bmr;
        if (biometric.body_fat is > 3 and < 70)
        {
            var leanMass = weight * (1 - biometric.body_fat.Value / 100.0);
            bmr = 370 + (21.6 * leanMass);
        }
        else
        {
            bmr = 10 * weight + 6.25 * height - 5 * age + (client.gender?.Equals("female", StringComparison.OrdinalIgnoreCase) == true ? -161 : 5);
        }

        // Sin un nivel de actividad estructurado no se inventa un PAL individual:
        // usamos un factor neutro y dejamos que el profesional ajuste el objetivo.
        var estimated = bmr * 1.45;

        // Ajustes por tipo de dieta: -15 % para pérdida de peso, +8 % para ganancia. Criterio de producto, origen no documentado.
        // Rango final 1200-4500 kcal, igual que MinimumSafeKcal/MaximumKcal.
        if (dietType.Contains("perdida", StringComparison.OrdinalIgnoreCase) ||
            dietType.Contains("peso", StringComparison.OrdinalIgnoreCase) ||
            dietType.Contains("adelgaz", StringComparison.OrdinalIgnoreCase))
            estimated *= 0.85;

        if (dietType.Contains("ganancia", StringComparison.OrdinalIgnoreCase) ||
            dietType.Contains("masa", StringComparison.OrdinalIgnoreCase))
            estimated *= 1.08;

        return Math.Clamp(estimated, 1200, 4500);
    }
}

/// <summary>
/// Resultado de <see cref="DietTherapyRuleEngine"/>: energía estimada, avisos legibles (<c>Rules</c>), pesos por nutriente
/// (claves: sugar, salt, fiber, potassium) y frecuencias semanales objetivo por grupo de alimentos.
/// </summary>
public sealed class DietTherapyProfile
{
    public double TargetKcal { get; set; }
    // Se calcula pero actualmente no lo consume el generador (reservado para el reparto por comidas).
    public double MealDistributionWeight { get; set; } = 1.0;
    public List<string> Rules { get; } = new();
    public Dictionary<string, double> NutrientWeights { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> FrequencyTargets { get; } = new(StringComparer.OrdinalIgnoreCase);
}
