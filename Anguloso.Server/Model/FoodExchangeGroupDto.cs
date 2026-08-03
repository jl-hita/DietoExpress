namespace Anguloso.Server.Model;

public class FoodExchangeGroupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Kcal { get; set; }
    public decimal Protein { get; set; }
    public decimal Carbs { get; set; }
    public decimal Fat { get; set; }
}
