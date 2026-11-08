using Anguloso.Server.Models;

namespace DietoExpress.Security.Tests;

/// <summary>
/// Catálogo de prueba con nombres y valores reales del formato BEDCA (crudo vs cocinado, valores incoherentes incluidos)
/// para ejercitar el clasificador y el motor de menús sin depender de la base de datos de desarrollo.
/// Formato: nombre|kcal|proteína|hidratos|grasa por 100 g.
/// </summary>
internal static class BedcaLikeCatalog
{
    private const string Raw = """
Aceite de oliva|883|0|0|100
Aceite de oliva virgen extra|884|-|0|100
Aceite de girasol|883|-|0|100
Aceite de algodón|884|0|0|100
Manzana|50|0.3|12|0.1
Naranja|38|0.8|8.6|0.1
Plátano|89|1|20|0.3
Pera|45|0.4|11|0.1
Mandarina|40|0.7|9|0.1
Fresa|36|0.7|7|0.5
Kiwi|52|1|11|0.6
Melocotón|39|0.9|9|0.1
Melón|27|0.8|6|0.1
Sandía|20|0.4|5|0.1
Uva blanca|68|0.6|16|0.1
Frambuesa, cruda|38|1.3|7|0.7
Arandano|32|0.7|6|0.4
Ciruela, con piel, cruda|54|0.7|12|0.2
Zumo de naranja|41|0.7|9|0.2
Melocotón, desecado|270|4|61|1
Tomate|19|0.9|3.5|0.1
Lechuga|16|1.1|1.4|0.6
Pepino|12|0.7|2|0.1
Zanahoria, cruda|34|0.9|7|0.2
Rúcula|31|2.6|3.7|0.7
Canonigos|14|2|1|0.4
Escarola|12|1.6|1|0.2
Pimiento rojo, crudo|29|1|5|0.5
Calabacín|17|1.6|2.2|0.2
Brécol, hervido|24|2.8|1.2|0.7
Brécol, crudo|26|3.3|2.2|0.4
Espinaca, hervida|22|2.1|2|0.4
Acelgas, hervidas|29|2.5|3.7|0.2
Berenjena|20|1.2|3.6|0.3
Coliflor, hervida|27|2.7|2.3|0.7
Judía verde, cruda|28|2.1|3.7|0.4
Judia verde, hervida|83|1.8|3.9|6.8
Champiñon|26|2.1|3.8|0.4
Calabaza, hervida|12|0.6|2.2|0.1
Puerro, crudo y congelado|24|2|4|0.3
Apio, crudo|11|0.8|1.8|0.2
Pollo, pechuga, con piel, crudo|105|23|0|1
Pollo, muslo, con piel, crudo|109|20|0|3
Pollo, pechuga, plancha|145|22|0|6
Pollo, frito|202|17|0.7|14.6
Pollo, fiambre|140|20|2|6
Grasa de pollo|883|0|0|99.8
Pavo, pechuga, con piel, crudo|107|24|0|1
Pavo, pechuga, sin piel, a la plancha|145|30|0|3
Pavo, fiambre, bajo en grasa|100|19|4|1
Ternera, solomillo, sin grasa, crudo|109|20|0|3
Ternera, lomo, crudo, con grasa separable|111|20|0|3
Ternera, costilla, cruda|140|20|0|7
Cerdo, lomo, crudo|152|18|0|9
Cerdo, solomillo, crudo|130|21|0|5
Cerdo, panceta, cruda|465|13|0.5|46.6
Conejo de granja, carne, cruda|131|21|0|5
Jamón cocido, categoría s/e|114|21|0|3
Jamón serrano|318|29|0|23
Chorizo|322|27|2|23
Merluza fresca|64|11.9|0|1.8
Merluza, congelada, cruda|85|16.7|0|1.9
Merluza, rebozada, frita|149|10.7|3.5|10.3
Bacalao, crudo|83|18.2|0.3|1
Lubina|85|18|0|1
Dorada, cruda|78|17|0|7
Lenguado|79|17|0|1
Rape, crudo|70|16|0|1
Pescadilla, congelada, cruda|55|12|0|1
Salmón|181|18.4|0|12
Salmón, plancha|179|20.2|0|11
Salmón ahumado|180|25.4|0|8.7
Atún, crudo|119|22|0|3
Atún, al natural|101|24|0|1
Sardina|140|18|0|8
Caballa, cruda|181|19|0|12
Trucha|90|16|0|3
Gamba, hervida|96|20|0|2
Langostino|112|24|2|1
Calamar, crudo|72|14|1|1
Sepia|80|18|0|1
Mejillón, hervido|112|18|3|3
Huevo de gallina fresco|149|12.5|0|11.1
Huevo de gallina, hervido, duro|145|12.5|0.3|10.5
Huevo de gallina, escalfado|146|12.6|0.3|10.6
Huevo de gallina, clara, cruda|45|10.5|0.3|0.1
Huevo de codorniz, entero, crudo|153|13.1|0.4|11.1
Tortilla, a la francesa|170|15|0|12
Tofu|119|11.5|3.3|6.6
Seitán|123|24|2|2
Yogur, desnatado, sabor natural|46|4|6|0.2
Yogur, enriquecido, natural|63|3|4|4
Yogur griego|138|6.4|5.4|10.2
Yogur, búlgaro|74|5|5.8|3.4
Yogur líquido, con frutas s/e|87|3|14|2
Yogur, desnatado, con frutas|71|4|13|0.2
Kefir|64|3|5|4
Leche de vaca, entera|65|3|4.7|3.8
Leche, semidesnatada, pasteurizada|46|3.2|4.6|1.6
Leche, desnatada, pasteurizada|34|3.3|4.6|0.2
Leche de almendra|351|14.5|60|5.5
Bebida de soja|53|3|6|2
Queso fresco de burgos|197|12|3|15
Requesón|102|12|3|4
Cuajada|87|5|7|5
Queso manchego|388|29|1|30
Pan integral|250|10.9|44|3
Pan de centeno|220|6|46|1
Pan de avena|270|8.4|48.5|4.4
Pan blanco, de barra|239|8|47|2
Pan rallado|258|8.5|51.5|1.6
Avena, cruda|399|16.9|66.3|6.9
Muesli|382|9|61|11
Cereales desayuno base de maíz y miel|393|6|83|4
Arroz|386|7|86|0.9
Arroz integral, crudo|384|7.5|81.3|2.6
Arroz integral, hervido|112|2.6|22.9|0.9
Arroz, hervido|392|7.5|85.3|1.7
Arroz con leche|92|3|16|1
Pasta alimenticia, cruda|352|12.5|70.9|1.4
Pasta alimenticia, integral, cruda|346|13|66|3
Pasta alimenticia, integral, hervida|121|5|23|1
Pasta alimenticia, con huevo, cruda|373|14|69|4
Patata, hervida|75|2.4|15.6|0.2
Patata, cruda|73|2.2|15.2|0.2
Patata, asada|206|4.3|46|0.1
Boniato, crudo|101|1|23|0.3
Quinoa, cruda|305|14|49|6
Sémola de trigo, cruda|348|13|70|1
Lenteja, seca, cruda|309|25|49|1
Lenteja, en conserva|74|6.3|11.4|0.2
Lenteja, hervida|331|23|54.8|1.7
Garbanzo, seco, crudo|334|19.3|49.2|6.3
Garbanzo, en conserva|133|7.2|14.1|2.9
Garbanzo, hervido|359|8.9|18.7|2.5
Alubia blanca, seca, cruda|271|22|42|1
Alubia blanca, en conserva|93|7|16|0.4
Alubia negra, seca, remojada, hervida|92|8|14|0.4
Almendra, cruda|586|19|6|45
Almendra, frita, salada|590|19|5|56
Nuez|592|14|3|63
Avellana|653|12|11|56
Pistacho|592|18|16|49
Anacardo, crudo|574|18|32|43
Cacahuete, frito, salado|590|27|9|50
Aguacate|136|2|0|12
Miel|314|1|77|0
Gelatina|343|84|0|0
""";

    public static List<foods> Create(string allergenStatus = "reported")
    {
        var list = new List<foods>();
        var id = 1;
        foreach (var line in Raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = line.Split('|');
            double? D(string s) => s == "-" ? null : double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            list.Add(new foods
            {
                id = id++,
                name = p[0],
                kcal = D(p[1]),
                protein = D(p[2]),
                carbs = D(p[3]),
                fat = D(p[4]),
                source = "bedca",
                allergen_data_status = allergenStatus,
                allergen_tags = Array.Empty<string>(),
                traces_tags = Array.Empty<string>()
            });
        }
        return list;
    }
}
