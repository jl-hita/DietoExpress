using Anguloso.Server.Model;
using Anguloso.Server.Models;

namespace Anguloso.Server.Logica;

/// <summary>
/// Política de porciones compartida por el motor y el validador, escalada por energía objetivo. Los límites mín/máx/por
/// defecto de cada alimento vienen de su perfil culinario (<see cref="FoodProfileData"/>) y aquí solo se escalan.
/// </summary>
public static class PortionPolicy
{
    // 2000 kcal es la referencia (porción "normal"); el rango 0.6-2.2 evita porciones absurdas en dietas extremas. Criterio de producto.
    public static double EnergyScale(double targetKcal) => targetKcal <= 0 ? 1.0 : Math.Clamp(targetKcal / 2000.0, 0.6, 2.2);

    public static (double Min, double Max, double Default) Bounds(FoodProfileData p, double targetKcal, bool highFat = false)
    {
        var s = EnergyScale(targetKcal);
        // Cuánto crece la porción con la energía según el rol: féculas/frutos secos proporcional; proteína, aceite y
        // verdura crecen menos que la energía (exponentes 0.85/0.7/0.5, criterio de producto). El mínimo nunca baja del 80 %.
        var factor = p.Role switch
        {
            FoodProfileData.RoleStarch or FoodProfileData.RoleLegume or FoodProfileData.RoleBread or FoodProfileData.RoleCereal or FoodProfileData.RoleNut => s,
            FoodProfileData.RoleProtein => Math.Pow(s, 0.85),
            FoodProfileData.RoleVegetable => Math.Pow(s, 0.5),
            FoodProfileData.RoleOil => Math.Pow(s, 0.7),
            _ => Math.Pow(s, 0.6)
        };
        var step = p.Step > 0 ? p.Step : 10;
        var min = Math.Max(step, Math.Round(p.Min * Math.Min(1.0, Math.Max(factor, 0.8)) / step) * step);
        // Las pautas cetogénica y baja en hidratos cubren la energía con grasa culinaria y frutos secos.
        var fatBoost = highFat && p.Role is FoodProfileData.RoleOil or FoodProfileData.RoleNut ? 1.75 : 1.0;
        var max = Math.Max(min, Math.Round(p.Max * Math.Max(1.0, factor) * fatBoost / step) * step);
        var def = Math.Clamp(Math.Round(p.Default * factor / step) * step, min, max);
        return (min, max, def);
    }
}

/// <summary>Alimento ya filtrado (seguro) y enriquecido con su perfil culinario y valores por 100 g.</summary>
public sealed class EngineFood
{
    // Abreviaturas: K = kcal, Pr = proteína, C = hidratos, F = grasa (todo por 100 g); P = perfil culinario.
    // Preferred/Disliked/TherapyFactor son pesos blandos para la selección; nunca amplían el catálogo seguro.
    public foods Food { get; init; } = null!;
    public FoodProfileData P { get; init; } = null!;
    public double K { get; init; }
    public double Pr { get; init; }
    public double C { get; init; }
    public double F { get; init; }
    public bool Preferred { get; set; }
    public bool Disliked { get; set; }
    public double TherapyFactor { get; set; } = 1.0;
    public int Id => Food.id;

    public static EngineFood From(foods food, FoodProfileData profile) => new()
    {
        Food = food, P = profile, K = food.kcal ?? 0, Pr = Math.Max(food.protein ?? 0, 0), C = Math.Max(food.carbs ?? 0, 0), F = Math.Max(food.fat ?? 0, 0)
    };
}

/// <summary>Entrada del motor: objetivos diarios, reparto por comidas (nombre y fracción de kcal), semilla y límites de variedad.</summary>
public sealed class MenuEngineOptions
{
    public int Days { get; set; } = 7;
    public string DietType { get; set; } = "Equilibrada";
    public double Kcal { get; set; }
    public double Protein { get; set; }
    public double Carbs { get; set; }
    public double Fat { get; set; }
    public int MaxWeeklyRepetitions { get; set; } = 4;
    /// <summary>Si es true se evalúan varias composiciones por día y se elige la mejor (EnableGlobalOptimization).</summary>
    public bool Refine { get; set; } = true;
    public int Seed { get; set; } = 12345;
    public List<(string Name, double KcalPct)> Meals { get; set; } = new();
    /// <summary>Frecuencias semanales objetivo por grupo (p. ej. "legumbre", "pescado_marisco") procedentes del perfil terapéutico; solo sesgan el esqueleto semanal.</summary>
    public Dictionary<string, double> FrequencyTargets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Salida del motor: días construidos, nº de reintentos locales de composición y avisos no bloqueantes.</summary>
public sealed class MenuEngineResult
{
    public List<DietDayDto> Days { get; set; } = new();
    public int DayRetries { get; set; }
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Constructor determinista de menús: esqueleto semanal de rotación (primeros platos, proteínas), plantillas de comidas
/// españolas (desayuno, media mañana, comida de 1º/2º/guarnición/pan/postre, merienda, cena ligera) y resolución de
/// porciones por descenso de coordenadas sobre una rejilla de raciones. Solo usa alimentos ya filtrados por seguridad.
/// Lugar en el flujo: recibe de <see cref="DietGeneratorService"/> los alimentos seguros y compatibles con la dieta, y
/// devuelve los días ya con gramos y macros; la validación posterior no es responsabilidad de esta clase.
/// Por cada día: (1) esqueleto semanal (BuildSkeleton) fija qué grupo de primer/segundo plato toca; (2) ComposeDay elige
/// alimentos por comida y rol culinario (Pick, ponderado y con semilla); (3) SolveDay ajusta gramos a kcal/macros;
/// (4) se repite hasta obtener un día aceptable, penalizando comidas repetidas.
/// </summary>
public sealed class DietMenuEngine
{
    private readonly MenuEngineOptions _o;
    private readonly bool _vegan, _vegetarian, _keto, _lowCarb;
    private readonly Dictionary<string, List<EngineFood>> _byRole = new(StringComparer.Ordinal);
    // Estado de variedad semanal (se actualiza en Commit): usos por alimento y por "kind" (variedad del mismo tipo de alimento).
    private readonly Dictionary<int, int> _weeklyUse = new();
    private readonly Dictionary<string, int> _weeklyKind = new(StringComparer.Ordinal);
    private HashSet<int> _prevDay = new();
    private string[] _lunchStarch = Array.Empty<string>();
    private string[] _lunchProtein = Array.Empty<string>();
    private string[] _dinnerProtein = Array.Empty<string>();
    private readonly List<string> _warnings = new();

    // Estado provisional de un intento de composición de un día; solo se vuelca al estado semanal si gana (Commit).
    private sealed class Attempt
    {
        public readonly HashSet<int> DayUsed = new();
        public readonly Dictionary<int, int> Added = new();
        public readonly Dictionary<string, int> AddedKind = new(StringComparer.Ordinal);
        public readonly Random Rnd;
        public Attempt(int seed) { Rnd = new Random(seed); }
    }

    private sealed class MealCtx
    {
        public readonly HashSet<int> Ids = new();
        public readonly HashSet<string> Kinds = new(StringComparer.Ordinal);
    }

    // Hueco de la plantilla de comida: un alimento con su rol dentro del plato (Course) y sus gramos G acotados por [Min, Max] en rejilla Step.
    private sealed class Slot
    {
        public EngineFood Food = null!;
        public string Meal = "";
        public int MealIndex;
        public string Course = "";
        public double Min, Max, Step, G;
    }

    private DietMenuEngine(MenuEngineOptions o)
    {
        _o = o;
        var t = DietaryRestrictionRules.Normalize(o.DietType);
        _vegan = t.Contains("vegana"); _vegetarian = t.Contains("vegetariana") || _vegan;
        _keto = t.Contains("cetogen"); _lowCarb = t.Contains("baja") && t.Contains("carb");
    }

    private DietMenuEngine(IReadOnlyList<EngineFood> foods, MenuEngineOptions o)
    {
        _o = o;
        var type = DietaryRestrictionRules.Normalize(o.DietType);
        _vegan = type.Contains("vegana"); _vegetarian = type.Contains("vegetariana") || _vegan;
        _keto = type.Contains("cetogen"); _lowCarb = type.Contains("baja") && type.Contains("carb");

        foreach (var f in foods)
        {
            if (!f.P.IsUsable) continue;
            if (!DietSemanticValidationService.IsFoodCompatibleWithDietType(f.Food.name, o.DietType, f.Food.category, f.Food.carbs, f.Food.fat, foodId: f.Food.id)) continue;
            if (_keto && (f.P.Role is FoodProfileData.RoleStarch or FoodProfileData.RoleLegume or FoodProfileData.RoleBread or FoodProfileData.RoleCereal)) continue;
            // Filtros adicionales por patrón de dieta (criterio de producto; el tope clínico de hidratos cetogénico lo valida DietSemanticValidationService):
            // keto solo admite frutos rojos, verduras de bajo contenido en hidratos (<= 4.5 g/100 g) y lácteos sin leche (<= 6 g); bajo en hidratos excluye féculas densas y fruta muy azucarada.
            if (_keto && f.P.Role == FoodProfileData.RoleFruit && !f.P.HasTag("berry")) continue;
            if (_lowCarb && f.P.Role == FoodProfileData.RoleStarch && f.P.Family is "pasta" or "patata" or "cereal") continue;
            if (_lowCarb && f.P.Role == FoodProfileData.RoleFruit && f.P.HasTag("highsugar")) continue;
            if (_keto && f.P.Role == FoodProfileData.RoleVegetable && f.C > 4.5) continue;
            if (_keto && f.P.Role == FoodProfileData.RoleDairy && (f.P.HasTag("milk") || f.C > 6)) continue;
            if (!_byRole.TryGetValue(f.P.Role, out var list)) _byRole[f.P.Role] = list = new List<EngineFood>();
            list.Add(f);
        }
    }

    /// <summary>Punto de entrada: construye los días del menú. Determinista para un mismo catálogo, opciones y <c>Seed</c>.</summary>
    public static MenuEngineResult Build(IReadOnlyList<EngineFood> foods, MenuEngineOptions options)
    {
        var engine = new DietMenuEngine(foods, options);
        return engine.Run();
    }

    private List<EngineFood> Pool(string role) => _byRole.TryGetValue(role, out var l) ? l : new List<EngineFood>();

    // ───────────────────────── Esqueleto semanal ─────────────────────────

    private MenuEngineResult Run()
    {
        var result = new MenuEngineResult();
        BuildSkeleton();
        for (var day = 0; day < _o.Days; day++)
        {
            var best = (Slots: (List<Slot>?)null, Score: double.MaxValue, Attempt: (Attempt?)null);
            // Con Refine se comparan al menos 4 composiciones por día y se conserva la de menor puntuación; sin Refine basta
            // la primera aceptable. 40 es el tope de reintentos (criterio de producto). Los multiplicadores del seed son primos arbitrarios.
            var minTries = _o.Refine ? 4 : 1;
            const int maxTries = 40;
            var bestAcceptable = false;
            for (var t = 0; t < maxTries; t++)
            {
                var attempt = new Attempt(unchecked(_o.Seed * 31 + day * 7919 + t * 104729));
                var slots = ComposeDay(day, attempt);
                var solveScore = SolveDay(slots, out var acceptable);
                var variety = VarietyPenalty(slots);
                // Un día que repite una comida completa ya usada dos veces solo vale si no hay alternativa.
                // Pesos de la puntuación (0.05 variedad, +1 si no cumple tolerancias): criterio de producto. Menor es mejor.
                var score = solveScore + variety * 0.05 + (acceptable ? 0 : 1);
                if (score < best.Score) { best = (slots, score, attempt); bestAcceptable = acceptable && variety < 1; }
                if (t + 1 >= minTries && bestAcceptable) break;
                if (t > 0) result.DayRetries++;
            }
            Commit(best.Slots!, best.Attempt!);
            result.Days.Add(ToDto(day, best.Slots!));
        }
        result.Warnings.AddRange(_warnings);
        return result;
    }

    private readonly Dictionary<string, int> _mealSignatures = new(StringComparer.Ordinal);

    private static string SignatureOf(IEnumerable<Slot> meal) => string.Join("|", meal.Select(x => x.Food.Id).OrderBy(id => id));

    // Penaliza repetir una comida completa (mismo conjunto de alimentos): ≥1 si ya apareció dos veces en la semana.
    private double VarietyPenalty(List<Slot> slots)
    {
        var penalty = 0.0;
        foreach (var meal in slots.GroupBy(x => x.MealIndex))
        {
            var uses = _mealSignatures.GetValueOrDefault(SignatureOf(meal));
            penalty += uses >= 2 ? 1.0 : uses == 1 ? 0.2 : 0;
        }
        return penalty;
    }

    private void Commit(List<Slot> slots, Attempt a)
    {
        foreach (var s in slots)
        {
            if (s.Food.P.IsStaple) continue;
            _weeklyUse[s.Food.Id] = _weeklyUse.GetValueOrDefault(s.Food.Id) + 1;
            _weeklyKind[s.Food.P.Kind] = _weeklyKind.GetValueOrDefault(s.Food.P.Kind) + 1;
        }
        _prevDay = slots.Select(s => s.Food.Id).ToHashSet();
        foreach (var meal in slots.GroupBy(x => x.MealIndex))
        {
            var key = SignatureOf(meal);
            _mealSignatures[key] = _mealSignatures.GetValueOrDefault(key) + 1;
        }
    }

    // Fija para toda la semana qué grupo toca en cada primer plato (_lunchStarch) y en cada segundo plato de comida y cena
    // (_lunchProtein/_dinnerProtein). Los pesos de reparto entre grupos (arroz, pasta, ave, pescado...) y las preferencias
    // de comida/cena son criterio de producto (gastronomía española); origen no documentado. Los FrequencyTargets del
    // perfil terapéutico solo elevan el peso de legumbre y pescado (con suelos 0.30 y 0.29).
    private void BuildSkeleton()
    {
        var rnd = new Random(_o.Seed);
        var n = Math.Max(1, _o.Days);

        // Primeros platos de la comida: legumbre, arroz, pasta, patata, cereal (según disponibilidad y tipo de dieta).
        var starchAvail = new List<(string Group, double W)>();
        var legumes = Pool(FoodProfileData.RoleLegume);
        var starch = Pool(FoodProfileData.RoleStarch);
        if (legumes.Count > 0) starchAvail.Add(("legumbre", Math.Max(0.30, _o.FrequencyTargets.GetValueOrDefault("legumbre", 3.0 / 7.0) * 0.8)));
        foreach (var (g, w) in new[] { ("arroz", 0.25), ("pasta", 0.25), ("patata", 0.15), ("cereal", 0.05) })
            if (starch.Any(x => x.P.Family == g)) starchAvail.Add((g, w));
        if (starchAvail.Count == 0 || _keto) _lunchStarch = Enumerable.Repeat("verdura", n).ToArray();
        else if (_lowCarb) _lunchStarch = Distribute(starchAvail.Where(x => x.Group is "legumbre" or "arroz" or "cereal").DefaultIfEmpty(starchAvail[0]).ToList(), n, rnd, true);
        else _lunchStarch = Distribute(starchAvail, n, rnd, true);

        // Segundos platos de comida y cena: 2n huecos repartidos entre grupos proteicos.
        var groups = new List<(string Group, double W)>();
        void AddGroup(string g, double w) { if (Pool(FoodProfileData.RoleProtein).Any(x => x.P.Family == g)) groups.Add((g, w)); }
        var fish = Math.Max(0.29, _o.FrequencyTargets.GetValueOrDefault("pescado_marisco", 2.0 / 7.0) * 0.9);
        if (_vegan) { AddGroup("vegetal", 0.40); if (legumes.Count > 0) groups.Add(("legumbre", 0.60)); }
        else if (_vegetarian) { AddGroup("huevo", 0.40); AddGroup("vegetal", 0.35); if (legumes.Count > 0) groups.Add(("legumbre", 0.25)); }
        else
        {
            AddGroup("ave", 0.24); AddGroup("pescado_blanco", fish * 0.50); AddGroup("pescado_azul", fish * 0.34);
            AddGroup("carne_roja", _keto ? 0.14 : 0.10); AddGroup("marisco", fish * 0.16); AddGroup("huevo", 0.20); AddGroup("vegetal", 0.02);
        }
        if (groups.Count == 0)
        {
            foreach (var g in Pool(FoodProfileData.RoleProtein).Select(x => x.P.Family).Distinct()) groups.Add((g, 1));
            if (groups.Count == 0 && legumes.Count > 0) groups.Add(("legumbre", 1));
        }
        // Cada "token" es un hueco de segundo plato asignado a un grupo; después se reparten entre comidas y cenas
        // (Choose) con preferencia por grupo, algo de azar y penalización por repetir el grupo del día anterior o el de la comida.
        var tokens = groups.Count == 0 ? new List<string>() : Distribute(groups, 2 * n, rnd, false).ToList();
        EnforceCapacity(tokens, groups, g => g == "legumbre" ? Pool(FoodProfileData.RoleLegume).Count : Pool(FoodProfileData.RoleProtein).Count(x => x.P.Family == g));
        _lunchProtein = new string[n]; _dinnerProtein = new string[n];
        var lunchPref = new Dictionary<string, double> { ["carne_roja"] = 3, ["pescado_azul"] = 2, ["ave"] = 2, ["pescado_blanco"] = 1.5, ["marisco"] = 1, ["legumbre"] = 1, ["vegetal"] = 1, ["huevo"] = -1 };
        var dinnerPref = new Dictionary<string, double> { ["huevo"] = 3, ["pescado_blanco"] = 2.5, ["marisco"] = 1.5, ["ave"] = 1.5, ["vegetal"] = 1.5, ["pescado_azul"] = 0.5, ["carne_roja"] = -1.5, ["legumbre"] = 0 };
        for (var d = 0; d < n && tokens.Count > 0; d++)
        {
            string Choose(Dictionary<string, double> pref, string? avoid1, string? avoid2)
            {
                var bestG = tokens[0]; var bestScore = double.MinValue;
                foreach (var tk in tokens.Distinct())
                {
                    var sc = pref.GetValueOrDefault(tk) + rnd.NextDouble() * 1.2;
                    if (tk == avoid1) sc -= 4;
                    if (tk == avoid2) sc -= 6;
                    if (sc > bestScore) { bestScore = sc; bestG = tk; }
                }
                tokens.Remove(bestG);
                return bestG;
            }
            _lunchProtein[d] = Choose(lunchPref, d > 0 ? _lunchProtein[d - 1] : null, null);
            _dinnerProtein[d] = tokens.Count > 0 ? Choose(dinnerPref, d > 0 ? _dinnerProtein[d - 1] : null, _lunchProtein[d]) : _lunchProtein[d];
        }
        for (var d = 0; d < n; d++)
        {
            _lunchProtein[d] ??= groups.Count > 0 ? groups[0].Group : "legumbre";
            _dinnerProtein[d] ??= _lunchProtein[d];
        }
    }

    /// <summary>
    /// Ningún grupo puede recibir más huecos de los que sus alimentos admiten con el límite semanal de repetición; el exceso
    /// se redistribuye a los grupos con capacidad. Evita, p. ej., siete segundos platos veganos con solo dos alimentos.
    /// </summary>
    private void EnforceCapacity(List<string> tokens, List<(string Group, double W)> groups, Func<string, int> foodCount)
    {
        var cap = Math.Max(1, _o.MaxWeeklyRepetitions);
        int Capacity(string g) => foodCount(g) * cap;
        var overflow = 0;
        foreach (var g in tokens.Distinct().ToList())
        {
            var allowed = Capacity(g);
            var count = tokens.Count(t => t == g);
            while (count > allowed && count > 0) { tokens.Remove(g); count--; overflow++; }
        }
        if (overflow == 0) return;
        var ordered = groups.OrderByDescending(x => x.W).Select(x => x.Group).Where(g => Capacity(g) > 0).ToList();
        if (ordered.Count == 0) return;
        var guard = 0;
        while (overflow > 0 && guard++ < 1000)
        {
            var target = ordered.FirstOrDefault(g => tokens.Count(t => t == g) < Capacity(g));
            if (target == null) { tokens.Add(ordered[0]); overflow--; _warnings.Add("Capacidad insuficiente de alimentos proteicos para el límite de repetición."); continue; }
            tokens.Add(target); overflow--;
        }
    }

    /// <summary>Reparte <paramref name="n"/> huecos entre grupos según pesos (resto mayor) y los ordena evitando repeticiones consecutivas.</summary>
    private static string[] Distribute(List<(string Group, double W)> groups, int n, Random rnd, bool avoidAdjacent)
    {
        var total = groups.Sum(g => g.W);
        var counts = groups.Select(g => (g.Group, Exact: g.W / total * n)).ToList();
        var assigned = counts.ToDictionary(c => c.Group, c => (int)Math.Floor(c.Exact));
        var left = n - assigned.Values.Sum();
        foreach (var c in counts.OrderByDescending(c => c.Exact - Math.Floor(c.Exact) + rnd.NextDouble() * 0.05).Take(left))
            assigned[c.Group]++;
        var list = assigned.SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).ToList();
        string[] bestOrder = list.ToArray();
        var bestCost = int.MaxValue;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var order = list.OrderBy(_ => rnd.Next()).ToArray();
            var cost = 0;
            for (var i = 1; i < order.Length; i++)
                if (order[i] == order[i - 1]) cost += avoidAdjacent ? 10 : 3;
            if (avoidAdjacent)
                for (var i = 2; i < order.Length; i++)
                    if (order[i] == "legumbre" && order[i - 2] == "legumbre") cost += 1;
            if (cost < bestCost) { bestCost = cost; bestOrder = order; }
            if (cost == 0) break;
        }
        return bestOrder;
    }

    // ───────────────────────── Selección ─────────────────────────

    // Elige un alimento por sorteo ponderado (determinista con la semilla del intento). Peso = comunidad del alimento
    // (Commonness^1.6) x señal terapéutica x preferencia, atenuado por repetición semanal, por repetir "kind" y por
    // haberlo usado el día anterior (x0.15). Si no hay candidatos, relaja en 3 pasos: (0) respeta el límite semanal y
    // el día, (1) ignora el límite semanal, (2) permite repetir en el día. Los alimentos "staple" (básicos de uso
    // diario: aceite, pan, leche, yogur natural) no se penalizan. Todos los factores numéricos son criterio de producto, origen no documentado.
    // Con optional=true no relaja y devuelve null si no hay candidato (el componente se omite).

    private EngineFood? Pick(IEnumerable<EngineFood> pool, Attempt a, MealCtx mealUsed, Func<EngineFood, bool>? filter = null, Func<EngineFood, double>? bias = null, bool optional = false)
    {
        var cands = filter == null ? pool.ToList() : pool.Where(filter).ToList();
        if (cands.Count == 0) return null;
        for (var relax = 0; relax < (optional ? 1 : 3); relax++)
        {
            var weights = new double[cands.Count];
            var total = 0.0;
            for (var i = 0; i < cands.Count; i++)
            {
                var f = cands[i];
                if (mealUsed.Ids.Contains(f.Id) || (f.P.Role != FoodProfileData.RoleOil && mealUsed.Kinds.Contains(f.P.Kind))) continue;
                var staple = f.P.IsStaple;
                var uses = _weeklyUse.GetValueOrDefault(f.Id) + a.Added.GetValueOrDefault(f.Id);
                if (!staple && relax == 0 && uses >= Math.Max(1, _o.MaxWeeklyRepetitions)) continue;
                if (!staple && relax < 2 && a.DayUsed.Contains(f.Id)) continue;
                var w = Math.Pow(Math.Max(1, (int)f.P.Commonness), 1.6) * f.TherapyFactor;
                if (f.Preferred) w *= 2.5;
                if (f.Disliked) w *= 0.03;
                if (!staple)
                {
                    w /= 1 + uses * 1.5;
                    var kindUses = _weeklyKind.GetValueOrDefault(f.P.Kind) + a.AddedKind.GetValueOrDefault(f.P.Kind);
                    w /= 1 + 0.7 * kindUses;
                    if (_prevDay.Contains(f.Id)) w *= 0.15;
                }
                if (bias != null) w *= bias(f);
                weights[i] = w; total += w;
            }
            if (total <= 0) continue;
            var r = a.Rnd.NextDouble() * total;
            for (var i = 0; i < cands.Count; i++)
            {
                r -= weights[i];
                if (r <= 0 && weights[i] > 0) return cands[i];
            }
            return cands[weights.Select((w, i) => (w, i)).Where(x => x.w > 0).OrderByDescending(x => x.w).First().i];
        }
        return null;
    }

    private void Register(EngineFood f, Attempt a, MealCtx mealUsed)
    {
        mealUsed.Ids.Add(f.Id);
        mealUsed.Kinds.Add(f.P.Kind);
        a.DayUsed.Add(f.Id);
        if (f.P.IsStaple) return;
        a.Added[f.Id] = a.Added.GetValueOrDefault(f.Id) + 1;
        a.AddedKind[f.P.Kind] = a.AddedKind.GetValueOrDefault(f.P.Kind) + 1;
    }

    private bool Add(List<Slot> slots, Attempt a, string meal, int mealIndex, string course, EngineFood? food, MealCtx mealUsed)
    {
        if (food == null) return false;
        Register(food, a, mealUsed);
        slots.Add(new Slot { Food = food, Meal = meal, MealIndex = mealIndex, Course = course });
        return true;
    }

    // ───────────────────────── Plantillas de comidas ─────────────────────────

    // Cada comida usa una plantilla según su nombre (desayuno, media mañana, merienda, cena; cualquier otro nombre se
    // trata como comida principal). Esto acopla el motor a los nombres de GetMealSplits o de la plantilla profesional.
    // Los "Course" ("Primer plato", "Segundo plato", "Guarnición"...) son los roles culinarios que se guardan en CourseRole.
    private List<Slot> ComposeDay(int day, Attempt a)
    {
        var slots = new List<Slot>();
        for (var m = 0; m < _o.Meals.Count; m++)
        {
            var name = _o.Meals[m].Name;
            var lower = name.ToLowerInvariant();
            var used = new MealCtx();
            if (lower.Contains("desayuno")) Breakfast(slots, a, name, m, day, used);
            else if (lower.Contains("media")) MidMorning(slots, a, name, m, day, used);
            else if (lower.Contains("merienda")) Snack(slots, a, name, m, day, used);
            else if (lower.Contains("cena")) Dinner(slots, a, name, m, day, used);
            else Lunch(slots, a, name, m, day, used);
        }
        return slots;
    }

    // Franja de uso del perfil culinario: B = desayuno, S = snack (media mañana/merienda), M = comidas principales.
    private bool InSlot(EngineFood f, string slot) => f.P.HasSlot(slot);

    // En cetogénica los lácteos con hidratos (yogur, kéfir) solo entran en el desayuno; el resto de ingestas usan queso.
    private bool DairyOk(EngineFood f) => !_keto || f.P.HasTag("cheese");

    private void Breakfast(List<Slot> slots, Attempt a, string name, int m, int day, MealCtx used)
    {
        var dairy = Pool(FoodProfileData.RoleDairy).Where(f => InSlot(f, "B")).ToList();
        EngineFood? dairyPick = null;
        if (_keto) dairyPick = Pick(dairy, a, used, f => f.P.HasTag("yogurt") || f.P.HasTag("cheese"));
        else dairyPick = Pick(dairy, a, used, f => !f.P.HasTag("cheese"));
        Add(slots, a, name, m, "Lácteo", dairyPick, used);

        EngineFood? bread = null;
        if (!_keto)
        {
            var bases = Pool(FoodProfileData.RoleBread).Concat(Pool(FoodProfileData.RoleCereal)).Where(f => InSlot(f, "B")).ToList();
            // Sin pan en el catálogo disponible (p. ej. paciente que no lo quiere) quedan pocos cereales: la base es opcional
            // para no forzar repeticiones por encima del máximo semanal.
            bread = Pick(bases, a, used, null, f => f.P.Role == FoodProfileData.RoleBread ? 1.5 : 1.0, optional: Pool(FoodProfileData.RoleBread).Count == 0);
            Add(slots, a, name, m, "Base desayuno", bread, used);
            if (bread != null && bread.P.Role == FoodProfileData.RoleBread && a.Rnd.NextDouble() < 0.6)
                Add(slots, a, name, m, "Aliño / Cocinado", Pick(Pool(FoodProfileData.RoleOil), a, used, f => InSlot(f, "B"), null, true), used);
        }
        if (!_keto || day % 2 == 0)
        {
            var fruit = Pick(Pool(FoodProfileData.RoleFruit), a, used, f => InSlot(f, "B"), null, _keto);
            Add(slots, a, name, m, "Fruta fresca", fruit, used);
        }
        if (_keto || day % 2 == 1 || dairyPick == null)
        {
            var topping = Pick(Pool(FoodProfileData.RoleCold).Concat(Pool(FoodProfileData.RoleProtein).Where(f => f.P.Family == "huevo" && InSlot(f, "B"))).Concat(Pool(FoodProfileData.RoleDairy).Where(f => f.P.HasTag("cheese"))),
                a, used, f => InSlot(f, "B") && (!_keto || f.P.Role != FoodProfileData.RoleCold || true));
            Add(slots, a, name, m, "Acompañamiento proteico", topping, used);
            if (_keto) Add(slots, a, name, m, "Grasa saludable", Pick(Pool(FoodProfileData.RoleNut).Concat(Pool(FoodProfileData.RoleFat)), a, used, f => InSlot(f, "B") || InSlot(f, "S")), used);
        }
        if (slots.Count(s => s.MealIndex == m) == 0)
            Add(slots, a, name, m, "Fruta fresca", Pick(Pool(FoodProfileData.RoleFruit), a, used), used);
    }

    private void MidMorning(List<Slot> slots, Attempt a, string name, int m, int day, MealCtx used)
    {
        if (!_keto) Add(slots, a, name, m, "Fruta fresca", Pick(Pool(FoodProfileData.RoleFruit), a, used, f => InSlot(f, "S"), null, true), used);
        var kind = (day + a.Rnd.Next(2)) % 3;
        if (kind == 0 || _keto) Add(slots, a, name, m, "Frutos secos", Pick(Pool(FoodProfileData.RoleNut), a, used), used);
        else if (kind == 1) Add(slots, a, name, m, "Colación proteica", Pick(Pool(FoodProfileData.RoleDairy), a, used, f => InSlot(f, "S") && !f.P.HasTag("milk") && DairyOk(f)), used);
        else Add(slots, a, name, m, "Colación proteica", Pick(Pool(FoodProfileData.RoleDairy).Concat(Pool(FoodProfileData.RoleNut)), a, used, f => InSlot(f, "S") && (f.P.Role != FoodProfileData.RoleDairy || DairyOk(f))), used);
        if (slots.Count(s => s.MealIndex == m) == 0) Add(slots, a, name, m, "Fruta fresca", Pick(Pool(FoodProfileData.RoleFruit), a, used), used);
    }

    private void Snack(List<Slot> slots, Attempt a, string name, int m, int day, MealCtx used)
    {
        if (!_keto)
        {
            var bases = Pool(FoodProfileData.RoleBread).Concat(Pool(FoodProfileData.RoleCereal)).Where(f => InSlot(f, "S")).ToList();
            Add(slots, a, name, m, "Base merienda", Pick(bases, a, used, null, null, optional: Pool(FoodProfileData.RoleBread).Count == 0), used);
        }
        var protein = Pool(FoodProfileData.RoleCold).Concat(Pool(FoodProfileData.RoleDairy).Where(f => !f.P.HasTag("milk") && DairyOk(f))).Concat(Pool(FoodProfileData.RoleNut)).Where(f => InSlot(f, "S")).ToList();
        Add(slots, a, name, m, "Acompañamiento proteico", Pick(protein, a, used, f => !_vegetarian || DietSemanticValidationService.IsFoodCompatibleWithDietType(f.Food.name, "Vegetariana", f.Food.category, f.Food.carbs, f.Food.fat, foodId: f.Food.id)), used);
        if (!_keto && a.Rnd.NextDouble() < 0.5)
            Add(slots, a, name, m, "Fruta fresca", Pick(Pool(FoodProfileData.RoleFruit), a, used, f => InSlot(f, "S"), null, true), used);
        if (slots.Count(s => s.MealIndex == m) == 0) Add(slots, a, name, m, "Fruta fresca", Pick(Pool(FoodProfileData.RoleFruit), a, used), used);
    }

    private IEnumerable<EngineFood> ProteinFor(string group)
    {
        if (group == "legumbre") return Pool(FoodProfileData.RoleLegume);
        return Pool(FoodProfileData.RoleProtein).Where(f => f.P.Family == group);
    }

    private void Lunch(List<Slot> slots, Attempt a, string name, int m, int day, MealCtx used)
    {
        var starchGroup = _lunchStarch[Math.Min(day, _lunchStarch.Length - 1)];
        var proteinGroup = _lunchProtein[Math.Min(day, _lunchProtein.Length - 1)];
        var first = starchGroup == "verdura"
            ? Pick(Pool(FoodProfileData.RoleVegetable), a, used, f => f.P.HasTag("cooked") && f.P.Commonness >= 3)
            : starchGroup == "legumbre" ? Pick(Pool(FoodProfileData.RoleLegume), a, used) : Pick(Pool(FoodProfileData.RoleStarch), a, used, f => f.P.Family == starchGroup);
        first ??= Pick(Pool(FoodProfileData.RoleStarch).Concat(Pool(FoodProfileData.RoleLegume)), a, used);
        Add(slots, a, name, m, "Primer plato", first, used);

        // Si el primer plato ya es legumbre, el segundo no repite legumbre.
        var group = starchGroup == "legumbre" && proteinGroup == "legumbre" ? "huevo" : proteinGroup;
        var second = Pick(ProteinFor(group), a, used) ?? Pick(Pool(FoodProfileData.RoleProtein), a, used);
        Add(slots, a, name, m, "Segundo plato", second, used);

        var salad = day % 3 != 1;
        var side = Pick(Pool(FoodProfileData.RoleVegetable), a, used, f => salad ? f.P.HasTag("salad") : f.P.HasTag("cooked"))
                   ?? Pick(Pool(FoodProfileData.RoleVegetable), a, used);
        Add(slots, a, name, m, "Guarnición", side, used);

        if (!_keto && !_lowCarb && a.Rnd.NextDouble() < 0.7)
            Add(slots, a, name, m, "Pan", Pick(Pool(FoodProfileData.RoleBread), a, used, f => InSlot(f, "M"), null, true), used);

        Dessert(slots, a, name, m, used, fruitFirst: a.Rnd.NextDouble() < 0.65);

        Add(slots, a, name, m, "Aliño / Cocinado", Pick(Pool(FoodProfileData.RoleOil), a, used, f => InSlot(f, "M")), used);
    }

    /// <summary>Postre opcional: fruta o yogur; si no queda ninguno por debajo del límite semanal, no se añade postre.</summary>
    private void Dessert(List<Slot> slots, Attempt a, string name, int m, MealCtx used, bool fruitFirst)
    {
        EngineFood? FruitPick() => _keto ? null : Pick(Pool(FoodProfileData.RoleFruit), a, used, f => InSlot(f, "M"), null, true);
        EngineFood? YogurtPick() => _keto ? null : Pick(Pool(FoodProfileData.RoleDairy), a, used, f => f.P.HasTag("yogurt") && InSlot(f, "M"), null, true);
        var pick = fruitFirst ? FruitPick() ?? YogurtPick() : YogurtPick() ?? FruitPick();
        Add(slots, a, name, m, "Postre", pick, used);
    }

    private void Dinner(List<Slot> slots, Attempt a, string name, int m, int day, MealCtx used)
    {
        var proteinGroup = _dinnerProtein[Math.Min(day, _dinnerProtein.Length - 1)];
        var lunchStarch = _lunchStarch[Math.Min(day, _lunchStarch.Length - 1)];
        var cookedFirst = day % 2 == 0 || _keto;
        if (cookedFirst)
            Add(slots, a, name, m, "Primer plato", Pick(Pool(FoodProfileData.RoleVegetable), a, used, f => f.P.HasTag("cooked")), used);

        var second = Pick(ProteinFor(proteinGroup), a, used) ?? Pick(Pool(FoodProfileData.RoleProtein), a, used);
        Add(slots, a, name, m, "Segundo plato", second, used);

        Add(slots, a, name, m, "Guarnición", Pick(Pool(FoodProfileData.RoleVegetable), a, used, f => f.P.HasTag("salad") || !cookedFirst) ?? Pick(Pool(FoodProfileData.RoleVegetable), a, used), used);

        if (!_keto && !_lowCarb)
        {
            if (a.Rnd.NextDouble() < 0.5 || !Pool(FoodProfileData.RoleStarch).Any())
                Add(slots, a, name, m, "Pan", Pick(Pool(FoodProfileData.RoleBread), a, used, f => InSlot(f, "M")), used);
            else
                Add(slots, a, name, m, "Acompañamiento", Pick(Pool(FoodProfileData.RoleStarch), a, used, f => f.P.Family != lunchStarch), used);
            if (!slots.Any(s => s.MealIndex == m && (s.Food.P.Role is FoodProfileData.RoleBread or FoodProfileData.RoleStarch or FoodProfileData.RoleLegume)))
                Add(slots, a, name, m, "Acompañamiento", Pick(Pool(FoodProfileData.RoleStarch).Concat(Pool(FoodProfileData.RoleLegume)).Concat(Pool(FoodProfileData.RoleBread)), a, used), used);
        }
        else if (_lowCarb && a.Rnd.NextDouble() < 0.4)
            Add(slots, a, name, m, "Pan", Pick(Pool(FoodProfileData.RoleBread), a, used), used);

        Dessert(slots, a, name, m, used, fruitFirst: a.Rnd.NextDouble() < 0.4);

        Add(slots, a, name, m, "Aliño / Cocinado", Pick(Pool(FoodProfileData.RoleOil), a, used, f => InSlot(f, "M")), used);
    }

    // ───────────────────────── Solver de porciones ─────────────────────────

    private sealed class FixedLoad
    {
        public double K, P, C, F;
        public double[] MealK = Array.Empty<double>();
    }

    // Ajuste de raciones: descenso de coordenadas sobre los gramos de cada hueco, dentro de [Min, Max] y en pasos de Step.
    // Minimiza J = suma ponderada de errores relativos al cuadrado de kcal, proteína, hidratos y grasa del día, más un
    // término pequeño por desviación de kcal de cada comida respecto a su reparto. Prueba pasos x1, x2 y x4; cuando
    // ningún movimiento simple mejora, prueba pares (sube uno, baja otro). Devuelve J y, en "acceptable", si el día queda
    // dentro de las tolerancias. fixedLoad aporta carga ya fijada (recetas); keepGrams parte de los gramos actuales.
    private double SolveDay(List<Slot> slots, out bool acceptable, FixedLoad? fixedLoad = null, bool keepGrams = false)
    {
        var n = slots.Count;
        var mealCount = _o.Meals.Count;
        var k = new double[n]; var pr = new double[n]; var c = new double[n]; var f = new double[n];
        for (var i = 0; i < n; i++)
        {
            var s = slots[i];
            var (min, max, def) = PortionPolicy.Bounds(s.Food.P, _o.Kcal, _keto || _lowCarb);
            s.Min = min; s.Max = max; s.Step = s.Food.P.Step > 0 ? s.Food.P.Step : 10;
            if (keepGrams) s.G = Math.Clamp(s.G, s.Min, s.Max); else s.G = def;
            k[i] = s.Food.K / 100.0; pr[i] = s.Food.Pr / 100.0; c[i] = s.Food.C / 100.0; f[i] = s.Food.F / 100.0;
        }
        double tK = fixedLoad?.K ?? 0, tP = fixedLoad?.P ?? 0, tC = fixedLoad?.C ?? 0, tF = fixedLoad?.F ?? 0;
        var mealK = new double[mealCount];
        if (fixedLoad != null)
            for (var mi = 0; mi < mealCount && mi < fixedLoad.MealK.Length; mi++) mealK[mi] = fixedLoad.MealK[mi];
        for (var i = 0; i < n; i++)
        {
            tK += k[i] * slots[i].G; tP += pr[i] * slots[i].G; tC += c[i] * slots[i].G; tF += f[i] * slots[i].G;
            mealK[slots[i].MealIndex] += k[i] * slots[i].G;
        }
        var mealTarget = _o.Meals.Select(x => _o.Kcal * x.KcalPct).ToArray();

        // Pesos de J: kcal 0.35, proteína 0.25, hidratos 0.20, grasa 0.20, reparto por comida 0.03. Criterio de producto
        // (prioriza energía y proteína); origen no documentado.
        double J(double a, double b, double cc, double d, double[] mk)
        {
            var j = 0.0;
            if (_o.Kcal > 0) j += 0.35 * Sq((a - _o.Kcal) / _o.Kcal);
            if (_o.Protein > 0) j += 0.25 * Sq((b - _o.Protein) / _o.Protein);
            if (_o.Carbs > 0) j += 0.20 * Sq((cc - _o.Carbs) / _o.Carbs);
            if (_o.Fat > 0) j += 0.20 * Sq((d - _o.Fat) / _o.Fat);
            if (_o.Kcal > 0)
                for (var mi = 0; mi < mk.Length; mi++)
                    if (mealTarget[mi] > 0) j += 0.03 * Sq((mk[mi] - mealTarget[mi]) / mealTarget[mi]);
            return j;
        }

        var cur = J(tK, tP, tC, tF, mealK);
        var tmpMeal = new double[mealCount];
        int[] mults = { 1, 2, 4 };
        // 600 iteraciones: tope de seguridad; normalmente converge antes (sin movimiento que mejore J).
        for (var iter = 0; iter < 600; iter++)
        {
            var bestJ = cur; var bestI = -1; var bestJi = -1; var bestD = 0.0; var bestD2 = 0.0;
            for (var i = 0; i < n; i++)
            {
                var s = slots[i];
                foreach (var mult in mults)
                    foreach (var sign in new[] { -1, 1 })
                    {
                        var ng = Math.Clamp(s.G + sign * mult * s.Step, s.Min, s.Max);
                        var d = ng - s.G;
                        if (Math.Abs(d) < 1e-9) continue;
                        Array.Copy(mealK, tmpMeal, mealCount);
                        tmpMeal[s.MealIndex] += k[i] * d;
                        var j = J(tK + k[i] * d, tP + pr[i] * d, tC + c[i] * d, tF + f[i] * d, tmpMeal);
                        if (j < bestJ - 1e-12) { bestJ = j; bestI = i; bestD = d; bestJi = -1; }
                    }
            }
            if (bestI < 0)
            {
                // Movimientos por pares (+/-): transfieren energía entre componentes sin alterar el total.
                for (var i = 0; i < n; i++)
                    for (var jx = 0; jx < n; jx++)
                    {
                        if (i == jx) continue;
                        var si = slots[i]; var sj = slots[jx];
                        var ngi = Math.Clamp(si.G + si.Step, si.Min, si.Max); var di = ngi - si.G;
                        var ngj = Math.Clamp(sj.G - sj.Step, sj.Min, sj.Max); var dj = ngj - sj.G;
                        if (Math.Abs(di) < 1e-9 || Math.Abs(dj) < 1e-9) continue;
                        Array.Copy(mealK, tmpMeal, mealCount);
                        tmpMeal[si.MealIndex] += k[i] * di; tmpMeal[sj.MealIndex] += k[jx] * dj;
                        var j = J(tK + k[i] * di + k[jx] * dj, tP + pr[i] * di + pr[jx] * dj, tC + c[i] * di + c[jx] * dj, tF + f[i] * di + f[jx] * dj, tmpMeal);
                        if (j < bestJ - 1e-12) { bestJ = j; bestI = i; bestD = di; bestJi = jx; bestD2 = dj; }
                    }
            }
            if (bestI < 0) break;
            var sb = slots[bestI];
            sb.G += bestD; tK += k[bestI] * bestD; tP += pr[bestI] * bestD; tC += c[bestI] * bestD; tF += f[bestI] * bestD;
            mealK[sb.MealIndex] += k[bestI] * bestD;
            if (bestJi >= 0)
            {
                var sj = slots[bestJi];
                sj.G += bestD2; tK += k[bestJi] * bestD2; tP += pr[bestJi] * bestD2; tC += c[bestJi] * bestD2; tF += f[bestJi] * bestD2;
                mealK[sj.MealIndex] += k[bestJi] * bestD2;
            }
            cur = bestJ;
        }

        // Tolerancias de aceptación del motor: kcal +/-5 %, macros +/-12 %. Criterio de producto; son el contrato de
        // calidad que luego comprueba la validación semántica (no son un límite clínico).
        acceptable = (_o.Kcal <= 0 || Math.Abs(tK - _o.Kcal) / _o.Kcal <= 0.05) &&
                     (_o.Protein <= 0 || Math.Abs(tP - _o.Protein) / _o.Protein <= 0.12) &&
                     (_o.Carbs <= 0 || Math.Abs(tC - _o.Carbs) / _o.Carbs <= 0.12) &&
                     (_o.Fat <= 0 || Math.Abs(tF - _o.Fat) / _o.Fat <= 0.12);
        return cur;
    }

    private static double Sq(double x) => x * x;

    // ───────────────────────── DTO ─────────────────────────

    // Los gramos se redondean a entero y los macros se recalculan desde ese valor redondeado (lo mostrado es coherente
    // con lo que se suma). ExchangeCount son raciones de intercambio, solo si el alimento define gramos por ración.

    private static MealItemDto BuildItem(EngineFood food, double grams, string course)
    {
        var g = Math.Round(grams, 0);
        var ratio = g / 100.0;
        var f = food.Food;
        return new MealItemDto
        {
            FoodId = f.id,
            FoodName = f.name,
            Grams = (decimal)g,
            Kcal = (decimal)Math.Round(food.K * ratio, 1),
            Protein = (decimal)Math.Round(food.Pr * ratio, 1),
            Carbs = (decimal)Math.Round(food.C * ratio, 1),
            Fat = (decimal)Math.Round(food.F * ratio, 1),
            ExchangeGroupId = f.exchange_group_id,
            ExchangeGroupName = f.exchange_group?.name,
            ExchangeCount = f.grams_per_exchange.HasValue && f.grams_per_exchange.Value > 0
                ? (decimal?)Math.Round((decimal)g / f.grams_per_exchange.Value, 1)
                : null,
            CourseRole = course
        };
    }

    /// <summary>
    /// Reajusta las cantidades de un día ya construido (p. ej. tras introducir recetas). Los elementos sin alimento del
    /// catálogo (recetas) se tratan como carga fija; el resto se resuelve con el mismo solver y las mismas porciones.
    /// </summary>
    public static void Rebalance(DietDayDto day, IReadOnlyDictionary<int, EngineFood> map, MenuEngineOptions options)
    {
        var engine = new DietMenuEngine(options);
        var meals = day.Meals.ToList();
        var fixedLoad = new FixedLoad { MealK = new double[meals.Count] };
        var slots = new List<Slot>();
        var itemOf = new Dictionary<Slot, MealItemDto>();
        for (var m = 0; m < meals.Count; m++)
            foreach (var item in meals[m].Items)
            {
                if (item.FoodId.HasValue && map.TryGetValue(item.FoodId.Value, out var food) && (item.Grams ?? 0) > 0)
                {
                    var slot = new Slot { Food = food, Meal = meals[m].Name, MealIndex = m, Course = item.CourseRole ?? "", G = (double)item.Grams! };
                    slots.Add(slot); itemOf[slot] = item;
                }
                else
                {
                    var k = (double)(item.Kcal ?? 0);
                    fixedLoad.K += k; fixedLoad.P += (double)(item.Protein ?? 0); fixedLoad.C += (double)(item.Carbs ?? 0); fixedLoad.F += (double)(item.Fat ?? 0);
                    fixedLoad.MealK[m] += k;
                }
            }
        if (slots.Count == 0) return;
        engine.SolveDay(slots, out _, fixedLoad, keepGrams: true);
        foreach (var slot in slots)
        {
            var rebuilt = BuildItem(slot.Food, slot.G, slot.Course);
            var item = itemOf[slot];
            item.Grams = rebuilt.Grams; item.Kcal = rebuilt.Kcal; item.Protein = rebuilt.Protein; item.Carbs = rebuilt.Carbs; item.Fat = rebuilt.Fat;
            item.ExchangeCount = rebuilt.ExchangeCount;
        }
    }

    private DietDayDto ToDto(int day, List<Slot> slots)
    {
        var dto = new DietDayDto { DayIndex = day };
        var meals = new List<MealDto>();
        for (var m = 0; m < _o.Meals.Count; m++)
        {
            var items = new List<MealItemDto>();
            foreach (var s in slots.Where(x => x.MealIndex == m))
                items.Add(BuildItem(s.Food, s.G, s.Course));
            meals.Add(new MealDto { Name = _o.Meals[m].Name, MealIndex = m, Items = items });
        }
        dto.Meals = meals;
        return dto;
    }
}
