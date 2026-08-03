namespace Anguloso.Server.Model;

public class UsdaFoodResponse
{
    public List<UsdaFood> foods { get; set; } = new();
}

public class UsdaFood
{
    public int fdcId { get; set; }
    public string description { get; set; }
    public List<UsdaNutrient> foodNutrients { get; set; } = new();
}

public class UsdaNutrient
{
    public UsdaNutrientInfo nutrient { get; set; }
    public double? amount { get; set; }
}

public class UsdaNutrientInfo
{
    public int id { get; set; }
    public string name { get; set; }
    public string unitName { get; set; }
}

public class MicronutrientesDto
{
    public double? VitaminaA { get; set; }
    public double? VitaminaC { get; set; }
    public double? VitaminaD { get; set; }
    public double? Hierro { get; set; }
    public double? Calcio { get; set; }
    public double? Magnesio { get; set; }
    public double? Zinc { get; set; }
}
