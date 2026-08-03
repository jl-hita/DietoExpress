using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace Anguloso.Server.Logica;

public class BEDCAClient
{
    private readonly HttpClient _http;
    private readonly LogServ _logServ;
    //private readonly DbContext _dbContext;
    private readonly angulosodbContext _dbContext;

    public BEDCAClient(HttpClient http, LogServ logServ, angulosodbContext dbContext)
    {
        _http = http;
        _logServ = logServ;
        _dbContext = dbContext;
    }

    //Para usar una vez, cuando se instala la APP
    public async Task<string> Importador()
    {
        int i = 0;
        int nGuardados = 0;
        //int nRegistros = 0;

        var gruposXml = await GetFoodGroups();
        var grupos = ParseFoodGroups(gruposXml);

        var existingIds = _dbContext.foods.Select(f => f.external_id).ToHashSet();

        foreach (var grupo in grupos)
        {
            var alimentosXml = await GetFoodsInGroup(grupo.Id);
            var alimentos = ParseFoods(alimentosXml, grupo.Id);

            foreach (var food in alimentos)
            {
                var detailsXml = await GetFoodDetails(food.Id);
                var (parsedFood, compositions) = ParseFoodDetails(detailsXml);

                // Mapear a objeto foods de la BD local
                var dbFood = MapToDbFood(parsedFood, compositions);
                _logServ.LogInfo($"[BEDCA] #{i++}: {dbFood.name} (id: {dbFood.external_id}) => {dbFood.kcal} kcal, protes:{dbFood.protein}g, carbs:{dbFood.carbs}g, grasa:{dbFood.fat}g, fibra: {dbFood.fiber}g, potasio: {dbFood.potassium_mg}mg");

                if (string.IsNullOrEmpty(dbFood.name))
                    continue;

                var exists = existingIds.Contains(dbFood.external_id);

                if (!exists)
                {
                    _dbContext.foods.Add(dbFood);
                    try
                    {
                        nGuardados += await _dbContext.SaveChangesAsync();

                    }
                    catch (Exception e)
                    {
                        _logServ.LogError($"Error al guardar alimentos de BEDCA: {e.Message}");
                    }
                }
                else
                {
                    _logServ.LogError($"El alimento {dbFood.name} ya existe en la BBDD");
                }

                await Task.Delay(200); // importante para no saturar BEDCA
            }
            await Task.Delay(500);
        }

        //if (nGuardados > 0)
        //{
        //    try
        //    {
        //        nRegistros = await _dbContext.SaveChangesAsync();
        //    }
        //    catch(Exception e)
        //    {
        //        _logServ.LogError($"Error al guardar alimentos de BEDCA: {e.Message}");
        //    }
        //}
            

        //BoolMensaje bmGuardar = await SaveToDBAsync(foods);
        //if(bmGuardar.Exito)
        //{
        //    _logServ.LogInfo($"[BEDCA] Guardado en BD: {bmGuardar.Mensaje}");
        //}
        //else
        //{
        //    _logServ.LogError($"[BEDCA] Error al guardar en BD: {bmGuardar.Mensaje}");
        //}

        return $"Importación finalizada: {i} alimentos procesados, {nGuardados} guardados";
    }

    public async Task<string> ExecuteQuery(string xml)
    {
        using var content = new StringContent(xml, Encoding.UTF8, "text/xml");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");

        var response = await _http.PostAsync("https://www.bedca.net/bdpub/procquery.php", content);

        var responseText = await response.Content.ReadAsStringAsync();

        //Console.WriteLine($"Status: {response.StatusCode}");
        //Console.WriteLine("HTTP: " + (int)response.StatusCode);
        //Console.WriteLine("BODY LENGTH: " + responseText?.Length);
        //Console.WriteLine($"Motivo: {response.ReasonPhrase}");
        //Console.WriteLine($"Respuesta: {responseText}");
        response.EnsureSuccessStatusCode();

        //return await response.Content.ReadAsStringAsync();
        //string respuestaCompleta = $"Status: {(int)response.StatusCode} Reason: {response.ReasonPhrase} {responseText}";
        //return respuestaCompleta;

        if (!response.IsSuccessStatusCode)
            throw new Exception($"BEDCA error HTTP {(int)response.StatusCode}");

        if (string.IsNullOrWhiteSpace(responseText))
            throw new Exception("BEDCA devolvió respuesta vacía");

        return responseText;
    }

    //===================
    // Consultas a BEDCA 
    //===================

    //Obtener grupos alimentarios
    public async Task<string> GetFoodGroups(/*string foodCode*/)
    {
        string xmlRequest =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<foodquery>" +
                "<type level=\"3\"/>" +
                "<selection>" +
                    "<atribute name=\"fg_id\"/>" +
                    "<atribute name=\"fg_ori_name\"/>" +
                    "<atribute name=\"fg_eng_name\"/>" +
                "</selection>" +
            "</foodquery>";
        return await ExecuteQuery(xmlRequest);
    }

    //Obtener todos los alimentos de un grupo
    public async Task<string> GetFoodsInGroup(int groupId)
    {
        string xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <foodquery>
                <type level="1"/>
                <selection>
                    <atribute name="f_id"/>
                    <atribute name="f_ori_name"/>
                    <atribute name="langual"/>
                    <atribute name="f_eng_name"/>
                    <atribute name="f_origen"/>
                </selection>

                <condition>
                    <cond1>
                        <atribute1 name="foodgroup_id"/>
                    </cond1>
                    <relation type="EQUAL"/>
                    <cond3>{groupId}</cond3>
                </condition>

                <condition>
                    <cond1>
                        <atribute1 name="f_origen"/>
                    </cond1>
                    <relation type="EQUAL"/>
                    <cond3>BEDCA</cond3>
                </condition>
            </foodquery>
            """;

        return await ExecuteQuery(xml);
    }

    //Obtener todos los nutrientes de un alimento
    //public async Task<string> GetFoodDetails(int foodId)
    //{
    //    string xml = $"""
    //        <?xml version="1.0" encoding="utf-8"?>
    //        <foodquery>
    //            <type level="2"/>

    //            <selection>
    //                <atribute name="f_id"/>
    //                <atribute name="f_ori_name"/>
    //                <atribute name="f_eng_name"/>

    //                <atribute name="c_id"/>
    //                <atribute name="c_ori_name"/>
    //                <atribute name="c_eng_name"/>

    //                <atribute name="componentgroup_id"/>
    //                <atribute name="v_unit"/>
    //                <atribute name="moex"/>
    //            </selection>

    //            <condition>
    //                <cond1>
    //                    <atribute1 name="f_id"/>
    //                </cond1>
    //                <relation type="EQUAL"/>
    //                <cond3>{foodId}</cond3>
    //            </condition>

    //            <condition>
    //                <cond1>
    //                    <atribute1 name="publico"/>
    //                </cond1>
    //                <relation type="EQUAL"/>
    //                <cond3>1</cond3>
    //            </condition>
    //        </foodquery>
    //        """;

    //    return await ExecuteQuery(xml);
    //}
    public async Task<string> GetFoodDetails(int foodId)
    {
        string xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <foodquery>
                <type level="2"/>

                <selection>
                    <atribute name="f_id"/>
                    <atribute name="f_ori_name"/>
                    <atribute name="f_eng_name"/>
                    <atribute name="sci_name"/>
                    <atribute name="langual"/>
                    <atribute name="foodexcode"/>
                    <atribute name="edible_portion"/>
                    <atribute name="f_origen"/>
                    <atribute name="c_id"/>
                    <atribute name="c_ori_name"/>
                    <atribute name="c_eng_name"/>
                    <atribute name="eur_name"/>
                    <atribute name="componentgroup_id"/>
                    <atribute name="best_location"/>
                    <atribute name="v_unit"/>
                    <atribute name="moex"/>
                </selection>

                <condition>
                    <cond1>
                        <atribute1 name="f_id"/>
                    </cond1>
                    <relation type="EQUAL"/>
                    <cond3>{foodId}</cond3>
                </condition>

                <condition>
                    <cond1>
                        <atribute1 name="publico"/>
                    </cond1>
                    <relation type="EQUAL"/>
                    <cond3>1</cond3>
                </condition>
            </foodquery>
            """;

        return await ExecuteQuery(xml);
    }

    //==============================
    // Helpers parseo XML -> objeto
    //==============================
    public List<FoodGroup> ParseFoodGroups(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return new List<FoodGroup>();

        var doc = XDocument.Parse(xml);

        return doc
            //.Descendants("food")
            .Descendants().Where(x => x.Name.LocalName == "food")
            .Select(food => new FoodGroup
            {
                Id = (int?)food.Element("fg_id") ?? 0,
                Name = (string?)food.Element("fg_ori_name"),
                EnglishName = (string?)food.Element("fg_eng_name")
            })
            .ToList();
    }

    public List<Food> ParseFoods(string xml, int groupId)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return new List<Food>();

        var doc = XDocument.Parse(xml);

        return doc.Descendants("food")
        .Select(x => new Food
        {
            Id = (int?)x.Element("f_id") ?? 0,
            OriginalName = (string?)x.Element("f_ori_name"),
            EnglishName = (string?)x.Element("f_eng_name"),
            Origin = (string?)x.Element("f_origen")
        })
        .ToList();
    }

    /// <summary>
    /// Parsea el XML de detalle de un alimento BEDCA y extrae todos los nutrientes.
    /// Los valores con moex="WKG" (por kg) se dividen entre 10 para obtener "por 100g",
    /// que es la convención usada en nuestra tabla foods.
    /// </summary>
    public (Food Food, List<FoodComposition> Compositions) ParseFoodDetails(string xml)
    {
        var doc = XDocument.Parse(xml);
        var foodElement = doc.Descendants("food").FirstOrDefault();

        if (foodElement == null)
            return (new Food(), new List<FoodComposition>());

        var food = new Food
        {
            Id = (int?)foodElement.Element("f_id") ?? 0,
            OriginalName = (string?)foodElement.Element("f_ori_name"),
            EnglishName = (string?)foodElement.Element("f_eng_name"),
            Origin = (string?)foodElement.Element("f_origen"),
            EdiblePortion = TryParseDouble((string?)foodElement.Element("edible_portion"))
        };

        var compositions = new List<FoodComposition>();

        foreach (var fv in foodElement.Elements("foodvalue"))
        {
            string? moex = (string?)fv.Element("moex");
            string? eurName = (string?)fv.Element("eur_name");
            double? rawValue = TryParseDouble((string?)fv.Element("best_location"));

            // Los valores con moex="WKG" vienen expresados por kg.
            // Dividimos entre 10 para obtener el valor por 100g.
            double? valuePer100g = rawValue;
            if (rawValue.HasValue && moex == "WKG")
            {
                valuePer100g = rawValue.Value / 10.0;
            }

            var composition = new FoodComposition
            {
                FoodId = food.Id,
                NutrientId = (int?)fv.Element("c_id") ?? 0,
                NutrientName = (string?)fv.Element("c_ori_name"),
                EurName = eurName,
                Value = valuePer100g,
                Unit = (string?)fv.Element("v_unit"),
                BestLocation = (string?)fv.Element("best_location")
            };

            compositions.Add(composition);
        }

        return (food, compositions);
    }

    /// <summary>
    /// Mapea una lista de FoodComposition (nutrientes BEDCA) a un objeto foods de la BD local.
    /// Utiliza el código europeo (eur_name) para asignar cada nutriente al campo correcto.
    /// </summary>
    public foods MapToDbFood(Food bedcaFood, List<FoodComposition> compositions)
    {
        var dbFood = new foods
        {
            name = bedcaFood.OriginalName ?? "",
            source = "bedca",
            external_id = $"bedca_{bedcaFood.Id}",
            default_grams = 100,
            created_at = DateTime.UtcNow,
            last_synced_at = DateTime.UtcNow
        };

        foreach (var c in compositions)
        {
            switch (c.EurName)
            {
                // Proximales
                case "ENERC":
                    // BEDCA devuelve energía en kJ → convertimos a kcal (1 kJ = 0.239006 kcal)
                    if (c.Unit == "kJ" && c.Value.HasValue)
                        dbFood.kcal = Math.Round(c.Value.Value * 0.239006, 1);
                    else
                        dbFood.kcal = c.Value;
                    break;
                case "PROT": dbFood.protein = c.Value; break;
                case "FAT": dbFood.fat = c.Value; break;
                case "CHO": dbFood.carbs = c.Value; break;
                case "FIBT": dbFood.fiber = c.Value; break;

                // Grasas detalladas
                case "CHORL": break; // colesterol - no tenemos campo aún

                // Vitaminas
                case "VITA": dbFood.vitamin_a_ug = c.Value; break;
                case "VITD": dbFood.vitamin_d_ug = c.Value; break;
                case "VITE": dbFood.vitamin_e_mg = c.Value; break;
                case "VITC": dbFood.vitamin_c_mg = c.Value; break;
                case "VITB12": dbFood.vitamin_b12_ug = c.Value; break;
                case "FOL": dbFood.folate_ug = c.Value; break;

                // Minerales (ya convertidos a por 100g en ParseFoodDetails)
                case "CA": dbFood.calcium_mg = c.Value; break;
                case "FE": dbFood.iron_mg = c.Value; break;
                case "K": dbFood.potassium_mg = c.Value; break;
                case "MG": dbFood.magnesium_mg = c.Value; break;
                case "ZN": dbFood.zinc_mg = c.Value; break;
                case "NA":
                    // sodio → sal (salt = sodium_mg * 2.5 / 1000)
                    dbFood.salt = c.Value.HasValue
                        ? Math.Round(c.Value.Value * 2.5 / 1000.0, 2)
                        : 0;
                    //if (c.Value.HasValue)
                    //    dbFood.salt = Math.Round(c.Value.Value * 2.5 / 1000.0, 2);
                    break;
            }
        }

        foods? foodCompleta = GramCalculatorHelper(dbFood);
        return foodCompleta ?? dbFood;
    }

    //Si a un alimento le falta solo un macro se calcula a partir de kcal y gramos de los otros macros
    private foods? GramCalculatorHelper(foods food)
    {
        try
        {
            int missingMacros = 0;

            if (!food.protein.HasValue) missingMacros++;
            if (!food.fat.HasValue) missingMacros++;
            if (!food.carbs.HasValue) missingMacros++;

            if (missingMacros == 1 && food.kcal.HasValue)
            {
                double kcal = food.kcal.Value;

                if (!food.protein.HasValue)
                {
                    double protein =
                        (kcal
                         - (food.fat ?? 0) * 9
                         - (food.carbs ?? 0) * 4)
                        / 4;

                    food.protein =
                        protein < 0
                            ? (protein > -1 ? 0 : null)
                            : Math.Round(protein, 2);
                }
                else if (!food.fat.HasValue)
                {
                    double fat =
                        (kcal
                         - (food.protein ?? 0) * 4
                         - (food.carbs ?? 0) * 4)
                        / 9;

                    food.fat =
                        fat < 0
                            ? (fat > -1 ? 0 : null)
                            : Math.Round(fat, 2);
                }
                else if (!food.carbs.HasValue)
                {
                    double carbs =
                        (kcal
                         - (food.protein ?? 0) * 4
                         - (food.fat ?? 0) * 9)
                        / 4;

                    food.carbs =
                        carbs < 0
                            ? (carbs > -1 ? 0 : null)
                            : Math.Round(carbs, 2);
                }

                return food;
            }
            else
                return null;
        }
        catch(Exception e)
        {
            _logServ.LogError($"Error calculando macros no disponibles para {food.name}: {e.Message}");
            return null;
        }
    }

    private double? TryParseDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return double.TryParse(
            value,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var result)
            ? result
            : (double?)null;
    }

    private async Task<BoolMensaje> SaveToDBAsync(List<foods> foods)
    {
        try
        {
            var gruposXml = await GetFoodGroups();
            var grupos = ParseFoodGroups(gruposXml);
            bool nuevosElementos = false;

            foreach (foods item in foods)
            {
                //var existe = await _dbContext.foods.Where(f => f.name == item.name || f.external_id == item.external_id).FirstOrDefaultAsync();
                //if(existe == null)
                //{
                //    _dbContext.Add(item);
                //    nuevosElementos = true;
                //}

                var exists = await _dbContext.foods.AnyAsync(f => f.name == item.name || f.external_id == item.external_id);

                if (!exists)
                {
                    _dbContext.foods.Add(item);
                    nuevosElementos = true;
                }
            }

            if(nuevosElementos)
                await _dbContext.SaveChangesAsync();
            return new BoolMensaje { Exito = true, Mensaje = "Alimentos guardados correctamente." };
        }
        catch (Exception ex)
        {
            _logServ.LogError($"Error al guardar alimentos en la BD: {ex.Message}");
            return new BoolMensaje { Exito = false, Mensaje = $"Error al guardar alimentos en la BD: {ex.Message}" };
        }
    }
}

public class FoodGroup
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? EnglishName { get; set; }
}

//public class Food
//{
//    public int Id { get; set; }
//    //public int GroupId { get; set; }

//    public string? Name { get; set; }
//    public string? EnglishName { get; set; }

//    public string? Langual { get; set; }
//    public string? Origin { get; set; }

//    public override string ToString()
//    {
//        return $"Alimento {Name} ({EnglishName}), langual {Langual}, origen: {Origin}";
//    }
//}

//public class Nutrient
//{
//    public int Id { get; set; }

//    public string? Name { get; set; }
//    public string? EnglishName { get; set; }

//    public int ComponentGroupId { get; set; }

//    public string? Unit { get; set; }

//    public override string ToString()
//    {
//        return $"Nutriente: {Name} ({EnglishName}), unidad: {Unit}";
//    }
//}

//public class FoodNutrient
//{
//    public int FoodId { get; set; }

//    public int NutrientId { get; set; }

//    public decimal? Value { get; set; }

//    public string? Moex { get; set; }

//    public override string ToString()
//    {
//        return $"Valor: {Value}, moex: {Moex}";
//    }
//}

public class Food
{
    public int Id { get; set; }                 // f_id
    public string? OriginalName { get; set; }   // f_ori_name
    public string? EnglishName { get; set; }    // f_eng_name
    public string? Origin { get; set; }         // f_origen
    public double? EdiblePortion { get; set; }  // edible_portion

    // Navegación opcional
    public List<FoodComposition>? Compositions { get; set; }
}

public class FoodComposition
{
    public int FoodId { get; set; }             // f_id
    public int NutrientId { get; set; }         // c_id
    public string? NutrientName { get; set; }   // c_ori_name
    public string? EurName { get; set; }        // eur_name (PROT, FAT, CHO, VITA, etc.)
    public double? Value { get; set; }          // valor numérico normalizado a por 100g
    public string? Unit { get; set; }           // v_unit (g, mg, ug, kJ...)
    public string? BestLocation { get; set; }   // valor original de best_location
}