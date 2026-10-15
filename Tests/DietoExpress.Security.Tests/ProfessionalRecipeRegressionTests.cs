using Xunit;
namespace DietoExpress.Security.Tests;
public sealed class ProfessionalRecipeRegressionTests
{
    private static string RepoRoot { get { var d=new DirectoryInfo(AppContext.BaseDirectory); while(d!=null&&!File.Exists(Path.Combine(d.FullName,"Anguloso.Server","Program.cs"))) d=d.Parent; return d?.FullName??throw new InvalidOperationException("No se encontró la raíz."); } }
    [Fact] public void Recipes_ExposeServingAwareNutritionAndRestrictions(){var dto=File.ReadAllText(Path.Combine(RepoRoot,"Anguloso.Server","Model","RecipeDto.cs"));var service=File.ReadAllText(Path.Combine(RepoRoot,"Anguloso.Server","Logica","RecipeNutritionService.cs"));Assert.Contains("Servings",dto);Assert.Contains("PerServingKcal",dto);Assert.Contains("DietaryFlags",dto);Assert.Contains("SelectMany(i => i.DietaryFlags)",service);Assert.Contains("/100.0",service);}
    [Fact] public void RecipeSubstitutionRecalculatesDietUsages(){var controller=File.ReadAllText(Path.Combine(RepoRoot,"Anguloso.Server","Controllers","RecipesController.cs"));var service=File.ReadAllText(Path.Combine(RepoRoot,"Anguloso.Server","Logica","RecipeNutritionService.cs"));Assert.Contains("substitute",controller);Assert.Contains("PreserveCalories",controller);Assert.Contains("RefreshRecipeUsagesAsync",controller);Assert.Contains("recipe_id",service);Assert.Contains("recipe_servings",service);}
    [Fact] public void RecipeMutationsValidateNewIngredientsAgainstActivePatientRestrictions()
    {
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "RecipesController.cs"));
        var validation = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietValidationService.cs"));
        Assert.Contains("ValidateNewIngredientsForActiveAssignmentsAsync", controller);
        Assert.Contains("ValidateFoodForClientAsync", controller);
        Assert.Contains("return Conflict", controller);
        Assert.Contains("cd.is_active == true", controller);
        Assert.Contains("AllergenDataUnknown", validation);
        Assert.Contains("allergen_tags == null || !food.allergen_tags.Any()", validation);
    }
    [Fact] public void DietEditorPreservesRecipeReferencesDuringValidationAndSave()
    {
        var model = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "modelos", "diet.ts"));
        var editor = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "diet-create", "diet-create.component.ts"));
        Assert.Contains("recipeId?: number", model);
        Assert.Contains("recipeServings?: number", model);
        Assert.Contains("recipeId: [item.recipeId ?? null]", editor);
        Assert.Contains("recipeServings: [item.recipeServings ?? null]", editor);
        Assert.Contains("recipeId: i.recipeId ?? null", editor);
        Assert.Contains("recipeServings: i.recipeServings ?? null", editor);
        Assert.Contains("recipeId: i.recipeId ?? undefined", editor);
        Assert.Contains("recipeServings: i.recipeServings ?? undefined", editor);
    }
    [Fact] public void DietMealItemsCarryRecipeProvenance(){var model=File.ReadAllText(Path.Combine(RepoRoot,"Anguloso.Server","Models","meal_items.cs"));var dto=File.ReadAllText(Path.Combine(RepoRoot,"Anguloso.Server","Model","DietDto.cs"));Assert.Contains("recipe_id",model);Assert.Contains("recipe_servings",model);Assert.Contains("RecipeId",dto);Assert.Contains("RecipeServings",dto);}
}
