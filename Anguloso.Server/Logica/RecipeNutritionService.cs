using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>Fuente única de verdad para nutrición de recetas y propagación de restricciones.</summary>
public sealed class RecipeNutritionService
{
    private readonly angulosodbContext _context;
    public RecipeNutritionService(angulosodbContext context) => _context = context;

    public Task<RecipeDetailDto> BuildDetailAsync(recipes recipe, IReadOnlyDictionary<int, foods> foodMap, CancellationToken cancellationToken = default)
    {
        var servings = recipe.servings <= 0 ? 1 : recipe.servings;
        var ingredients = recipe.recipe_items.Where(i => i.grams > 0 && foodMap.ContainsKey(i.food_id)).Select(i => BuildIngredient(i, foodMap[i.food_id])).ToList();
        var totalKcal = Sum(ingredients.Select(i => i.Kcal));
        var totalProtein = Sum(ingredients.Select(i => i.Protein));
        var totalCarbs = Sum(ingredients.Select(i => i.Carbs));
        var totalFat = Sum(ingredients.Select(i => i.Fat));
        var flags = ingredients.SelectMany(i => i.DietaryFlags).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToList();
        return Task.FromResult(new RecipeDetailDto {
            Id=recipe.id, Name=recipe.name, Instructions=recipe.instructions, CreatedAt=recipe.created_at, Servings=servings, YieldGrams=recipe.yield_grams,
            Ingredients=ingredients, DietaryFlags=flags,
            Nutrition=new RecipeNutritionDto {
                TotalKcal=totalKcal, TotalProtein=totalProtein, TotalCarbs=totalCarbs, TotalFat=totalFat,
                PerServingKcal=Divide(totalKcal,servings), PerServingProtein=Divide(totalProtein,servings), PerServingCarbs=Divide(totalCarbs,servings), PerServingFat=Divide(totalFat,servings)
            }
        });
    }

    public async Task RefreshRecipeUsagesAsync(int recipeId, CancellationToken cancellationToken = default)
    {
        var recipe=await _context.recipes.AsNoTracking().Include(r=>r.recipe_items).ThenInclude(i=>i.food).FirstOrDefaultAsync(r=>r.id==recipeId,cancellationToken);
        if(recipe==null) return;
        var servings=recipe.servings<=0?1:recipe.servings;
        var kcal=recipe.recipe_items.Sum(i=>Value(i.food?.kcal,i.grams));
        var protein=recipe.recipe_items.Sum(i=>Value(i.food?.protein,i.grams));
        var carbs=recipe.recipe_items.Sum(i=>Value(i.food?.carbs,i.grams));
        var fat=recipe.recipe_items.Sum(i=>Value(i.food?.fat,i.grams));
        var usages=await _context.meal_items.Where(i=>i.recipe_id==recipeId).ToListAsync(cancellationToken);
        foreach(var item in usages){
            var multiplier=item.recipe_servings.GetValueOrDefault(1);
            item.kcal=(decimal?)Math.Round(kcal/(double)servings*(double)multiplier,2);
            item.protein=(decimal?)Math.Round(protein/(double)servings*(double)multiplier,2);
            item.carbs=(decimal?)Math.Round(carbs/(double)servings*(double)multiplier,2);
            item.fat=(decimal?)Math.Round(fat/(double)servings*(double)multiplier,2);
            if(recipe.yield_grams.HasValue) item.grams=Math.Round(recipe.yield_grams.Value/servings*(decimal)multiplier,2);
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static RecipeIngredientDto BuildIngredient(recipe_items item, foods food){var f=(double)item.grams/100.0;return new RecipeIngredientDto{FoodId=food.id,FoodName=food.name,Brands=food.brands,Grams=item.grams,Kcal=Scale(food.kcal,f),Protein=Scale(food.protein,f),Carbs=Scale(food.carbs,f),Fat=Scale(food.fat,f),DietaryFlags=(food.dietary_flags??Array.Empty<string>()).ToList()};}
    private static double? Scale(double? v,double f)=>v.HasValue?Math.Round(v.Value*f,2):null;
    private static double? Sum(IEnumerable<double?> v){var x=v.Where(a=>a.HasValue).Select(a=>a!.Value).ToList();return x.Count==0?null:Math.Round(x.Sum(),2);}
    private static double? Divide(double? v,decimal d)=>v.HasValue&&d>0?Math.Round(v.Value/(double)d,2):null;
    private static double Value(double? v,decimal g)=>v.GetValueOrDefault()*(double)g/100.0;
}
