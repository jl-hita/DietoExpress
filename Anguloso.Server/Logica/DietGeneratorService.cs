using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anguloso.Server.Logica;

/// <summary>
/// Orquestador de la generación de dietas. Flujo: (1) resuelve cliente, perfiles de especialización y objetivos
/// energéticos/macros; (2) carga el catálogo de alimentos y lo filtra por seguridad (alergias, intolerancias,
/// exclusiones, visibilidad por tenant); (3) delega en <see cref="DietMenuEngine"/> el reparto por comidas/roles
/// culinarios y el ajuste de raciones; (4) opcionalmente inyecta recetas y reequilibra; (5) valida con
/// <see cref="DietSemanticValidationService"/> (reintenta con otro seed si falla); (6) añade el resumen de micronutrientes.
/// No persiste nada: devuelve el DTO ya validado.
/// </summary>
public class DietGeneratorService
{
    private readonly angulosodbContext _context;
    private readonly SpecializationRulesService _specializationRulesService;
    private readonly AdvancedDietOptimizerService _advancedDietOptimizer;
    private readonly DietTherapyRuleEngine _dietTherapyRuleEngine;
    private readonly DietSemanticValidationService _semanticValidationService;
    private readonly ILogger<DietGeneratorService>? _logger;

    public DietGeneratorService(angulosodbContext context, SpecializationRulesService specializationRulesService, DietSemanticValidationService semanticValidationService, ILogger<DietGeneratorService>? logger = null)
    {
        _context = context;
        _specializationRulesService = specializationRulesService;
        _advancedDietOptimizer = new AdvancedDietOptimizerService(context);
        _dietTherapyRuleEngine = new DietTherapyRuleEngine();
        _semanticValidationService = semanticValidationService;
        _logger = logger;
    }

    // La generación construye una dieta a partir de objetivos nutricionales, alimentos permitidos
    // y restricciones del paciente; los filtros de tenant se aplican antes de optimizar las cantidades.
    public async Task<DietDetailDto> GenerateDietAsync(GenerateDietRequestDto request, int? tenantId, int userId, bool canUseTenantLocalFoods, CancellationToken cancellationToken = default)
    {
        var generationTimer = Stopwatch.StartNew();
        var profileTimer = Stopwatch.StartNew();
        var profileElapsedMs = 0L;
        var foodsLoadElapsedMs = 0L;
        var foodFilteringElapsedMs = 0L;
        var generationAttemptCount = 0;

        // 1. Resolver cliente y especializaciones antes de fijar los objetivos automáticos.
        var hasExplicitKcal = request.TargetKcal > 0;
        double targetKcal = hasExplicitKcal ? request.TargetKcal : 2000;
        clients? client = null;
        SpecializationRulesService.NutritionProfile? nutritionProfile = null;
        IReadOnlyList<string> clinicalGuidance = Array.Empty<string>();
        JsonElement? sportsProfile = null;
        JsonElement? weightProfile = null;
        var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DietTherapyProfile? dietTherapyProfile = null;
        biometrics? latestBiometrics = null;

        var preferredFoods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dislikedFoods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        double? recentAdherence = null;
        var adherenceBoost = 0.0;

        if (request.ClientId.HasValue)
        {
            client = await _context.clients
                .Include(c => c.digestive_health)
                 .Include(c => c.food_preferences)
                .Include(c => c.medical_history)
                .Include(c => c.lifestyle_history)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.id == request.ClientId.Value && c.archived_at == null && (!tenantId.HasValue || c.tenant_id == tenantId.Value), cancellationToken);

            if (client != null)
            {
                latestBiometrics = await _context.biometrics
                    .AsNoTracking()
                    .Where(b => b.client_id == client.id)
                    .OrderByDescending(b => b.measurement_date)
                    .FirstOrDefaultAsync(cancellationToken);

                if (request.EnableDietTherapyRules)
                {
                    dietTherapyProfile = _dietTherapyRuleEngine.BuildProfile(
                        client,
                        client.medical_history,
                        client.lifestyle_history,
                        latestBiometrics,
                        request.DietType,
                        targetKcal,
                        hasExplicitKcal);

                    if (!hasExplicitKcal && request.EnableAutomaticEnergyEstimate)
                        targetKcal = dietTherapyProfile.TargetKcal;
                }
                // Las preferencias del paciente son criterios blandos: nunca pueden vencer
                // alergias, intolerancias, exclusiones clínicas o reglas de especialización.
                preferredFoods = ParseFoodTerms(client.food_preferences?.preferred_foods);
                dislikedFoods = ParseFoodTerms(client.food_preferences?.disliked_foods);

                var recentCheckins = await _context.patient_checkins
                    .AsNoTracking()
                    .Where(c => c.client_id == client.id &&
                        (!tenantId.HasValue || c.tenant_id == tenantId.Value))
                    .OrderByDescending(c => c.week_start)
                    .Take(6)
                    .Select(c => c.adherence)
                    .ToListAsync(cancellationToken);

                if (recentCheckins.Count > 0)
                {
                    recentAdherence = recentCheckins.Where(v => v.HasValue)
                        .Select(v => (double)v!.Value)
                        .DefaultIfEmpty()
                        .Average();

                    // Cuando la adherencia reciente es baja damos más peso a lo que el
                    // paciente ya ha declarado que le gusta. Es una señal de adherencia,
                    // no una regla clínica ni una sustitución del criterio profesional.
                    // Umbral (<= 5 sobre 10) y valor del boost: criterio de producto, origen no documentado.
                    // El valor solo se usa como indicador (>0); el peso real es el x1.5 de TherapyFactor más abajo.
                    if (recentAdherence.HasValue && recentAdherence.Value <= 5)
                        adherenceBoost = 20;
                }

                if (tenantId.HasValue)
                {
                    nutritionProfile = await _specializationRulesService.GetNutritionProfileAsync(
                        client.id, tenantId.Value, cancellationToken);
                    clinicalGuidance = await _specializationRulesService.GetClinicalGuidanceAsync(
                        client.id, tenantId.Value, cancellationToken);
                    sportsProfile = await _specializationRulesService.GetClientSpecializationProfileAsync(
                        client.id, tenantId.Value, "sports_nutrition", cancellationToken);
                    weightProfile = await _specializationRulesService.GetClientSpecializationProfileAsync(
                        client.id, tenantId.Value, "weight_management", cancellationToken);

                    // El objetivo automático incorpora el perfil de control de peso cuando existe.
                    // La energía de mantenimiento se estima sin aplicar un déficit genérico adicional.
                    if (!hasExplicitKcal && request.EnableAutomaticEnergyEstimate && weightProfile.HasValue)
                    {
                        var maintenanceKcal = DietTherapyRuleEngine.EstimateEnergy(
                            client, latestBiometrics, "Equilibrada", targetKcal);
                        targetKcal = DietTherapyRuleEngine.ApplyWeightManagementTarget(
                            maintenanceKcal,
                            GetProfileDouble(weightProfile, "deficitPercent"),
                            GetProfileDouble(weightProfile, "targetRateKgPerWeek"),
                            GetProfileDouble(weightProfile, "minimumKcal"));
                    }
                }

                if (client.digestive_health?.gluten_intolerance == true)
                    AddGlutenKeywords(exclusions);
                if (client.digestive_health?.lactose_intolerance == true)
                    AddLactoseKeywords(exclusions);
                if (client.digestive_health?.fodmaps_intolerance == true)
                    AddFodmapKeywords(exclusions);
                if (tenantId.HasValue)
                {
                    var specializationExclusions = await _specializationRulesService.GetFoodExclusionsAsync(
                        client.id, tenantId.Value, cancellationToken);
                    foreach (var keyword in specializationExclusions)
                        exclusions.Add(keyword);
                }

                foreach (var term in DietaryRestrictionRules.BuildForbiddenTerms(
                    client.food_preferences?.allergies,
                    client.digestive_health?.gluten_intolerance == true,
                    client.digestive_health?.lactose_intolerance == true,
                    client.digestive_health?.fodmaps_intolerance == true))
                    exclusions.Add(term);
            }
        }

        profileElapsedMs = profileTimer.ElapsedMilliseconds;
        _logger?.LogInformation("Diet generation phase ProfileResolution completed in {ElapsedMs} ms; HasClient={HasClient}; AutomaticEnergy={AutomaticEnergy}", profileElapsedMs, client != null, request.EnableAutomaticEnergyEstimate);

        // Incorporar exclusiones directas solicitadas por el profesional en la petición
        if (request.ExcludedFoodKeywords != null && request.ExcludedFoodKeywords.Count > 0)
        {
            foreach (var kw in request.ExcludedFoodKeywords)
            {
                if (!string.IsNullOrWhiteSpace(kw))
                    exclusions.Add(kw.Trim());
            }
        }

        // Contrato energético: un objetivo explícito del profesional prevalece sobre el cálculo automático, pero NO sobre
        // el mínimo energético que el propio profesional fijó en el perfil de control de peso del paciente (salvaguarda
        // clínica) ni sobre el rango operativo 1200–4500 kcal (la validación semántica rechaza lo que quede fuera).
        // El mínimo se aplica antes de calcular macros para mantener coherencia entre energía y distribución.
        var minimumKcal = GetProfileDouble(weightProfile, "minimumKcal");
        if (minimumKcal.HasValue && targetKcal < minimumKcal.Value)
        {
            _logger?.LogWarning("El objetivo energético {Requested} kcal queda por debajo del mínimo del perfil de control de peso; se aplica {Minimum} kcal.", targetKcal, minimumKcal.Value);
            targetKcal = minimumKcal.Value;
        }

        // 2. Macros objetivo. Prioridad de la proteína: valor explícito de la petición > perfil deportivo (g/kg)
        // > perfil de control de peso (g/kg) > perfil de nutrición deportiva > porcentaje por tipo de dieta.
        // Los hidratos son el residuo energético tras proteína y grasa, salvo objetivo explícito o perfil deportivo.
        double? sportsProtein = GetProfileDouble(sportsProfile, "proteinGPerKg");
        double? sportsCarbs = GetProfileDouble(sportsProfile, "carbsGPerKg");
        double? weightProtein = GetProfileDouble(weightProfile, "proteinGPerKg");

        double targetProtein = request.TargetProtein
            ?? (client != null && sportsProtein.HasValue
                ? await CalculateGramsFromKgAsync(client.id, sportsProtein.Value, CalculateDefaultProtein(targetKcal, request.DietType), cancellationToken)
                : client != null && weightProtein.HasValue
                    ? await CalculateGramsFromKgAsync(client.id, weightProtein.Value, CalculateDefaultProtein(targetKcal, request.DietType), cancellationToken)
                    : nutritionProfile != null && client != null
                        ? await CalculateSportsProteinAsync(client.id, nutritionProfile, targetKcal, cancellationToken)
                        : CalculateDefaultProtein(targetKcal, request.DietType));
        double targetFat = request.TargetFat ?? CalculateDefaultFat(targetKcal, request.DietType);
        double targetCarbs = request.TargetCarbs
            ?? (client != null && sportsCarbs.HasValue
                ? await CalculateGramsFromKgAsync(client.id, sportsCarbs.Value, CalculateDefaultCarbs(targetKcal, targetProtein, targetFat), cancellationToken)
                : CalculateDefaultCarbs(targetKcal, targetProtein, targetFat));

        // Una pauta cetogénica no puede superar su tope de hidratos ni siquiera con un objetivo explícito mayor:
        // se deja un 10 % de margen bajo el tope y la energía retirada se traslada a grasa.
        if (DietSemanticValidationService.IsKetoDietType(request.DietType))
        {
            var ketoCarbCeiling = DietSemanticValidationService.KetoDailyCarbLimitGrams * 0.9;
            if (targetCarbs > ketoCarbCeiling)
            {
                // 4 kcal/g de hidrato retirado se convierten en gramos de grasa a 9 kcal/g (conserva la energía total).
                targetFat += (targetCarbs - ketoCarbCeiling) * 4.0 / 9.0;
                targetCarbs = ketoCarbCeiling;
            }
        }

        // 3. Cargar un catálogo acotado de alimentos desde la BD.
        // No debemos traer toda la tabla a memoria: el catálogo puede crecer mucho
        // con las sincronizaciones de USDA/OpenFoodFacts y bloquear la generación.
        // Los alimentos con perfil culinario curado se cargan primero para que un catálogo grande no los desplace.
        // Tope de carga: criterio de producto (rendimiento), origen no documentado.
        const int maxFoodsToLoad = 5000;
        var foodsLoadTimer = Stopwatch.StartNew();
        // La expresión debe ser completamente traducible por EF Core. No podemos invocar
        // UserCanUseTenantLocalFood dentro del Where porque EF no puede traducir métodos CLR
        // arbitrarios a SQL. La regla de visibilidad se expresa aquí con propiedades y valores
        // simples; así mantenemos el filtro en BD y evitamos cargar alimentos no autorizados.
        var canUseAnyTenantLocalFood = tenantId.HasValue && canUseTenantLocalFoods;
        var allFoods = await _context.foods
            .AsNoTracking()
            .Where(f => f.kcal.HasValue && f.kcal > 0 &&
                f.name != null &&
                ((f.source == null || f.source.ToLower() != "local") ||
                 (tenantId.HasValue && f.tenant_id == tenantId.Value &&
                  (canUseAnyTenantLocalFood || f.created_by_user_id == userId))))
            .OrderBy(f => _context.food_culinary_profile.Any(p => p.food_id == f.id && p.excluded_reason == null) ? 0 : 1)
            .ThenBy(f => f.id)
            .Include(f => f.exchange_group)
            .Take(maxFoodsToLoad)
            .ToListAsync(cancellationToken);
        var storedProfiles = (await _context.food_culinary_profile.AsNoTracking().ToListAsync(cancellationToken))
            .ToDictionary(p => p.food_id, FoodProfileData.FromEntity);
        foodsLoadElapsedMs = foodsLoadTimer.ElapsedMilliseconds;
        _logger?.LogInformation("Diet generation phase FoodCatalogLoad completed in {ElapsedMs} ms; LoadedFoods={LoadedFoods}; Limit={Limit}; CuratedProfiles={Profiles}", foodsLoadElapsedMs, allFoods.Count, maxFoodsToLoad, storedProfiles.Count);

        var foodFilteringTimer = Stopwatch.StartNew();
        // Las reglas estructuradas por atributos tienen prioridad sobre las coincidencias de texto.
        // Esto evita depender de que el nombre comercial del alimento contenga una palabra concreta.
        var structuredSpecializationExclusions = tenantId.HasValue && request.ClientId.HasValue
            ? await _specializationRulesService.GetExcludedFoodIdsAsync(
                request.ClientId.Value,
                tenantId.Value,
                allFoods.Select(f => f.id).ToArray(),
                cancellationToken)
            : new HashSet<int>();

        // Filtro de seguridad único: los términos de exclusión se compilan una vez para todo el catálogo y solo
        // los alimentos que lo superan (y tienen perfil culinario) pueden llegar al generador.
        var exclusionMatcher = new DietaryRestrictionRules.ExclusionMatcher(exclusions);
        var engineFoods = new List<EngineFood>();
        // Elección explícita de esta generación; si no la hay, la preferencia guardada en la ficha del paciente.
        var excludeBread = request.ExcludeBread ?? client?.food_preferences?.exclude_bread ?? false;
        foreach (var f in allFoods)
        {
            if (structuredSpecializationExclusions.Contains(f.id)) continue;
            // Defensa en profundidad del filtro SQL: un alimento local solo lo usa quien tiene permiso sobre él.
            if (string.Equals(f.source, "local", StringComparison.OrdinalIgnoreCase) && !UserCanUseTenantLocalFood(f, tenantId, userId, canUseTenantLocalFoods)) continue;
            // Perfil culinario curado en BD si existe; si no, se infiere por heurística de nombre/categoría/macros.
            if (!storedProfiles.TryGetValue(f.id, out var profile))
                profile = FoodCulinaryClassifier.Classify(f.name, f.category, f.kcal, f.protein, f.carbs, f.fat);
            if (profile == null || !profile.IsUsable) continue;
            // Un perfil persistido (auto o manual) no exime de validar los macros actuales del alimento.
            if (FoodCulinaryClassifier.GetMacroIntegrityIssue(profile.Role, f.kcal, f.protein, f.carbs, f.fat) != null) continue;
            if (excludeBread && profile.Role == FoodProfileData.RoleBread) continue;
            // Se descartan productos de marca (importados de bases comerciales); solo se admiten genéricos y BEDCA. Criterio de producto.
            if (!string.IsNullOrWhiteSpace(f.brands) && !string.Equals(f.source, "bedca", StringComparison.OrdinalIgnoreCase)) continue;
            if (IsBlocked(f, exclusionMatcher)) continue;
            if (!HasKnownAllergenDataForClient(f, client)) continue;
            engineFoods.Add(EngineFood.From(f, profile));
        }

        foodFilteringElapsedMs = foodFilteringTimer.ElapsedMilliseconds;
        _logger?.LogInformation("Diet generation phase FoodFiltering completed in {ElapsedMs} ms; AllowedFoods={AllowedFoods}; ExclusionTerms={ExclusionTerms}", foodFilteringElapsedMs, engineFoods.Count, exclusionMatcher.TermCount);

        cancellationToken.ThrowIfCancellationRequested();

        if (engineFoods.Count == 0)
        {
            var allergenVerificationHint = !string.IsNullOrWhiteSpace(client?.food_preferences?.allergies)
                ? " Para proteger al paciente, se han descartado alimentos sin datos de alérgenos verificados; revisa el catálogo antes de volver a generar."
                : string.Empty;
            throw new InvalidOperationException(
                "No queda ningún alimento compatible con las restricciones clínicas, el tipo de dieta y las exclusiones seleccionadas. Reduce las exclusiones o revisa las intolerancias/alergias del paciente." +
                allergenVerificationHint);
        }

        // Compatibilidad con el tipo de dieta y comprobación de que existen alimentos para cada pool imprescindible
        // (proteína, hidratos, verdura, fruta, aceite); en cetogénica no se exigen hidratos ni fruta.
        var isKetoType = request.DietType.Equals("Cetogenica", StringComparison.OrdinalIgnoreCase);
        var compatible = engineFoods.Where(e =>
            DietSemanticValidationService.IsFoodCompatibleWithDietType(e.Food.name, request.DietType, e.Food.category, e.Food.carbs, e.Food.fat, foodId: e.Id) &&
            (!isKetoType || e.P.Role is not (FoodProfileData.RoleStarch or FoodProfileData.RoleLegume or FoodProfileData.RoleBread or FoodProfileData.RoleCereal))).ToList();
        var allowedFoods = compatible.Select(e => e.Food).ToList();
        var profilesById = compatible.ToDictionary(e => e.Id, e => e.P);
        var mealSplits = GetMealSplits(request.MealsPerDay);
        var missingPools = new List<string>();
        if (!compatible.Any(e => e.P.Role is FoodProfileData.RoleProtein or FoodProfileData.RoleLegume)) missingPools.Add("proteínas");
        if (!isKetoType && !compatible.Any(e => e.P.Role is FoodProfileData.RoleStarch or FoodProfileData.RoleLegume or FoodProfileData.RoleBread)) missingPools.Add("hidratos de carbono");
        if (!compatible.Any(e => e.P.Role == FoodProfileData.RoleVegetable)) missingPools.Add("verduras/hortalizas");
        if (!isKetoType && !compatible.Any(e => e.P.Role == FoodProfileData.RoleFruit)) missingPools.Add("frutas");
        if (!compatible.Any(e => e.P.Role == FoodProfileData.RoleOil)) missingPools.Add("aceite culinario");
        if (missingPools.Count > 0)
        {
            throw new InvalidOperationException(
                $"El catálogo compatible no contiene suficientes alimentos para construir el plan. Faltan: {string.Join(", ", missingPools)}. Revisa las restricciones del paciente, el tipo de dieta y las exclusiones.");
        }

        // Preferencias del paciente y señal terapéutica: solo ponderan la selección, nunca amplían el catálogo seguro.
        foreach (var e in compatible)
        {
            e.Preferred = MatchesFoodTerms(e.Food, preferredFoods);
            e.Disliked = MatchesFoodTerms(e.Food, dislikedFoods);
            e.TherapyFactor = CalculateTherapyFactor(e.Food, dietTherapyProfile);
            // x1.5 (criterio de producto) sobre alimentos preferidos cuando la adherencia reciente es baja.
            if (e.Preferred && adherenceBoost > 0) e.TherapyFactor *= 1.5;
        }

        // Las plantillas profesionales reutilizan la estructura de comidas, pero no fuerzan
        // alimentos concretos: cada componente vuelve a pasar por las restricciones clínicas
        // y por el motor de menús.
        var templateId = request.TemplateDietId ?? request.VariantOfDietId;
        if (templateId.HasValue && tenantId.HasValue)
        {
            var templateMeals = await _context.diets
                .AsNoTracking()
                .Where(d => d.id == templateId.Value &&
                            d.tenant_id == tenantId.Value &&
                            d.archived_at == null &&
                            (d.is_template || request.VariantOfDietId.HasValue))
                .SelectMany(d => d.diet_days.OrderBy(dd => dd.day_index).Take(1)
                    .SelectMany(dd => dd.meals.OrderBy(m => m.meal_index)
                        .Select(m => m.name)))
                .ToListAsync(cancellationToken);

            if (templateMeals.Count == mealSplits.Count)
            {
                for (var i = 0; i < mealSplits.Count; i++)
                    mealSplits[i].MealName = templateMeals[i];
            }
        }

        DietDetailDto? advancedDiet = null;

        // El motor es determinista dado un seed: la variedad procede de la rotación semanal y de la selección ponderada,
        // y las composiciones de cada día se reintentan localmente. Solo si la validación final falla se genera otro seed;
        // no se relajan restricciones ni tolerancias.
        const int maxGenerationAttempts = 2;
        var baseSeed = Random.Shared.Next();
        var engineMap = compatible.ToDictionary(e => e.Id);
        for (var attempt = 0; attempt < maxGenerationAttempts; attempt++)
        {
            generationAttemptCount = attempt + 1;
            var attemptBuildTimer = Stopwatch.StartNew();
            var engineOptions = new MenuEngineOptions
            {
                Days = request.NumberOfDays,
                DietType = request.DietType,
                Kcal = targetKcal,
                Protein = targetProtein,
                Carbs = targetCarbs,
                Fat = targetFat,
                MaxWeeklyRepetitions = request.MaxWeeklyFoodRepetitions,
                Refine = request.EnableGlobalOptimization,
                // 7919 es un primo arbitrario para separar los seeds de cada intento (sin significado nutricional).
                Seed = unchecked(baseSeed + attempt * 7919),
                Meals = mealSplits.Select(sp => (sp.MealName, sp.KcalPct)).ToList(),
                FrequencyTargets = dietTherapyProfile?.FrequencyTargets != null
                    ? new Dictionary<string, double>(dietTherapyProfile.FrequencyTargets, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            };
            var engineResult = DietMenuEngine.Build(compatible, engineOptions);

            var candidateDiet = new DietDetailDto
            {
                Name = $"Plan {request.DietType} ({Math.Round(targetKcal)} kcal)",
                TargetKcal = (decimal)Math.Round(targetKcal),
                TargetProtein = (decimal)Math.Round(targetProtein),
                TargetCarbs = (decimal)Math.Round(targetCarbs),
                TargetFat = (decimal)Math.Round(targetFat),
                Notes = $"Plan generado automáticamente por el motor experto el {DateTime.Now:dd/MM/yyyy}. {request.MealsPerDay} comidas al día." +
                    " Cantidades de cereales, legumbres, carnes y pescados expresadas en peso en crudo salvo que el alimento indique que está cocinado." +
                    (request.ExplainGeneration && dietTherapyProfile != null && dietTherapyProfile.Rules.Count > 0 ? " Reglas aplicadas: " + string.Join(" ", dietTherapyProfile.Rules) : string.Empty) +
                    (clinicalGuidance.Count > 0 ? " Especializaciones activas: " + string.Join(" ", clinicalGuidance) : string.Empty) +
                    (preferredFoods.Count > 0 ? " Preferencias alimentarias del paciente aplicadas como criterio de adherencia." : string.Empty) +
                    (recentAdherence.HasValue ? $" Adherencia reciente considerada: {Math.Round(recentAdherence.Value, 1)}/10." : string.Empty),
                Days = engineResult.Days
            };
            _logger?.LogInformation("Diet generation phase BasePlanBuild completed in {ElapsedMs} ms; Attempt={Attempt}; Days={Days}; MealsPerDay={MealsPerDay}; DayRetries={DayRetries}", attemptBuildTimer.ElapsedMilliseconds, attempt + 1, request.NumberOfDays, request.MealsPerDay, engineResult.DayRetries);

            // Las recetas profesionales solo entran si todos sus ingredientes están en el catálogo seguro; después se
            // reequilibra cada día con las mismas porciones y límites culinarios que usa el motor.
            if (request.UseRecipes && tenantId.HasValue)
            {
                var recipeTimer = Stopwatch.StartNew();
                await _advancedDietOptimizer.ApplyRecipesAsync(
                    candidateDiet, request, allowedFoods.ToDictionary(f => f.id), tenantId, targetKcal, cancellationToken);
                foreach (var day in candidateDiet.Days.Where(d => d.Meals.Any(m => m.Items.Any(i => i.RecipeId.HasValue))))
                    DietMenuEngine.Rebalance(day, engineMap, engineOptions);
                _logger?.LogInformation("Diet generation phase RecipeInjection completed in {ElapsedMs} ms; Attempt={Attempt}", recipeTimer.ElapsedMilliseconds, attempt + 1);
            }

            try
            {
                var validationTimer = Stopwatch.StartNew();
                await _semanticValidationService.ValidateOrThrowAsync(
                    candidateDiet,
                    request,
                    allowedFoods,
                    _context,
                    tenantId,
                    userId,
                    canUseTenantLocalFoods,
                    cancellationToken,
                    profilesById);

                _logger?.LogInformation("Diet generation phase SemanticValidation completed in {ElapsedMs} ms; Attempt={Attempt}; Result=Valid", validationTimer.ElapsedMilliseconds, attempt + 1);
                advancedDiet = candidateDiet;
                break;
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogWarning(ex, "Diet generation phase SemanticValidation failed; Attempt={Attempt}; ExceptionType={ExceptionType}; ValidationFailure={ValidationFailure}", attempt + 1, ex.GetType().Name, ex.Message);
                if (attempt == maxGenerationAttempts - 1)
                    throw new InvalidOperationException(
                        $"No se ha podido generar una dieta válida tras {maxGenerationAttempts} intentos. Último motivo: {ex.Message}",
                        ex);
            }
        }

        var validatedDiet = advancedDiet
            ?? throw new InvalidOperationException("No se ha podido generar una dieta válida.");
        // Persistir las exclusiones explícitas en el resultado para que sobrevivan al
        // guardado de la dieta y se apliquen también en futuras regeneraciones.
        validatedDiet.DietType = request.DietType;
        validatedDiet.ExcludedFoodKeywords = (request.ExcludedFoodKeywords ?? new List<string>())
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Select(keyword => keyword.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Calcular resumen diario de micronutrientes y % CDR para cada día generado
        var micronutrientTimer = Stopwatch.StartNew();
        PopulateDailyMicronutrients(validatedDiet, allowedFoods);
        _logger?.LogInformation("Diet generation phase MicronutrientSummary completed in {ElapsedMs} ms; Days={Days}", micronutrientTimer.ElapsedMilliseconds, validatedDiet.Days.Count);

        generationTimer.Stop();
        _logger?.LogInformation("Diet generation completed in {ElapsedMs} ms; DietType={DietType}; Days={Days}; MealsPerDay={MealsPerDay}; LoadedFoods={LoadedFoods}; AllowedFoods={AllowedFoods}; GlobalOptimization={GlobalOptimization}; Attempts={Attempts}; ProfileMs={ProfileMs}; FoodLoadMs={FoodLoadMs}; FoodFilteringMs={FoodFilteringMs}", generationTimer.ElapsedMilliseconds, request.DietType, request.NumberOfDays, request.MealsPerDay, allFoods.Count, allowedFoods.Count, request.EnableGlobalOptimization, generationAttemptCount, profileElapsedMs, foodsLoadElapsedMs, foodFilteringElapsedMs);

        return validatedDiet;
    }

    /// <summary>
    /// Calcula por día el aporte de micronutrientes y su % respecto a la ingesta de referencia. Los azúcares y las grasas
    /// saturada/mono/poliinsaturada solo suman de alimentos con dato; los indicadores *DataComplete avisan de si el total
    /// es parcial. Los alimentos sin dato de un micronutriente cuentan como 0 (infraestima, no se imputa).
    /// </summary>
    public static void PopulateDailyMicronutrients(DietDetailDto diet, IReadOnlyCollection<foods> allowedFoods)
    {
        if (diet.Days == null || diet.Days.Count == 0) return;
        var foodsById = allowedFoods.GroupBy(f => f.id).ToDictionary(g => g.Key, g => g.First());

        foreach (var day in diet.Days)
        {
            var summary = new MicronutrientDailySummaryDto();
            double ca = 0, fe = 0, mg = 0, k = 0, zn = 0;
            double vitA = 0, vitC = 0, vitD = 0, vitE = 0, vitB12 = 0, folate = 0;
            double fiber = 0, sugar = 0, salt = 0;
            double saturatedFat = 0, monounsaturatedFat = 0, polyunsaturatedFat = 0;
            bool sugarDataComplete = true, saturatedFatDataComplete = true;
            bool monounsaturatedFatDataComplete = true, polyunsaturatedFatDataComplete = true;
            int totalFoodCount = 0, sugarFoodCount = 0, saturatedFatFoodCount = 0;
            int monounsaturatedFatFoodCount = 0, polyunsaturatedFatFoodCount = 0;

            foreach (var meal in day.Meals)
            {
                foreach (var item in meal.Items)
                {
                    if (!item.FoodId.HasValue || (item.Grams ?? 0) <= 0 || !foodsById.TryGetValue(item.FoodId.Value, out var food))
                        continue;

                    var ratio = (double)item.Grams!.Value / 100.0;
                    totalFoodCount++;
                    ca += (food.calcium_mg ?? 0) * ratio;
                    fe += (food.iron_mg ?? 0) * ratio;
                    mg += (food.magnesium_mg ?? 0) * ratio;
                    k += (food.potassium_mg ?? 0) * ratio;
                    zn += (food.zinc_mg ?? 0) * ratio;

                    vitA += (food.vitamin_a_ug ?? 0) * ratio;
                    vitC += (food.vitamin_c_mg ?? 0) * ratio;
                    vitD += (food.vitamin_d_ug ?? 0) * ratio;
                    vitE += (food.vitamin_e_mg ?? 0) * ratio;
                    vitB12 += (food.vitamin_b12_ug ?? 0) * ratio;
                    folate += (food.folate_ug ?? 0) * ratio;

                    fiber += (food.fiber ?? 0) * ratio;
                    if (food.sugar.HasValue) sugar += food.sugar.Value * ratio;
                    salt += (food.salt ?? 0) * ratio;
                    if (food.sugar.HasValue) sugarFoodCount++; else sugarDataComplete = false;
                    if (food.saturated_fat.HasValue) { saturatedFat += food.saturated_fat.Value * ratio; saturatedFatFoodCount++; } else saturatedFatDataComplete = false;
                    if (food.monounsaturated_fat.HasValue) { monounsaturatedFat += food.monounsaturated_fat.Value * ratio; monounsaturatedFatFoodCount++; } else monounsaturatedFatDataComplete = false;
                    if (food.polyunsaturated_fat.HasValue) { polyunsaturatedFat += food.polyunsaturated_fat.Value * ratio; polyunsaturatedFatFoodCount++; } else polyunsaturatedFatDataComplete = false;
                }
            }

            // Ingestas Diarias Recomendadas (CDR / RDA adultos según EFSA/AESAN). Valores únicos para todos los pacientes:
            // no se ajustan por sexo, edad ni embarazo (limitación conocida; regla nutricional simplificada).
            const double rdaCa = 950.0;     // mg
            const double rdaFe = 14.0;      // mg (media adulta ponderada)
            const double rdaMg = 350.0;     // mg
            const double rdaK = 3500.0;     // mg
            const double rdaZn = 10.0;      // mg
            const double rdaVitA = 800.0;   // ug
            const double rdaVitC = 80.0;    // mg
            const double rdaVitD = 15.0;    // ug (600 UI)
            const double rdaVitE = 12.0;    // mg
            const double rdaVitB12 = 2.5;   // ug
            const double rdaFolate = 330.0; // ug
            const double rdaFiber = 30.0;   // g

            summary.CalciumMg = (decimal)Math.Round(ca, 1);
            summary.CalciumPctRda = (decimal)Math.Round((ca / rdaCa) * 100.0, 0);
            summary.IronMg = (decimal)Math.Round(fe, 1);
            summary.IronPctRda = (decimal)Math.Round((fe / rdaFe) * 100.0, 0);
            summary.MagnesiumMg = (decimal)Math.Round(mg, 1);
            summary.MagnesiumPctRda = (decimal)Math.Round((mg / rdaMg) * 100.0, 0);
            summary.PotassiumMg = (decimal)Math.Round(k, 1);
            summary.PotassiumPctRda = (decimal)Math.Round((k / rdaK) * 100.0, 0);
            summary.ZincMg = (decimal)Math.Round(zn, 1);
            summary.ZincPctRda = (decimal)Math.Round((zn / rdaZn) * 100.0, 0);

            summary.VitaminAUg = (decimal)Math.Round(vitA, 1);
            summary.VitaminAPctRda = (decimal)Math.Round((vitA / rdaVitA) * 100.0, 0);
            summary.VitaminCMg = (decimal)Math.Round(vitC, 1);
            summary.VitaminCPctRda = (decimal)Math.Round((vitC / rdaVitC) * 100.0, 0);
            summary.VitaminDUg = (decimal)Math.Round(vitD, 1);
            summary.VitaminDPctRda = (decimal)Math.Round((vitD / rdaVitD) * 100.0, 0);
            summary.VitaminEMg = (decimal)Math.Round(vitE, 1);
            summary.VitaminEPctRda = (decimal)Math.Round((vitE / rdaVitE) * 100.0, 0);
            summary.VitaminB12Ug = (decimal)Math.Round(vitB12, 1);
            summary.VitaminB12PctRda = (decimal)Math.Round((vitB12 / rdaVitB12) * 100.0, 0);
            summary.FolateUg = (decimal)Math.Round(folate, 1);
            summary.FolatePctRda = (decimal)Math.Round((folate / rdaFolate) * 100.0, 0);

            summary.FiberG = (decimal)Math.Round(fiber, 1);
            summary.FiberPctRda = (decimal)Math.Round((fiber / rdaFiber) * 100.0, 0);
            summary.SugarG = (decimal)Math.Round(sugar, 1);
            summary.SugarDataComplete = sugarDataComplete;
            summary.TotalFoodCount = totalFoodCount;
            summary.SugarFoodCount = sugarFoodCount;
            summary.SaturatedFatG = (decimal)Math.Round(saturatedFat, 1);
            summary.SaturatedFatDataComplete = saturatedFatDataComplete;
            summary.SaturatedFatFoodCount = saturatedFatFoodCount;
            summary.MonounsaturatedFatG = (decimal)Math.Round(monounsaturatedFat, 1);
            summary.MonounsaturatedFatDataComplete = monounsaturatedFatDataComplete;
            summary.MonounsaturatedFatFoodCount = monounsaturatedFatFoodCount;
            summary.PolyunsaturatedFatG = (decimal)Math.Round(polyunsaturatedFat, 1);
            summary.PolyunsaturatedFatDataComplete = polyunsaturatedFatDataComplete;
            summary.PolyunsaturatedFatFoodCount = polyunsaturatedFatFoodCount;
            summary.SaltG = (decimal)Math.Round(salt, 2);

            day.Micronutrients = summary;
        }
    }

    #region Plantillas de Comidas y Distribución

    private class MealSplit
    {
        public string MealName { get; set; } = string.Empty;
        public double KcalPct { get; set; }
    }

    // Reparto calórico por comida (suma 1.0 en cada caso). Criterio de producto, origen no documentado;
    // cualquier valor distinto de 3 o 4 se trata como 5 comidas.
    private List<MealSplit> GetMealSplits(int mealsCount)
    {
        return mealsCount switch
        {
            3 => new List<MealSplit>
            {
                new() { MealName = "Desayuno", KcalPct = 0.28 },
                new() { MealName = "Comida", KcalPct = 0.42 },
                new() { MealName = "Cena", KcalPct = 0.30 }
            },
            4 => new List<MealSplit>
            {
                new() { MealName = "Desayuno", KcalPct = 0.25 },
                new() { MealName = "Comida", KcalPct = 0.38 },
                new() { MealName = "Merienda", KcalPct = 0.12 },
                new() { MealName = "Cena", KcalPct = 0.25 }
            },
            _ => new List<MealSplit> // 5 comidas
            {
                new() { MealName = "Desayuno", KcalPct = 0.22 },
                new() { MealName = "Media Mañana", KcalPct = 0.10 },
                new() { MealName = "Comida", KcalPct = 0.36 },
                new() { MealName = "Merienda", KcalPct = 0.10 },
                new() { MealName = "Cena", KcalPct = 0.22 }
            }
        };
    }

    #endregion



    #region Helpers de Cálculo y Restricciones

    private static double? GetProfileDouble(JsonElement? profile, string property)
    {
        if (!profile.HasValue || profile.Value.ValueKind != JsonValueKind.Object)
            return null;

        if (!profile.Value.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number)
            return null;

        return value.TryGetDouble(out var number) && !double.IsNaN(number) && !double.IsInfinity(number)
            ? number
            : null;
    }

    private async Task<double> CalculateGramsFromKgAsync(
        int clientId,
        double gramsPerKg,
        double fallbackValue,
        CancellationToken cancellationToken)
    {
        var latest = await _context.biometrics
            .AsNoTracking()
            .Where(b => b.client_id == clientId && b.weight.HasValue && b.weight > 0)
            .OrderByDescending(b => b.measurement_date)
            .ThenByDescending(b => b.id)
            .FirstOrDefaultAsync(cancellationToken);

        return latest?.weight is double weight && weight > 0
            ? Math.Round(weight * gramsPerKg, 1)
            : fallbackValue;
    }

    private async Task<double> CalculateSportsProteinAsync(
        int clientId,
        SpecializationRulesService.NutritionProfile profile,
        double targetKcal,
        CancellationToken cancellationToken)
    {
        var latestBiometric = await _context.biometrics
            .AsNoTracking()
            .Where(b => b.client_id == clientId && b.weight.HasValue && b.weight > 0)
            .OrderByDescending(b => b.measurement_date)
            .ThenByDescending(b => b.id)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestBiometric?.weight is not double weight || weight <= 0)
            return CalculateDefaultProtein(targetKcal, "Equilibrada");

        var gramsPerKg = profile.ProteinGramsPerKg;
        if (profile.ProteinMinGramsPerKg.HasValue)
            gramsPerKg = Math.Max(gramsPerKg, profile.ProteinMinGramsPerKg.Value);
        if (profile.ProteinMaxGramsPerKg.HasValue)
            gramsPerKg = Math.Min(gramsPerKg, profile.ProteinMaxGramsPerKg.Value);

        return Math.Round(weight * gramsPerKg, 1);
    }

    // Macros por defecto cuando no hay objetivo explícito ni perfil: porcentaje de la energía por tipo de dieta
    // (criterio de producto, origen no documentado). Proteína e hidratos a 4 kcal/g, grasa a 9 kcal/g.
    private double CalculateDefaultProtein(double kcal, string dietType)
    {
        double pct = dietType switch
        {
            "AltaProteina" => 0.30,
            "BajaCarbos"   => 0.28,
            "Cetogenica"   => 0.25, // moderada para evitar gluconeogénesis
            _              => 0.24  // Equilibrada / Vegetariana / Vegana (~120g para 2000 kcal)
        };
        return (kcal * pct) / 4.0;
    }

    private double CalculateDefaultFat(double kcal, string dietType)
    {
        double pct = dietType switch
        {
            "Cetogenica"   => 0.70, // >70% grasa para mantener cetosis
            "BajaCarbos"   => 0.40,
            "AltaProteina" => 0.25,
            _              => 0.26  // Equilibrada / Vegetariana / Vegana (~58g para 2000 kcal)
        };
        return (kcal * pct) / 9.0;
    }

    private double CalculateDefaultCarbs(double kcal, double proteinGrams, double fatGrams)
    {
        double usedKcal = (proteinGrams * 4.0) + (fatGrams * 9.0);
        return Math.Max(0, (kcal - usedKcal) / 4.0);
    }

    private static HashSet<string> ParseFoodTerms(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return raw
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeFoodTerm)
            .Where(term => term.Length >= 3)
            .Take(80) // tope defensivo ante textos libres enormes; criterio de producto
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeFoodTerm(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray();

        return new string(chars).Normalize(System.Text.NormalizationForm.FormC);
    }

    private static bool MatchesFoodTerms(foods food, HashSet<string> terms)
    {
        if (terms.Count == 0) return false;

        var name = NormalizeFoodTerm(food.name ?? string.Empty);
        var category = NormalizeFoodTerm(food.category ?? string.Empty);

        return terms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                 category.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool UserCanUseTenantLocalFood(foods food, int? tenantId, int userId, bool canUseTenantLocalFoods)
    {
        if (!tenantId.HasValue || food.tenant_id != tenantId.Value) return false;
        return canUseTenantLocalFoods || food.created_by_user_id == userId;
    }

    // Con alergias declaradas solo se aceptan alimentos con datos de alérgenos verificados (fuente "reported"). Los alérgenos
    // de food_dietary_profile son una curación propuesta: se suman al filtro de exclusiones pero no cuentan como verificación.
    // Con intolerancias sin alergia declarada basta con que el alimento tenga perfil dietético curado.
    private static bool HasKnownAllergenDataForClient(foods food, clients? client)
    {
        var declaresAllergies = !string.IsNullOrWhiteSpace(client?.food_preferences?.allergies);
        var hasIntolerance = client?.digestive_health?.gluten_intolerance == true ||
                             client?.digestive_health?.lactose_intolerance == true ||
                             client?.digestive_health?.fodmaps_intolerance == true;
        if (!declaresAllergies && !hasIntolerance) return true;

        var verified = !DietaryRestrictionRules.HasUnknownAllergenData(
            declaresAllergies ? client!.food_preferences!.allergies : "intolerancia",
            (food.allergen_tags ?? Array.Empty<string>()).Concat(food.traces_tags ?? Array.Empty<string>()),
            food.allergen_data_status);
        if (verified) return true;
        // Intolerancia sin alergia declarada: basta con que el alimento tenga perfil dietético curado, cuyos alérgenos se suman al filtro.
        return !declaresAllergies && FoodDietaryProfileCatalog.TryGet(food.id, out _);
    }

    // Misma comprobación que IsExcluded pero con los términos precompilados y las etiquetas curadas del perfil añadidas
    // a las banderas estructuradas del alimento (nunca sustituyen a las etiquetas importadas: se suman).
    private static bool IsBlocked(foods food, DietaryRestrictionRules.ExclusionMatcher matcher)
    {
        if (matcher.TermCount == 0) return false;
        return !matcher.IsAllowed(
            food.name,
            food.category,
            (food.dietary_flags ?? Array.Empty<string>())
                .Concat(food.allergen_tags ?? Array.Empty<string>())
                .Concat(food.traces_tags ?? Array.Empty<string>())
                .Concat(FoodDietaryProfileCatalog.TryGet(food.id, out var dietary) ? dietary.AllergenTerms : Array.Empty<string>()));
    }

    private bool IsExcluded(foods food, HashSet<string> exclusions)
    {
        if (exclusions == null || exclusions.Count == 0) return false;

        return !DietaryRestrictionRules.IsFoodAllowed(
            food.name,
            food.category,
            (food.dietary_flags ?? Array.Empty<string>())
                .Concat(food.allergen_tags ?? Array.Empty<string>())
                .Concat(food.traces_tags ?? Array.Empty<string>()),
            exclusions);
    }

    // Señal terapéutica blanda: penaliza o favorece alimentos según los pesos de nutrientes del perfil terapéutico.
    private static double CalculateTherapyFactor(foods food, DietTherapyProfile? profile)
    {
        if (profile == null || profile.NutrientWeights.Count == 0) return 1.0;
        double penalty = 0;
        foreach (var rule in profile.NutrientWeights)
        {
            var key = rule.Key.ToLowerInvariant();
            var value = key switch { "sugar" => (food.sugar ?? 0) / 10.0, "salt" => food.salt ?? 0, "fiber" => (food.fiber ?? 0) / 3.0, "potassium" => (food.potassium_mg ?? 0) / 100.0, _ => 0 };
            penalty += rule.Value * Math.Min(value, 3.0);
        }
        // Los divisores (10, 3, 100), el tope 3.0, la pendiente 0.25 y los límites 0.2-2.5 son criterio de producto
        // (normalizan cada nutriente a una escala comparable); origen no documentado. Pesos negativos favorecen el alimento.
        return Math.Clamp(Math.Exp(-0.25 * penalty), 0.2, 2.5);
    }

    private void AddGlutenKeywords(HashSet<string> ex)
    {
        foreach (var k in new[] { "trigo", "centeno", "cebada", "avena", "gluten", "pan", "pasta", "harina", "espelta", "sémola" })
            ex.Add(k);
    }

    private void AddLactoseKeywords(HashSet<string> ex)
    {
        foreach (var k in new[] { "leche", "queso", "yogur", "lact", "nata", "mantequilla", "cuajada", "suero", "kefir" })
            ex.Add(k);
    }

    private void AddFodmapKeywords(HashSet<string> ex)
    {
        foreach (var k in new[] { "ajo", "cebolla", "manzana", "pera", "alcachofa", "legumbre", "garbanzo", "lenteja", "alubia", "miel" })
            ex.Add(k);
    }

    #endregion
}