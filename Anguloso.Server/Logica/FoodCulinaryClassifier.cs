using System.Globalization;
using System.Text;
using Anguloso.Server.Models;

namespace Anguloso.Server.Logica;

/// <summary>Perfil culinario de un alimento tal y como lo consume el motor de menús.</summary>
public sealed class FoodProfileData
{
    public const string RoleProtein = "Protein";
    public const string RoleStarch = "Starch";
    public const string RoleLegume = "Legume";
    public const string RoleVegetable = "Vegetable";
    public const string RoleFruit = "Fruit";
    public const string RoleDairy = "Dairy";
    public const string RoleBread = "Bread";
    public const string RoleCereal = "Cereal";
    public const string RoleOil = "Oil";
    public const string RoleNut = "Nut";
    public const string RoleCold = "Cold";
    public const string RoleFat = "Fat";

    public string Role { get; set; } = "";
    /// <summary>Grupo de rotación semanal (ave, carne_roja, pescado_blanco, pescado_azul, marisco, huevo, vegetal, legumbre, arroz, pasta, patata...).</summary>
    public string Family { get; set; } = "";
    public short Commonness { get; set; } = 3;
    /// <summary>B = desayuno, S = media mañana/merienda, M = comida/cena.</summary>
    public string[] Slots { get; set; } = Array.Empty<string>();
    public string State { get; set; } = "ready";
    public double Min { get; set; }
    public double Max { get; set; }
    public double Step { get; set; } = 10;
    public double Default { get; set; }
    public string? UnitName { get; set; }
    public double? UnitGrams { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public bool IsStaple { get; set; }
    public string? ExcludedReason { get; set; }

    public bool IsUsable => ExcludedReason == null && Role.Length > 0;
    public bool HasSlot(string slot) => Array.IndexOf(Slots, slot) >= 0;
    public bool HasTag(string tag) => Array.IndexOf(Tags, tag) >= 0;

    /// <summary>Variedad concreta dentro del grupo (merluza, pollo, lenteja...) para no repetir "casi lo mismo" en la semana.</summary>
    public string Kind
    {
        get
        {
            foreach (var tag in Tags)
                if (tag.StartsWith("kind:", StringComparison.Ordinal)) return tag.Substring(5);
            return Family;
        }
    }

    public static FoodProfileData FromEntity(food_culinary_profile e) => new()
    {
        Role = e.role, Family = e.family, Commonness = e.commonness, Slots = e.meal_slots ?? Array.Empty<string>(),
        State = e.cooking_state, Min = e.min_grams, Max = e.max_grams, Step = e.step_grams, Default = e.default_grams,
        UnitName = e.unit_name, UnitGrams = e.unit_grams, Tags = e.tags ?? Array.Empty<string>(),
        IsStaple = e.is_staple, ExcludedReason = e.excluded_reason
    };

    public void CopyTo(food_culinary_profile e)
    {
        e.role = Role; e.family = Family; e.commonness = Commonness; e.meal_slots = Slots; e.cooking_state = State;
        e.min_grams = Min; e.max_grams = Max; e.step_grams = Step; e.default_grams = Default;
        e.unit_name = UnitName; e.unit_grams = UnitGrams; e.tags = Tags;
        e.is_staple = IsStaple; e.excluded_reason = ExcludedReason;
        e.classifier_version = FoodCulinaryClassifier.Version; e.updated_at = DateTime.UtcNow;
    }
}

/// <summary>
/// Clasifica un alimento del catálogo por su identidad (nombre normalizado y coherencia energética) en un perfil culinario.
/// Es una lista blanca: lo que no reconoce no entra en la generación automática. Los alimentos reconocidos pero con datos
/// incoherentes (p. ej. «Arroz, hervido» con las kcal del crudo) se marcan con <see cref="FoodProfileData.ExcludedReason"/>.
/// Los pesos de cereales, legumbres, carnes y pescados se expresan en crudo cuando el catálogo los guarda en crudo.
/// </summary>
public static class FoodCulinaryClassifier
{
    public const int Version = 4;

    private static readonly string[] GlobalRejectWords =
    {
        "frito", "frita", "fritos", "fritas", "rebozad", "empanad", "churro", "ahumad", "escabeche", "salado", "salada",
        "salazon", "desecad", "polvo", "liofiliz", "almibar", "confitad", "azucarad", "edulcor", "mermelada", "zumo",
        "nectar", "licor", "vino", "cerveza", "batido", "helado", "chocolate", "galleta", "bolleria", "pastel", "sobao",
        "croissant", "donut", "salsa", "cubito", "caldo", "pate", "chorizo", "salchicha", "bacon", "panceta", "morcilla",
        "sobrasada", "mortadela", "fuet", "salami", "foie", "hamburguesa", "kebab", "pizza", "lasana", "canelones",
        "croqueta", "flamenquin", "empanadilla", "rollito", "paella", "fabada", "ensaladilla", "gazpacho", "mousse",
        "flan", "natillas", "pudding", "petit", "crema", "mayonesa", "ketchup", "vinagre", "infusion", "refresco",
        "suplemento", "complejo", "gelatina", "enlatada", "enlatado"
    };

    private static readonly Dictionary<string, string> FamilyOfFish = new()
    {
        ["merluza"] = "pescado_blanco", ["bacalao"] = "pescado_blanco", ["lubina"] = "pescado_blanco", ["dorada"] = "pescado_blanco",
        ["lenguado"] = "pescado_blanco", ["rape"] = "pescado_blanco", ["mero"] = "pescado_blanco", ["pescadilla"] = "pescado_blanco",
        ["gallo"] = "pescado_blanco", ["besugo"] = "pescado_blanco", ["rodaballo"] = "pescado_blanco", ["panga"] = "pescado_blanco",
        ["faneca"] = "pescado_blanco", ["bacaladilla"] = "pescado_blanco", ["platija"] = "pescado_blanco", ["fletan"] = "pescado_blanco",
        ["salmon"] = "pescado_azul", ["atun"] = "pescado_azul", ["sardina"] = "pescado_azul", ["caballa"] = "pescado_azul",
        ["boqueron"] = "pescado_azul", ["jurel"] = "pescado_azul", ["trucha"] = "pescado_azul", ["bonito"] = "pescado_azul"
    };

    private static readonly string[] Shellfish = { "gamba", "langostino", "cigala", "camaron" };
    private static readonly string[] Molluscs = { "calamar", "sepia", "pulpo", "chipiron", "mejillon", "almeja", "berberecho", "vieira" };

    // palabra, kind, ensalada, cocinada, popularidad, alérgeno
    private static readonly (string Word, bool Salad, bool Cooked, short Common, string? Allergen)[] Vegetables =
    {
        ("lechuga", true, false, 5, null), ("tomate", true, true, 5, null), ("pepino", true, false, 4, null),
        ("zanahoria", true, true, 5, null), ("canonigos", true, false, 3, null), ("rucula", true, false, 3, null),
        ("escarola", true, false, 3, null), ("endibia", true, false, 3, null), ("rabano", true, false, 2, null),
        ("apio", true, true, 2, "apio"), ("berro", true, false, 2, null), ("pimiento", true, true, 4, null),
        ("champinon", true, true, 4, null), ("remolacha", true, false, 2, null),
        ("calabacin", false, true, 5, null), ("brocoli", false, true, 4, null), ("brecol", false, true, 4, null),
        ("espinaca", false, true, 4, null), ("acelga", false, true, 3, null), ("berenjena", false, true, 4, null),
        ("coliflor", false, true, 3, null), ("calabaza", false, true, 3, null), ("judia", false, true, 4, null),
        ("esparrago", false, true, 3, null), ("alcachofa", false, true, 3, null), ("repollo", false, true, 3, null),
        ("col", false, true, 3, null), ("lombarda", false, true, 3, null), ("puerro", false, true, 3, null),
        ("seta", false, true, 3, null), ("nabo", false, true, 2, null), ("cardo", false, true, 2, null),
        ("menestra", false, true, 3, null), ("grelo", false, true, 2, null), ("borraja", false, true, 2, null)
    };

    private static readonly (string Word, short Common, double Min, double Max, double Step, double Def)[] Fruits =
    {
        ("manzana", 5, 100, 250, 50, 150), ("naranja", 5, 100, 250, 50, 150), ("platano", 5, 100, 150, 50, 100),
        ("pera", 4, 100, 250, 50, 150), ("mandarina", 5, 100, 250, 50, 150), ("fresa", 4, 100, 250, 50, 150),
        ("kiwi", 4, 100, 250, 50, 150), ("melocoton", 4, 100, 250, 50, 150), ("melon", 4, 150, 300, 50, 200),
        ("sandia", 4, 150, 300, 50, 200), ("uva", 3, 80, 200, 20, 120), ("cereza", 3, 80, 200, 20, 120),
        ("ciruela", 3, 100, 250, 50, 150), ("albaricoque", 3, 100, 250, 50, 150), ("nectarina", 3, 100, 250, 50, 150),
        ("pina", 3, 100, 250, 50, 150), ("pomelo", 2, 100, 250, 50, 150), ("granada", 2, 100, 200, 50, 100),
        ("higos", 2, 100, 200, 50, 100), ("caqui", 2, 100, 250, 50, 150), ("mango", 2, 100, 250, 50, 150),
        ("papaya", 2, 100, 250, 50, 150), ("frambuesa", 3, 60, 150, 20, 100), ("arandano", 3, 60, 150, 20, 100),
        ("paraguaya", 3, 100, 250, 50, 150), ("chirimoya", 2, 100, 250, 50, 150), ("mora", 2, 60, 150, 20, 100)
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastSpace = true;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var c = char.ToLowerInvariant(ch);
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) { sb.Append(c); lastSpace = false; }
            else if (!lastSpace) { sb.Append(' '); lastSpace = true; }
        }
        return sb.ToString().Trim();
    }

    /// <summary>Clasifica un alimento. Devuelve null si no es reconocible como alimento culinario común.</summary>
    public static FoodProfileData? Classify(string? name, string? category, double? kcal, double? protein, double? carbs, double? fat)
    {
        var n = Normalize(name);
        if (n.Length == 0 || n.Length > 70) return null;
        var tokens = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var set = new HashSet<string>(tokens, StringComparer.Ordinal);
        var padded = " " + n + " ";
        bool Has(string w) => padded.Contains(" " + w + " ", StringComparison.Ordinal);
        bool Starts(string w) => tokens.Length > 0 && tokens[0] == w;
        var k = kcal ?? 0;
        var conserva = Has("conserva");

        foreach (var reject in GlobalRejectWords)
            if (n.Contains(reject, StringComparison.Ordinal)) return null;
        if (Has("en") && Has("aceite") && !Starts("aceite")) return null;
        if (!conserva && Has("congelado") && (Has("pure") || Has("copos"))) return null;

        FoodProfileData? p =
            Oil(n, tokens) ?? Egg(n, Has, tokens) ?? Poultry(Has, tokens, n) ?? RedMeat(Has, tokens, n) ?? Fish(Has, tokens, n) ??
            PlantProtein(Has, tokens) ?? Cold(Has, n) ?? Dairy(n, Has, set, tokens) ?? Bread(Has, tokens, n) ?? Cereal(Has, tokens, n) ??
            Starch(Has, tokens, n, k) ?? Legume(Has, tokens, n, k, conserva) ?? Vegetable(Has, tokens, n, k, fat ?? 0, conserva) ??
            Fruit(Starts, tokens, n) ?? NutOrAvocado(Starts, tokens, n);
        if (p == null) return null;

        ApplyEnergyPlausibility(p, n, k, protein, carbs, fat);
        return p;
    }

    private static FoodProfileData Make(string role, string family, short common, string[] slots, string state, double min, double max, double step, double def, string kind, params string[] extraTags)
    {
        var tags = new List<string> { "kind:" + kind };
        tags.AddRange(extraTags);
        return new FoodProfileData
        {
            Role = role, Family = family, Commonness = common, Slots = slots, State = state, Min = min, Max = max,
            Step = step, Default = def, Tags = tags.ToArray()
        };
    }

    private static string[] S(params string[] s) => s;

    private static FoodProfileData? Oil(string n, string[] tokens)
    {
        if (!(tokens.Length >= 3 && tokens[0] == "aceite" && tokens[1] == "de" && (tokens[2] == "oliva" || tokens[2] == "girasol")))
            return null;
        if (n.Contains("ecologic", StringComparison.Ordinal)) return null;
        var p = Make(FoodProfileData.RoleOil, "aceite", (short)(tokens[2] == "oliva" ? 5 : 2), S("B", "M"), "ready", 5, 20, 5, 10, "aceite_" + tokens[2], "fat");
        p.IsStaple = true;
        return p;
    }

    private static bool StateIsCooked(Func<string, bool> has) =>
        has("plancha") || has("horno") || has("asado") || has("asada") || has("hervido") || has("hervida") || has("vapor") || has("parrilla") || has("cocido") || has("cocida");

    private static FoodProfileData? Egg(string n, Func<string, bool> has, string[] tokens)
    {
        if (!has("huevo") && !has("huevos") && !has("tortilla")) return null;
        if (has("codorniz") || has("pato") || has("pavo") || has("yema") || has("clara") || has("desecada") || has("revuelto") || has("mantequilla") || has("patata") || has("pasta") || has("flan"))
            return null;
        if (has("tortilla"))
        {
            if (!has("francesa")) return null;
            var t = Make(FoodProfileData.RoleProtein, "huevo", 4, S("M", "B"), "ready", 60, 180, 60, 120, "tortilla_francesa");
            t.UnitName = "tortilla"; t.UnitGrams = 60;
            return t;
        }
        var simple = tokens.Length == 1 || has("gallina") || has("hervido") || has("duro") || has("escalfado") || has("pasado") || has("fresco");
        if (!simple) return null;
        var p = Make(FoodProfileData.RoleProtein, "huevo", 5, S("M", "B", "S"), has("fresco") || tokens.Length == 1 ? "raw" : "cooked", 55, 165, 55, 110, "huevo");
        p.UnitName = "huevo"; p.UnitGrams = 55;
        return p;
    }

    private static FoodProfileData? Poultry(Func<string, bool> has, string[] tokens, string n)
    {
        var chicken = has("pollo");
        var turkey = has("pavo");
        var rabbit = has("conejo");
        if (!chicken && !turkey && !rabbit) return null;
        if (has("fiambre") || has("grasa") || has("corazon") || has("higado") || has("ala") || has("huevo") || has("muslo") && has("empanado")) return null;
        if (rabbit)
        {
            var r = Make(FoodProfileData.RoleProtein, "ave", 2, S("M"), StateIsCooked(has) ? "cooked" : "raw", 100, 220, 10, 150, "conejo");
            return r;
        }
        var lean = has("pechuga") || has("muslo") || has("entero") || has("crudo") || has("cruda") || has("asado") || has("plancha") || tokens.Length == 1 || has("de");
        if (!lean) return null;
        var kind = chicken ? "pollo" : "pavo";
        var common = (short)(has("pechuga") ? (chicken ? 5 : 4) : 3);
        var cooked = StateIsCooked(has);
        return Make(FoodProfileData.RoleProtein, "ave", common, S("M"), cooked ? "cooked" : "raw",
            cooked ? 80 : 100, cooked ? 180 : 220, 10, cooked ? 120 : 150, kind);
    }

    private static FoodProfileData? RedMeat(Func<string, bool> has, string[] tokens, string n)
    {
        string? kind = has("ternera") ? "ternera" : has("vaca") || has("buey") ? "ternera" : has("cerdo") ? "cerdo" : has("cordero") ? "cordero" : null;
        if (kind == null) return null;
        if (has("costilla") || has("callos") || has("rinon") || has("lengua") || has("corazon") || has("pulmon") || has("higado") || has("sesos") ||
            has("rabo") || has("picada") || has("cabezada") || has("chuleta") || has("sin") && has("especificar") || has("molleja") || has("pierna") && kind == "cerdo")
            return null;
        var lean = has("solomillo") || has("lomo") || has("filete") || has("magra") || has("jamon") && kind == "cerdo";
        if (!lean) return null;
        var cooked = StateIsCooked(has);
        var common = (short)(has("solomillo") || has("lomo") ? 4 : 3);
        return Make(FoodProfileData.RoleProtein, "carne_roja", common, S("M"), cooked ? "cooked" : "raw",
            cooked ? 80 : 100, cooked ? 180 : 220, 10, cooked ? 110 : 140, kind);
    }

    private static FoodProfileData? Fish(Func<string, bool> has, string[] tokens, string n)
    {
        if (has("ahumado") || has("ahumada") || has("salado") || has("anchoas") || has("surimi") || has("rebozada")) return null;
        foreach (var (word, family) in FamilyOfFish)
        {
            if (!has(word)) continue;
            if (has("hueva") || has("aceite") || has("escabeche")) return null;
            var natural = word == "atun" && has("natural");
            var cooked = StateIsCooked(has) || natural;
            double min = cooked ? 80 : 120, max = cooked ? 200 : 250, def = cooked ? 130 : 170;
            if (natural) { min = 50; max = 120; def = 80; }
            var common = (short)(word is "merluza" or "salmon" or "atun" or "bacalao" or "dorada" or "lubina" or "sardina" ? 5 : 3);
            var p = Make(FoodProfileData.RoleProtein, family, common, natural ? S("M", "S") : S("M"), cooked ? "cooked" : "raw", min, max, natural ? 10 : 10, def, word);
           
            return p;
        }
        foreach (var w in Shellfish)
            if (has(w) || has(w + "s"))
            {
                var cooked = StateIsCooked(has) || has("congelada");
                var p = Make(FoodProfileData.RoleProtein, "marisco", 3, S("M"), cooked ? "cooked" : "raw", 80, 200, 10, 120, w);
               
                return p;
            }
        foreach (var w in Molluscs)
            if (has(w) || has(w + "s"))
            {
                if (has("conserva") || has("escabeche")) return null;
                var p = Make(FoodProfileData.RoleProtein, "marisco", 3, S("M"), StateIsCooked(has) ? "cooked" : "raw", 80, 200, 10, 120, w);
               
                return p;
            }
        return null;
    }

    private static FoodProfileData? PlantProtein(Func<string, bool> has, string[] tokens)
    {
        if (has("tofu"))
        {
            var p = Make(FoodProfileData.RoleProtein, "vegetal", 3, S("M"), "ready", 100, 250, 10, 150, "tofu");
           
            return p;
        }
        if (has("tempeh"))
        {
            var p = Make(FoodProfileData.RoleProtein, "vegetal", 2, S("M"), "ready", 80, 160, 10, 120, "tempeh");
           
            return p;
        }
        if (has("seitan"))
        {
            var p = Make(FoodProfileData.RoleProtein, "vegetal", 2, S("M"), "ready", 80, 200, 10, 120, "seitan");
           
            return p;
        }
        return null;
    }

    private static FoodProfileData? Cold(Func<string, bool> has, string n)
    {
        FoodProfileData? p = null;
        if (has("jamon") && has("cocido")) p = Make(FoodProfileData.RoleCold, "fiambre_carne", 5, S("B", "S"), "ready", 30, 90, 10, 50, "jamon_cocido");
        else if ((has("pavo") || has("pollo")) && has("fiambre") && has("bajo") && has("grasa")) p = Make(FoodProfileData.RoleCold, "fiambre_carne", 4, S("B", "S"), "ready", 30, 90, 10, 50, has("pavo") ? "fiambre_pavo" : "fiambre_pollo");
        else if (has("jamon") && has("serrano")) p = Make(FoodProfileData.RoleCold, "fiambre_carne", 4, S("B", "S"), "ready", 20, 60, 10, 30, "jamon_serrano");
        return p;
    }

    private static FoodProfileData? Dairy(string n, Func<string, bool> has, HashSet<string> set, string[] tokens)
    {
        if (has("yogur") || has("yogurt"))
        {
            if (has("con") || has("sabor") && !has("natural") || has("fresa") || has("fresas") || has("frutas") || has("coco") || has("liquido") || has("cereales") ||
                has("vainilla") || has("aromatizado") || has("azucarado") || has("mousse") || has("nata") || has("enriquecido") && !has("natural")) return null;
            var natural = has("natural") || has("griego") || has("bulgaro") || tokens.Length <= 2;
            if (!natural) return null;
            var p = Make(FoodProfileData.RoleDairy, "lacteo", (short)(has("griego") ? 4 : 5), S("B", "S", "M"), "ready", 125, 250, 125, 125, has("griego") ? "yogur_griego" : "yogur", "yogurt");
            p.UnitName = "yogur"; p.UnitGrams = 125; p.IsStaple = !has("griego");
            return p;
        }
        if (has("kefir"))
        {
            var p = Make(FoodProfileData.RoleDairy, "lacteo", 3, S("B", "S"), "ready", 125, 250, 125, 125, "kefir", "yogurt");
            p.UnitName = "vaso"; p.UnitGrams = 125;
            return p;
        }
        if (Starts0(tokens, "leche") && !has("condensada") && !has("polvo") && !has("materna") && !has("cabra") && !has("oveja") &&
            !has("almendra") && !has("coco") && !has("fermentada") && !has("merengada") && !has("evaporada"))
        {
            var p = Make(FoodProfileData.RoleDairy, "lacteo", 5, S("B", "S"), "ready", 150, 300, 50, 200, "leche", "milk");
            p.UnitName = "vaso"; p.UnitGrams = 200; p.IsStaple = true;
            return p;
        }
        if (has("bebida") && has("soja") && !has("batido"))
        {
            var p = Make(FoodProfileData.RoleDairy, "bebida_vegetal", 3, S("B", "S"), "ready", 150, 300, 50, 200, "bebida_soja", "milk");
            p.UnitName = "vaso"; p.UnitGrams = 200; p.IsStaple = true;
            return p;
        }
        if ((has("queso") && has("fresco") && !has("cabra") && !has("frutas") && !has("desnatado") || has("requeson") || has("cuajada")))
        {
            var cuajada = has("cuajada");
            var p = Make(FoodProfileData.RoleDairy, "lacteo", 4, S("B", "S"), "ready", cuajada ? 125 : 40, cuajada ? 250 : 120, cuajada ? 125 : 20, cuajada ? 125 : 60, cuajada ? "cuajada" : "queso_fresco", "cheese");
           
            return p;
        }
        return null;
    }

    private static bool Starts0(string[] tokens, string w) => tokens.Length > 0 && tokens[0] == w;

    private static FoodProfileData? Bread(Func<string, bool> has, string[] tokens, string n)
    {
        if (!Starts0(tokens, "pan")) return null;
        if (has("tostado") || has("tostada") || has("rallado") || has("hamburguesa") || has("leche") || has("maiz") || has("blanco") ||
            has("molde") || has("cebada") || has("frito") || has("barra")) return null;
        var kind = has("centeno") ? "pan_centeno" : has("avena") ? "pan_avena" : has("integral") ? "pan_integral" : tokens.Length == 1 ? "pan" : null;
        if (kind == null) return null;
        var p = Make(FoodProfileData.RoleBread, "pan", (short)(kind == "pan_integral" ? 5 : 3), S("B", "S", "M"), "ready", 30, 90, 10, 60, kind);
        p.UnitName = "rebanada"; p.UnitGrams = 30; p.IsStaple = true;
        return p;
    }

    private static FoodProfileData? Cereal(Func<string, bool> has, string[] tokens, string n)
    {
        if (Starts0(tokens, "avena") && !has("harina") && !has("pan") && !has("cocida"))
        {
            var p = Make(FoodProfileData.RoleCereal, "cereal_desayuno", 4, S("B", "S"), "raw", 30, 70, 10, 40, "avena");
           
            return p;
        }
        if (Starts0(tokens, "muesli"))
        {
            var p = Make(FoodProfileData.RoleCereal, "cereal_desayuno", 3, S("B", "S"), "ready", 30, 70, 10, 40, "muesli");
           
            return p;
        }
        return null;
    }

    private static readonly HashSet<string> RiceWords = new(StringComparer.Ordinal) { "arroz", "integral", "crudo", "cocido", "hervido", "blanco", "basmati", "largo", "redondo" };
    private static readonly HashSet<string> PastaWords = new(StringComparer.Ordinal) { "pasta", "alimenticia", "integral", "cruda", "cocida", "hervida", "con", "huevo", "espagueti", "espaguetis", "macarron", "macarrones", "fideo", "fideos", "tallarin", "tallarines" };
    private static readonly HashSet<string> PotatoWords = new(StringComparer.Ordinal) { "patata", "patatas", "cruda", "cocida", "hervida", "pelada" };
    private static readonly HashSet<string> LegumeWords = new(StringComparer.Ordinal)
    {
        "lenteja", "lentejas", "garbanzo", "garbanzos", "alubia", "alubias", "blanca", "blancas", "negra", "negras", "seca", "secas", "seco", "secos", "cruda", "crudas", "crudo", "crudos",
        "cocida", "cocidas", "cocido", "cocidos", "hervida", "hervidas", "hervido", "hervidos", "remojada", "remojado", "en", "conserva", "pardina", "pinta", "judia", "judias"
    };

    private static FoodProfileData? Starch(Func<string, bool> has, string[] tokens, string n, double kcal)
    {
        string? kind = null; string? family = null; double rawMin = 40, rawMax = 100, rawDef = 70;
        short common = 4;
        if (Starts0(tokens, "arroz") && tokens.All(RiceWords.Contains)) { kind = "arroz"; family = "arroz"; common = 5; }
        else if (Starts0(tokens, "pasta") && tokens.All(PastaWords.Contains) || tokens.Length > 0 && tokens.All(t => PastaWords.Contains(t)) && (has("espaguetis") || has("macarrones")))
        { kind = has("integral") ? "pasta_integral" : "pasta"; family = "pasta"; common = 5; }
        else if (Starts0(tokens, "patata") && tokens.All(PotatoWords.Contains)) { kind = "patata"; family = "patata"; common = 5; }
        else if (Starts0(tokens, "boniato") && !has("frito")) { kind = "boniato"; family = "patata"; common = 3; }
        else if (Starts0(tokens, "quinoa")) { kind = "quinoa"; family = "cereal"; common = 2; rawMin = 40; rawMax = 80; rawDef = 60; }
        else if (has("semola") && has("trigo") && !has("hervida")) { kind = "cuscus"; family = "cereal"; common = 3; }
        else if (has("semola") && has("trigo") && has("hervida")) { kind = "cuscus"; family = "cereal"; common = 3; }
        if (kind == null || family == null) return null;

        if (family == "patata")
        {
            var pp = Make(FoodProfileData.RoleStarch, family, common, S("M"), has("cruda") ? "raw" : "cooked", 150, 350, 50, 220, kind);
            return pp;
        }
        var cookedName = has("hervido") || has("hervida") || has("cocido") || has("cocida");
        string? excluded = null;
        string state;
        if (kcal >= 250) { state = "raw"; if (cookedName) excluded = "energia_incoherente_con_cocinado"; }
        else if (kcal > 0 && kcal <= 200) state = "cooked";
        else { state = "raw"; excluded = "estado_crudo_cocinado_ambiguo"; }
        var p = Make(FoodProfileData.RoleStarch, family, common, S("M"), state,
            state == "raw" ? rawMin : 120, state == "raw" ? rawMax : 300, 10, state == "raw" ? rawDef : 200, kind);
       
        p.ExcludedReason = excluded;
        return p;
    }

    private static FoodProfileData? Legume(Func<string, bool> has, string[] tokens, string n, double kcal, bool conserva)
    {
        if (tokens.Length == 0 || !tokens.All(LegumeWords.Contains)) return null;
        string? kind = has("lenteja") || has("lentejas") ? "lenteja" : has("garbanzo") || has("garbanzos") ? "garbanzo" : has("alubia") || has("alubias") ? "alubia" : null;
        if (kind == null) return null;
        var cookedName = has("hervida") || has("hervido") || has("cocida") || has("cocido") || has("cocidas") || has("cocidos") || has("hervidas") || has("remojada") || conserva;
        string state; string? excluded = null;
        if (kcal >= 250) { state = "raw"; if (cookedName) excluded = "energia_incoherente_con_cocinado"; }
        else if (kcal > 0 && kcal <= 200) state = "cooked";
        else { state = "raw"; excluded = "estado_crudo_cocinado_ambiguo"; }
        var common = (short)(kind == "alubia" ? 4 : 5);
        var p = Make(FoodProfileData.RoleLegume, "legumbre", common, S("M"), state, state == "raw" ? 40 : 150, state == "raw" ? 120 : 400, 10, state == "raw" ? 70 : 250, kind);
        p.ExcludedReason = excluded;
        return p;
    }

    private static readonly HashSet<string> VegStateWords = new(StringComparer.Ordinal)
    {
        "cruda", "crudo", "hervida", "hervido", "hervidas", "asado", "asada", "congelada", "congelado", "picada", "plancha", "verde", "verdes", "rojo", "roja", "blanca", "de", "y", "tierno", "pelado", "maduro", "negra",
        "bruselas", "rizada", "gallega", "lombarda", "del", "en", "conserva", "seta", "setas", "judias", "judia", "al", "vapor", "horno", "cocida", "cocido", "morron", "congeladas", "verduras"
    };

    private static FoodProfileData? Vegetable(Func<string, bool> has, string[] tokens, string n, double kcal, double fat, bool conserva)
    {
        if (conserva) return null;
        foreach (var (word, salad, cooked, common, allergen) in Vegetables)
        {
            if (!(tokens.Length > 0 && (tokens[0] == word || tokens[0] == word + "s" || tokens[0] == word + "es"))) continue;
            if (!tokens.Skip(1).All(t => VegStateWords.Contains(t) || t == word)) return null;
            if (word == "judia" && !has("verde")) return null;
            if (word == "col" && tokens.Length == 1) { }
            var tags = new List<string>();
            if (salad) tags.Add("salad");
            if (cooked) tags.Add("cooked");
            var isSalad = salad && !has("hervida") && !has("hervido") && !has("asado") && !has("asada");
            var p = Make(FoodProfileData.RoleVegetable, "verdura", common, S("M"), has("cruda") || has("crudo") || tokens.Length == 1 ? "raw" : "cooked",
                isSalad ? 80 : 100, isSalad ? 250 : 300, 10, isSalad ? 150 : 180, word, tags.ToArray());
            if (kcal > 60 || fat > 3) p.ExcludedReason = "datos_incoherentes_verdura";
            return p;
        }
        return null;
    }

    private static FoodProfileData? Fruit(Func<string, bool> starts, string[] tokens, string n)
    {
        if (tokens.Length == 0) return null;
        foreach (var (word, common, min, max, step, def) in Fruits)
        {
            if (!(tokens[0] == word || tokens[0] == word + "s" || tokens[0] == word + "es")) continue;
            if (tokens.Length > 4) return null;
            if (n.Contains("seca", StringComparison.Ordinal) || n.Contains("pasa", StringComparison.Ordinal) || n.Contains("deseca", StringComparison.Ordinal)) return null;
            var p = Make(FoodProfileData.RoleFruit, "fruta", common, S("B", "S", "M"), "raw", min, max, step, def, word);
            if (word is "fresa" or "frambuesa" or "arandano" or "mora") p.Tags = p.Tags.Append("berry").ToArray();
            if (word is "platano" or "uva" or "cereza" or "mango" or "caqui" or "higos") p.Tags = p.Tags.Append("highsugar").ToArray();
            return p;
        }
        return null;
    }

    private static FoodProfileData? NutOrAvocado(Func<string, bool> starts, string[] tokens, string n)
    {
        if (tokens.Length == 0) return null;
        var first = tokens[0];
        foreach (var nut in new[] { "almendra", "nuez", "avellana", "pistacho", "anacardo" })
        {
            if (!(first == nut || first == nut + "s" || (nut == "nuez" && first == "nueces"))) continue;
            if (n.Contains("cascara", StringComparison.Ordinal) || n.Contains("sal", StringComparison.Ordinal) && n.Contains("con sal", StringComparison.Ordinal) || n.Contains("moscada", StringComparison.Ordinal)) return null;
            if (tokens.Skip(1).Any(t => t is not ("cruda" or "crudo" or "tostada" or "tostado" or "natural" or "pelada" or "peladas" or "sin" or "sal"))) return null;
            var p = Make(FoodProfileData.RoleNut, "frutos_secos", (short)(nut is "almendra" or "nuez" ? 4 : 3), S("S", "B"), "ready", 15, 40, 5, 25, nut, "fat");
           
            return p;
        }
        if (first == "aguacate" && !n.Contains("congelado", StringComparison.Ordinal))
        {
            return Make(FoodProfileData.RoleFat, "aguacate", 3, S("B", "M"), "raw", 50, 120, 10, 80, "aguacate", "fat", "avocado");
        }
        return null;
    }


    private static void ApplyEnergyPlausibility(FoodProfileData p, string n, double kcal, double? protein, double? carbs, double? fat)
    {
        if (p.ExcludedReason != null) return;
        if (kcal <= 0) { p.ExcludedReason = "sin_energia"; return; }
        if (protein < 0 || carbs < 0 || fat < 0) { p.ExcludedReason = "macronutrientes_negativos"; return; }
        var pr = Math.Max(protein ?? 0, 0); var c = Math.Max(carbs ?? 0, 0); var f = Math.Max(fat ?? 0, 0);
        var atwater = pr * 4 + c * 4 + f * 9;
        if (atwater > 0 && Math.Abs(atwater - kcal) / Math.Max(kcal, 1) > 0.30 && Math.Abs(atwater - kcal) > 25)
            p.ExcludedReason = "energia_no_coincide_con_macros";
        if ((protein == null || carbs == null || fat == null) && p.Role != FoodProfileData.RoleOil)
            p.ExcludedReason ??= "macronutrientes_incompletos";
    }
}
