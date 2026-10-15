namespace Anguloso.Server.Logica;

public static class ShoppingListRules
{
    // Normaliza categorías heterogéneas de BEDCA/OFF/USDA y nombres libres para que
    // la lista profesional y la del paciente presenten siempre los mismos grupos.
    public static string NormalizeCategory(string? rawCategory, string? foodName)
    {
        var cat = (rawCategory ?? "").Trim();
        var name = (foodName ?? "").ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(cat) || cat.Equals("otros", StringComparison.OrdinalIgnoreCase))
        {
            if (name.Contains("pollo") || name.Contains("ternera") || name.Contains("cerdo") || name.Contains("pavo") || name.Contains("lomo") || name.Contains("conejo"))
                return "Carnes y Aves";
            if (name.Contains("atún") || name.Contains("atun") || name.Contains("merluza") || name.Contains("salmon") || name.Contains("salmón") || name.Contains("bacalao") || name.Contains("lubina") || name.Contains("dorada") || name.Contains("gamba") || name.Contains("langostino"))
                return "Pescados y Mariscos";
            if (name.Contains("leche") || name.Contains("queso") || name.Contains("yogur") || name.Contains("cuajada") || name.Contains("kéfir"))
                return "Lácteos y Derivados";
            if (name.Contains("huevo") || name.Contains("clara"))
                return "Huevos";
            if (name.Contains("manzana") || name.Contains("plátano") || name.Contains("platano") || name.Contains("naranja") || name.Contains("pera") || name.Contains("fresa") || name.Contains("kiwi") || name.Contains("mandarina"))
                return "Frutas";
            if (name.Contains("tomate") || name.Contains("lechuga") || name.Contains("espinaca") || name.Contains("calabacín") || name.Contains("cebolla") || name.Contains("zanahoria") || name.Contains("brócoli") || name.Contains("pepino"))
                return "Verduras y Hortalizas";
            if (name.Contains("arroz") || name.Contains("pasta") || name.Contains("pan") || name.Contains("avena") || name.Contains("quinoa") || name.Contains("macarrones") || name.Contains("espaguetis"))
                return "Cereales y Harinas";
            if (name.Contains("lenteja") || name.Contains("garbanzo") || name.Contains("alubia") || name.Contains("judía"))
                return "Legumbres";
            if (name.Contains("aceite") || name.Contains("oliva") || name.Contains("mantequilla"))
                return "Aceites y Grasas";
            if (name.Contains("nuez") || name.Contains("almendra") || name.Contains("avellana") || name.Contains("anacardo") || name.Contains("cacahuete") || name.Contains("pistacho"))
                return "Frutos Secos y Semillas";
            return "Otros Alimentos";
        }

        var lower = cat.ToLowerInvariant();
        if (lower.Contains("verdura") || lower.Contains("hortaliz")) return "Verduras y Hortalizas";
        if (lower.Contains("fruta")) return "Frutas";
        if (lower.Contains("carne") || lower.Contains("ave")) return "Carnes y Aves";
        if (lower.Contains("pescado") || lower.Contains("marisco")) return "Pescados y Mariscos";
        if (lower.Contains("lácteo") || lower.Contains("lacteo") || lower.Contains("queso") || lower.Contains("leche")) return "Lácteos y Derivados";
        if (lower.Contains("huevo")) return "Huevos";
        if (lower.Contains("legumbre")) return "Legumbres";
        if (lower.Contains("cereal") || lower.Contains("pasta") || lower.Contains("pan") || lower.Contains("harina")) return "Cereales y Harinas";
        if (lower.Contains("aceite") || lower.Contains("grasa")) return "Aceites y Grasas";
        if (lower.Contains("fruto seco") || lower.Contains("semilla")) return "Frutos Secos y Semillas";
        return cat;
    }

    // Convierte los gramos exactos de la dieta en una recomendación de compra práctica.
    public static (double roundedGrams, string commercialDesc) CalculateCommercialRounding(double grams, string foodName, string category)
    {
        var lower = foodName.ToLowerInvariant();

        if (lower.Contains("huevo") && !lower.Contains("clara"))
        {
            int eggs = (int)Math.Max(1, Math.Ceiling(grams / 55.0));
            return (eggs * 55.0, $"{eggs} unidad(es) ({eggs * 55} g aprox.)");
        }

        if (lower.Contains("atún") || lower.Contains("atun") || lower.Contains("bonito"))
        {
            int cans = (int)Math.Max(1, Math.Ceiling(grams / 80.0));
            double rounded = cans * 80.0;
            return (rounded, $"{cans} lata(s) de 80g ({rounded} g)");
        }

        if (lower.Contains("yogur"))
        {
            int pots = (int)Math.Max(1, Math.Ceiling(grams / 125.0));
            double rounded = pots * 125.0;
            return (rounded, $"{pots} envase(s) de 125g ({rounded} g)");
        }

        if (category == "Frutas")
        {
            int units = (int)Math.Max(1, Math.Round(grams / 160.0));
            double rounded = units * 160.0;
            if (grams >= 1000)
            {
                double kg = Math.Ceiling(grams / 500.0) * 0.5;
                return (kg * 1000.0, $"{kg:0.#} kg (aprox. {units} piezas)");
            }
            return (rounded, $"aprox. {units} pieza(s) ({grams:0} g)");
        }

        if (category == "Carnes y Aves" || category == "Pescados y Mariscos")
        {
            if (grams < 250)
            {
                double rounded = Math.Ceiling(grams / 50.0) * 50.0;
                return (rounded, $"{rounded:0} g");
            }
            if (grams < 1000)
            {
                double rounded = Math.Ceiling(grams / 100.0) * 100.0;
                return (rounded, $"{rounded:0} g");
            }
            double kgRounded = Math.Ceiling(grams / 250.0) * 0.25;
            return (kgRounded * 1000.0, $"{kgRounded:0.##} kg");
        }

        if (category == "Aceites y Grasas")
        {
            if (grams <= 250) return (250, "1 envase / botella (250 ml/g)");
            if (grams <= 500) return (500, "1 botella (500 ml)");
            return (1000, "1 botella (1 Litro)");
        }

        if (category == "Cereales y Harinas" || category == "Legumbres")
        {
            if (grams <= 500) return (500, $"1 paquete de 500g (usará {grams:0} g)");
            if (grams <= 1000) return (1000, $"1 paquete de 1 kg (usará {grams:0} g)");
            int packs = (int)Math.Ceiling(grams / 1000.0);
            return (packs * 1000, $"{packs} paquetes de 1 kg ({packs * 1000} g)");
        }

        if (grams >= 1000)
        {
            double kg = Math.Ceiling(grams / 250.0) * 0.25;
            return (kg * 1000.0, $"{kg:0.##} kg");
        }

        double roundedGeneral = Math.Ceiling(grams / 25.0) * 25.0;
        return (roundedGeneral, $"{roundedGeneral:0} g");
    }
}
