using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Anguloso.Server.Logica;

public class OpenFoodFactsService
{
    private readonly HttpClient _http;
    private readonly string _connectionString;
    //private readonly string _usdaKey;
    private readonly LogServ _logServ;
    private readonly ConfigServ _configServ;

    public OpenFoodFactsService(HttpClient http, string connectionString, /*string usdaKey,*/ LogServ logServ, ConfigServ configServ)
    {
        _http = http;
        _connectionString = connectionString;
        //_usdaKey = usdaKey;
        _logServ = logServ;
        _configServ = configServ;
        _http.BaseAddress = new Uri("https://world.openfoodfacts.org/");
    }

    private angulosodbContext CrearDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<angulosodbContext>();
        optionsBuilder.UseNpgsql(_connectionString);
        return new angulosodbContext(optionsBuilder.Options);
    }

    public async Task<OffProduct?> GetProductByBarcodeAsync(string barcode)
    {
        var response = await _http.GetAsync($"api/v3/product/{barcode}");

        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<OffProductResponse>();

        return data?.Product;
    }

    /*
     * Proporciona una lista de alimentos
     * - Busca en nuestra BBDD:
     *   - Si encuentra resultados los devuelve
     *   - Si no encuentra suficientes o ninguno: Llama a SearchProductsAsync, que busca los datos en OFF y USDA
     */
    public async Task<List<OffProduct>> SearchAsync(string term, string? userName = null)
    {
        try
        {
            using var dbContext = CrearDbContext();
            string pais = "spain";
            string lang = "es";

            if (userName != null)
            {
                users? user = null;
                try
                {
                    user = dbContext.users.AsNoTracking().Where(u => u.username == userName).FirstOrDefault();
                }
                catch (Exception) { }

                //pais = user != null ? user.country : "spain";
                //lang = user != null ? user.lang : "es";

                //TODO Pensar si, en lugar de dar valor por defecto, no sería mejor mostrar una advertencia en pantalla al usuario para que configure sus locales
                pais = string.IsNullOrWhiteSpace(user?.country)
                    ? "spain"
                    : user.country;

                lang = string.IsNullOrWhiteSpace(user?.lang)
                    ? "es"
                    : user.lang;
            }

            // 1. Buscar en la base de datos
            var localResults = await dbContext.foods.Where(f => EF.Functions.ILike(f.name, $"%{term}%")).ToListAsync();

            // 2. Si hay más de 5 resultados, devuelve la lista
            //if (localResults.Any())
            if (localResults.Count > 5)
                return ListaProductos(localResults);

            // 3. Buscar fuera si no hay resultados
            var offResults = await SearchProductsAsync(term, pais, lang);

            //Si queremos devolver la consulta más los pocos guardados debemos usar estas lineas
            localResults = await dbContext.foods.Where(f => EF.Functions.ILike(f.name, $"%{term}%")).ToListAsync();
            return ListaProductos(localResults);

            //Si solo queremos devolver la consulta debemos usar esta línea
            //return offResults;
        }
        catch(Exception ex)
        {
            _logServ.LogError($"Excepción en OpenFoodFactsService.SearchAsync() => {ex.Message}");
            return new List<OffProduct>();
        }
    }

    /*
     * Busca productos en OpenFoodFacts
     * Si los productos no tienen información de macros se buscan en la USDA (US Department of Agriculture)
     */
    public async Task<List<OffProduct>> SearchProductsAsync(string query, string pais = "spain", string lang = "es")
    {
        var products = new List<OffProduct>();
        //_logServ.LogInfo($"query='{query}', pais='{pais}', lang='{lang}'");

        try
        {
            string fields = "product_name,brands,nutriscore_grade,nutriments,code,categories,serving_size";
            int nResultados = 50;
            // Construir URL (cgi/search.pl es la recomendada para json)
            var url = $"cgi/search.pl?search_terms={Uri.EscapeDataString(query)}&tagtype_0=countries&tag_contains_0=contains&tag_0={Uri.EscapeDataString(pais)}&tagtype_1=languages&tag_contains_1=contains&tag_1={Uri.EscapeDataString(lang)}&fields={Uri.EscapeDataString(fields)}&page_size={nResultados}&json=1";
            var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            
            if (!doc.RootElement.TryGetProperty("products", out var productsJson))
                return products;

            foreach (var p in productsJson.EnumerateArray())
            {
                string servingSizeText = GetString(p, "serving_size");
                ServingSizeObject? servingSizeObject = ExtractServingSizeGrams(servingSizeText);

                var off = new OffProduct
                {
                    Code = GetString(p, "code"),
                    Product_name = GetString(p, "product_name"),
                    Brands = GetString(p, "brands"),
                    Category = GetString(p, "categories"),
                    Nutriscore_grade = GetString(p, "nutriscore_grade"),
                    ServingSizeText = servingSizeText,
                    ServingSize = servingSizeObject != null ? servingSizeObject.Value : null,
                    ServingSizeUnit = servingSizeObject != null ? servingSizeObject.Unit : null,
                    Nutriments = ExtractNutrimentsFromOffElement(p),
                    //Micronutrients = ExtractMicronutrientsFromOffElement(p) //Los sacamos de la USDA directamente
                    Micronutrients = new OffMicronutrients()
                };

                bool needMacros = !HasEssentialNutrients(off.Nutriments);
                bool needServing = off.ServingSize == null || string.IsNullOrEmpty(off.ServingSizeUnit);
                //bool needMicros = !HasAnyMicronutrients(off.Micronutrients);
                bool needMicros = AreAllMicronutrientsEmpty(off.Micronutrients);


                // Si nos faltan macros esenciales → fallback a USDA
                //if (!HasEssentialNutrients(off.Nutriments) || string.IsNullOrEmpty(off.ServingSizeText) || off.ServingSize == null || string.IsNullOrEmpty(off.ServingSizeUnit))
                //Si fallan macros, micros o serving size
                if (needMacros || needServing || needMicros)
                {
                    _logServ.LogInfo($"USDA fallback for '{off.Product_name}' | macros:{needMacros} micros:{needMicros}");

                    var (fallbackNutrients, fallbackMicros, fallbackServing, servingSizeTextUSDA, foodCategory) = await GetNutrientsFromUsdaAsync(off.Product_name);

                    if (fallbackMicros != null)
                    {
                        MergeMicronutrients(off.Micronutrients, fallbackMicros);
                    }
                    if (fallbackNutrients != null)
                    {
                        MergeNutriments(off.Nutriments, fallbackNutrients);
                    }
                    if (fallbackServing != null)
                    {
                        off.ServingSize = fallbackServing.Value;
                        off.ServingSizeUnit = fallbackServing.Unit;
                        //off.ServingSizeText = $"{fallbackServing.Value} {fallbackServing.Unit}";
                        off.ServingSizeText = servingSizeTextUSDA ?? $"{fallbackServing.Value} {fallbackServing.Unit}";
                    }
                    if(!string.IsNullOrEmpty(foodCategory))
                    {
                        off.Category = foodCategory;
                    }
                }

                //Comprueba si el producto está en la BBDD. Si no está -> lo guarda
                await SaveFoodToDb(off);

                products.Add(off);
            }

            return products;
        }
        catch (Exception ex)
        {
            _logServ.LogError($"Excepción en OpenFoodFactsService.SearchProductsAsync() => {ex.Message}");
            return products;
        }
    }

    /*
     * Busca macros y micros en la USDA
     */
    private async Task<(OffNutriments? Nutriments, OffMicronutrients? Micronutrients, ServingSizeObject? ServingSize, string? ServingText, string? Category)> GetNutrientsFromUsdaAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return (null, null, null, null, null);

        //var requestUrl = $"https://api.nal.usda.gov/fdc/v1/foods/search?api_key={Uri.EscapeDataString(_usdaKey)}";

        string? usdKey = _configServ.GetConfigString("usdaKey");
        if (string.IsNullOrWhiteSpace(usdKey))
        {
            _logServ.LogError("USDA API key is not configured.");
            return (null, null, null, null, null);
        }

        var requestUrl = $"https://api.nal.usda.gov/fdc/v1/foods/search?api_key={Uri.EscapeDataString(usdKey)}";
        var bodyObj = new { generalSearchInput = query, pageSize = 5 }; // varios resultados para fallback

        var content = new StringContent(JsonSerializer.Serialize(bodyObj));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var resp = await _http.PostAsync(requestUrl, content);
        if (!resp.IsSuccessStatusCode) return (null, null, null, null, null);

        var text = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);

        if (!doc.RootElement.TryGetProperty("foods", out var foods) || foods.GetArrayLength() == 0)
            return (null, null, null, null, null);

        var nutr = new OffNutriments();
        var micros = new OffMicronutrients();
        ServingSizeObject? serving = null;
        string? servingText = null;
        string? foodCategory = null;

        // Diccionarios para mapear nutrientId a propiedad
        var macroMap = new Dictionary<int, Action<double>>
        {
            {1008, v => nutr.EnergyKcal100g ??= v},
            {1003, v => nutr.Proteins100g ??= v},
            {1004, v => nutr.Fat100g ??= v},
            {1005, v => nutr.Carbohydrates100g ??= v},
            {2000, v => nutr.Sugars100g ??= v},
            {1079, v => nutr.Fiber100g ??= v},
            {1258, v => nutr.SaturatedFat100g ??= v},
            {1093, v => nutr.Salt100g ??= v * 0.00254}
        };

        var microMap = new Dictionary<int, Action<double>>
        {
            {1106, v => micros.VitaminA_ug ??= v},
            {1162, v => micros.VitaminC_mg ??= v},
            {1114, v => micros.VitaminD_ug ??= v},
            {1109, v => micros.VitaminE_mg ??= v},
            {1178, v => micros.VitaminB12_ug ??= v},
            {1177, v => micros.Folate_ug ??= v},
            {1087, v => micros.Calcium_mg ??= v},
            {1089, v => micros.Iron_mg ??= v},
            {1090, v => micros.Magnesium_mg ??= v},
            {1092, v => micros.Potassium_mg ??= v},
            {1095, v => micros.Zinc_mg ??= v}
        };

        var folateIds = new[] { 1177, 1186, 1187, 1190 };

        foreach (var first in foods.EnumerateArray())
        {
            // Serving size y category: tomamos el primero válido
            if (serving == null)
            {
                if (first.TryGetProperty("servingSize", out var ss) && ss.ValueKind == JsonValueKind.Number)
                    serving = new ServingSizeObject { Value = ss.GetDouble(), Unit = "g" };
                else if (first.TryGetProperty("householdServingWeight", out var hw) && hw.ValueKind == JsonValueKind.Number)
                    serving = new ServingSizeObject { Value = hw.GetDouble(), Unit = "g" };

                if (first.TryGetProperty("householdServingFullText", out var ht) && ht.ValueKind == JsonValueKind.String)
                    servingText = ht.GetString();

                if (first.TryGetProperty("foodCategory", out var catEl) && catEl.ValueKind == JsonValueKind.String)
                    foodCategory = catEl.GetString();
            }

            if (first.TryGetProperty("foodNutrients", out var foodNutrients) && foodNutrients.ValueKind == JsonValueKind.Array)
            {
                foreach (var nut in foodNutrients.EnumerateArray())
                {
                    if (!nut.TryGetProperty("value", out var valEl) || valEl.ValueKind != JsonValueKind.Number) continue;

                    //Console.WriteLine($"{nut}"); //Chivato del JSON de USDA

                    double value = valEl.GetDouble();

                    int? nutrientId = nut.TryGetProperty("nutrientId", out var idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt32() : (int?)null;
                    string name = nut.TryGetProperty("nutrientName", out var nameEl) && nameEl.ValueKind == JsonValueKind.String ? nameEl.GetString()! : "";

                    // Intentamos mapear por nutrientId
                    if (nutrientId.HasValue)
                    {
                        if (macroMap.TryGetValue(nutrientId.Value, out var macroAction))
                            macroAction(value);

                        if (microMap.TryGetValue(nutrientId.Value, out var microAction))
                            microAction(value);

                        else if (folateIds.Contains(nutrientId.Value))
                            micros.Folate_ug = (micros.Folate_ug ?? 0) + value;
                    }

                    // Fallback: mapear por nombre si no está asignado
                    name = name.ToLowerInvariant();
                    if (!nutrientId.HasValue || !macroMap.ContainsKey(nutrientId.Value))
                    {
                        if (name.Contains("energy") || name.Contains("kcal")) nutr.EnergyKcal100g ??= value;
                        else if (name.Contains("protein")) nutr.Proteins100g ??= value;
                        else if (name.Contains("total lipid") || name.Contains("fat")) nutr.Fat100g ??= value;
                        else if (name.Contains("carbohydrate")) nutr.Carbohydrates100g ??= value;
                        else if (name.Contains("sugar")) nutr.Sugars100g ??= value;
                        else if (name.Contains("fiber")) nutr.Fiber100g ??= value;
                        else if (name.Contains("saturated")) nutr.SaturatedFat100g ??= value;
                        else if (name.Contains("sodium")) nutr.Salt100g ??= value * 0.00254;

                        if (name.Contains("vitamin a")) micros.VitaminA_ug ??= value;
                        else if (name.Contains("vitamin c")) micros.VitaminC_mg ??= value;
                        else if (name.Contains("vitamin d")) micros.VitaminD_ug ??= value;
                        else if (name.Contains("vitamin e")) micros.VitaminE_mg ??= value;
                        else if (name.Contains("vitamin b-12") || name.Contains("cobalamin")) micros.VitaminB12_ug ??= value;
                        //else if (name.Contains("folate")) micros.Folate_ug ??= value;
                        else if (name.Contains("folate")) micros.Folate_ug = (micros.Folate_ug ?? 0) + value;
                        else if (name.Contains("calcium")) micros.Calcium_mg ??= value;
                        else if (name.Contains("iron")) micros.Iron_mg ??= value;
                        else if (name.Contains("magnesium")) micros.Magnesium_mg ??= value;
                        else if (name.Contains("potassium")) micros.Potassium_mg ??= value;
                        else if (name.Contains("zinc")) micros.Zinc_mg ??= value;
                    }
                }
            }
        }

        return (nutr, micros, serving, servingText, foodCategory);
    }



    private OffNutriments ExtractNutrimentsFromOffElement(JsonElement p)
    {
        var n = new OffNutriments();

        if (p.TryGetProperty("nutriments", out var nutrEl) && nutrEl.ValueKind == JsonValueKind.Object)
        {
            // keys in OFF have dashes, e.g. "energy-kcal_100g", "fat_100g", "carbohydrates_100g", "proteins_100g"
            n.EnergyKcal100g = TryGetDouble(nutrEl, "energy-kcal_100g") ?? TryGetDouble(nutrEl, "energy_100g") ?? TryGetDouble(nutrEl, "energy-kcal") ?? null;
            n.Fat100g = TryGetDouble(nutrEl, "fat_100g") ?? TryGetDouble(nutrEl, "fat") ?? null;
            n.SaturatedFat100g = TryGetDouble(nutrEl, "saturated-fat_100g") ?? TryGetDouble(nutrEl, "saturated-fat") ?? TryGetDouble(nutrEl, "saturated_fat_100g");
            n.Carbohydrates100g = TryGetDouble(nutrEl, "carbohydrates_100g") ?? TryGetDouble(nutrEl, "carbohydrates") ?? TryGetDouble(nutrEl, "carbohydrate_100g");
            n.Sugars100g = TryGetDouble(nutrEl, "sugars_100g") ?? TryGetDouble(nutrEl, "sugars");
            n.Fiber100g = TryGetDouble(nutrEl, "fiber_100g") ?? TryGetDouble(nutrEl, "fiber");
            n.Proteins100g = TryGetDouble(nutrEl, "proteins_100g") ?? TryGetDouble(nutrEl, "proteins");
            n.Salt100g = TryGetDouble(nutrEl, "salt_100g") ?? TryGetDouble(nutrEl, "salt");
        }

        return n;
    }

    private OffMicronutrients ExtractMicronutrientsFromOffElement(JsonElement p)
    {
        var m = new OffMicronutrients();

        if (p.TryGetProperty("nutriments", out var nutrEl))
        {
            m.VitaminC_mg = TryGetDouble(nutrEl, "vitamin-c_100g");
            m.VitaminA_ug = TryGetDouble(nutrEl, "vitamin-a_100g");
            m.VitaminD_ug = TryGetDouble(nutrEl, "vitamin-d_100g");
            m.VitaminE_mg = TryGetDouble(nutrEl, "vitamin-e_100g");
            m.VitaminB12_ug = TryGetDouble(nutrEl, "vitamin-b12_100g");
            m.Folate_ug = TryGetDouble(nutrEl, "folate_100g");

            m.Calcium_mg = TryGetDouble(nutrEl, "calcium_100g");
            m.Iron_mg = TryGetDouble(nutrEl, "iron_100g");
            m.Magnesium_mg = TryGetDouble(nutrEl, "magnesium_100g");
            m.Potassium_mg = TryGetDouble(nutrEl, "potassium_100g");
            m.Zinc_mg = TryGetDouble(nutrEl, "zinc_100g");
        }

        return m;
    }

    private bool HasAnyMicronutrients(OffMicronutrients? m)
    {
        if (m == null) return false;

        return
            m.VitaminA_ug.HasValue ||
            m.VitaminC_mg.HasValue ||
            m.VitaminD_ug.HasValue ||
            m.VitaminE_mg.HasValue ||
            m.VitaminB12_ug.HasValue ||
            m.Folate_ug.HasValue ||
            m.Calcium_mg.HasValue ||
            m.Iron_mg.HasValue ||
            m.Magnesium_mg.HasValue ||
            m.Potassium_mg.HasValue ||
            m.Zinc_mg.HasValue;
    }

    private bool AreAllMicronutrientsEmpty(OffMicronutrients? m)
    {
        if (m == null) return true;

        return
            m.VitaminA_ug == null &&
            m.VitaminC_mg == null &&
            m.VitaminD_ug == null &&
            m.VitaminE_mg == null &&
            m.VitaminB12_ug == null &&
            m.Folate_ug == null &&
            m.Calcium_mg == null &&
            m.Iron_mg == null &&
            m.Magnesium_mg == null &&
            m.Potassium_mg == null &&
            m.Zinc_mg == null;
    }

    private static double? TryGetDouble(JsonElement nutrEl, string key)
    {
        if (nutrEl.TryGetProperty(key, out var prop) && prop.ValueKind != JsonValueKind.Null)
        {
            // puede ser número o string con coma
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var d))
                return d;
            if (prop.ValueKind == JsonValueKind.String)
            {
                var s = prop.GetString();
                if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d2))
                    return d2;
                // intentar con coma decimal local
                s = s?.Replace(',', '.');
                if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d3))
                    return d3;
            }
        }
        return null;
    }

    private static string GetString(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null)
            return v.GetString() ?? "";
        return "";
    }

    private static bool HasEssentialNutrients(OffNutriments n)
    {
        // Consideramos esenciales: kcal y proteínas y carbohidratos y grasas
        return n.EnergyKcal100g.HasValue && n.Proteins100g.HasValue && n.Carbohydrates100g.HasValue && n.Fat100g.HasValue;
    }

    private static void MergeNutriments(OffNutriments target, OffNutriments source)
    {
        if (target.EnergyKcal100g == null) target.EnergyKcal100g = source.EnergyKcal100g;
        if (target.Fat100g == null) target.Fat100g = source.Fat100g;
        if (target.SaturatedFat100g == null) target.SaturatedFat100g = source.SaturatedFat100g;
        if (target.Carbohydrates100g == null) target.Carbohydrates100g = source.Carbohydrates100g;
        if (target.Sugars100g == null) target.Sugars100g = source.Sugars100g;
        if (target.Fiber100g == null) target.Fiber100g = source.Fiber100g;
        if (target.Proteins100g == null) target.Proteins100g = source.Proteins100g;
        if (target.Salt100g == null) target.Salt100g = source.Salt100g;
    }

    private static void MergeMicronutrients(OffMicronutrients target, OffMicronutrients source)
    {
        if (target.VitaminC_mg == null) target.VitaminC_mg = source.VitaminC_mg;
        if (target.VitaminA_ug == null) target.VitaminA_ug = source.VitaminA_ug;
        if (target.VitaminD_ug == null) target.VitaminD_ug = source.VitaminD_ug;
        if (target.VitaminE_mg == null) target.VitaminE_mg = source.VitaminE_mg;
        if (target.VitaminB12_ug == null) target.VitaminB12_ug = source.VitaminB12_ug;

        if (target.Calcium_mg == null) target.Calcium_mg = source.Calcium_mg;
        if (target.Iron_mg == null) target.Iron_mg = source.Iron_mg;
        if (target.Magnesium_mg == null) target.Magnesium_mg = source.Magnesium_mg;
        if (target.Potassium_mg == null) target.Potassium_mg = source.Potassium_mg;
        if (target.Zinc_mg == null) target.Zinc_mg = source.Zinc_mg;
        if(target.Folate_ug == null) target.Folate_ug = source.Folate_ug;
    }


    /*
     * Comprueba si un producto está en la BBDD
     *  - Si está lo ignora
     *  - Si no está lo guarda
     */
    private async Task SaveFoodToDb(OffProduct product)
    {
        try
        {
            using var dbContext = CrearDbContext();

            var existing = await dbContext.foods.FirstOrDefaultAsync(f => f.external_id == product.Code);

            bool isNew = existing == null;

            var food = existing ?? new foods
            {
                external_id = product.Code,
                created_at = DateTime.UtcNow
            };

            // =========================
            // Datos básicos
            // =========================
            food.name = product.Product_name;
            food.brands = product.Brands;
            food.category = product.Category;
            food.nutriscore = product.Nutriscore_grade;

            // =========================
            // Serving size
            // =========================
            food.serving_size = product.ServingSize ?? food.serving_size;
            food.serving_size_unit = product.ServingSizeUnit ?? food.serving_size_unit;
            food.serving_size_text = product.ServingSizeText ?? food.serving_size_text;

            // =========================
            // MACROS (solo si existen)
            // =========================
            var n = product.Nutriments;
            if (n != null)
            {
                food.kcal = n.EnergyKcal100g ?? food.kcal;
                food.fat = n.Fat100g ?? food.fat;
                food.carbs = n.Carbohydrates100g ?? food.carbs;
                food.protein = n.Proteins100g ?? food.protein;
                food.fiber = n.Fiber100g ?? food.fiber;
                food.sugar = n.Sugars100g ?? food.sugar;
                food.saturated_fat = n.SaturatedFat100g ?? food.saturated_fat;
                food.salt = n.Salt100g ?? food.salt;
            }

            // =========================
            // MICROS
            // =========================
            var m = product.Micronutrients;
            if (m != null)
            {
                food.vitamin_a_ug = m.VitaminA_ug ?? food.vitamin_a_ug;
                food.vitamin_c_mg = m.VitaminC_mg ?? food.vitamin_c_mg;
                food.vitamin_d_ug = m.VitaminD_ug ?? food.vitamin_d_ug;
                food.vitamin_e_mg = m.VitaminE_mg ?? food.vitamin_e_mg;
                food.vitamin_b12_ug = m.VitaminB12_ug ?? food.vitamin_b12_ug;
                food.folate_ug = m.Folate_ug ?? food.folate_ug;

                food.calcium_mg = m.Calcium_mg ?? food.calcium_mg;
                food.iron_mg = m.Iron_mg ?? food.iron_mg;
                food.magnesium_mg = m.Magnesium_mg ?? food.magnesium_mg;
                food.potassium_mg = m.Potassium_mg ?? food.potassium_mg;
                food.zinc_mg = m.Zinc_mg ?? food.zinc_mg;
            }

            // =========================
            // Origen / sincronización
            // =========================
            food.source = food.source ?? "openfoodfacts";
            food.last_synced_at = DateTime.UtcNow;

            if (isNew)
                dbContext.foods.Add(food);

            await dbContext.SaveChangesAsync();
            product.Id = food.id;
        }
        catch (Exception ex)
        {
            _logServ.LogError(
                $"Excepción en OpenFoodFactsService.SaveFoodToDb() => {ex.Message}"
            );
        }
    }


    public static List<OffProduct> ListaProductos(List<foods> listaFoods)
    {
        List<OffProduct> listaProductos = new();

        try
        {
            foreach (foods food in listaFoods)
            {
                listaProductos.Add(new OffProduct(food));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }

        return listaProductos;
    }

    //Intenta extraer datos de serving size de ServingSizeText de OFF
    private ServingSizeObject? ExtractServingSizeGrams(string? serving)
    {
        if (string.IsNullOrWhiteSpace(serving)) return null;

        var match = Regex.Match(serving, @"(\d+(?:\.\d+)?)\s*(g|ml)");
        if (!match.Success) return null;

        return new ServingSizeObject
        {
            Value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            Unit = match.Groups[2].Value
        };
    }
}

public class ServingSizeObject
{
    public double? Value { get; set; }
    public string? Unit { get; set; }
}
