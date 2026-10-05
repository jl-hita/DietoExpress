using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>
/// Regresiones del núcleo de especializaciones. Se mantienen como pruebas de arquitectura
/// porque el endpoint utiliza SQL explícito para aislar tenant y asignación histórica.
/// </summary>
public sealed class SpecializationsRegressionTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    [Fact]
    public void SpecializationsSchema_IsTenantScopedAndSeeded()
    {
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));

        Assert.Contains("UpgradeSpecializationsSchemaV1", bootstrap);
        Assert.Contains("CREATE TABLE IF NOT EXISTS specializations", bootstrap);
        Assert.Contains("CREATE TABLE IF NOT EXISTS tenant_specializations", bootstrap);
        Assert.Contains("CREATE TABLE IF NOT EXISTS client_specializations", bootstrap);
        Assert.Contains("CREATE TABLE IF NOT EXISTS specialization_rules", bootstrap);
        Assert.Contains("PRIMARY KEY (tenant_id, specialization_id)", bootstrap);
        Assert.Contains("CONSTRAINT uq_client_specialization UNIQUE (tenant_id, client_id, specialization_id)", bootstrap);
        Assert.Contains("INSERT INTO schema_migrations(id) VALUES ('specializations-v1')", bootstrap);
        Assert.Contains("'vegan'", bootstrap);
        Assert.Contains("'vegetarian'", bootstrap);
        Assert.Contains("'sports_nutrition'", bootstrap);
        Assert.Contains("'diabetes'", bootstrap);
        Assert.Contains("'celiac'", bootstrap);
    }

    [Fact]
    public void SpecializationsBootstrap_IsInvokedDuringStartup()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Program.cs"));

        Assert.Contains("DatabaseBootstrap.UpgradeSpecializationsSchemaV1(context, logger);", program);
    }

    [Fact]
    public void SpecializationsCatalog_IsTenantScoped()
    {
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "SpecializationsController.cs"));

        Assert.Contains("[Authorize(Policy = \"Professional\")]", controller);
        Assert.Contains("ts.tenant_id=@tenant", controller);
        Assert.Contains("FROM specializations s", controller);
        Assert.Contains("WHERE s.active = TRUE", controller);
        Assert.Contains("Add(command, \"tenant\", tenantId.Value)", controller);
    }

    [Fact]
    public void ClientSpecializations_ValidateClientTenantAndAssignment()
    {
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "SpecializationsController.cs"));

        Assert.Contains("c.id=@client AND c.tenant_id=@tenant AND c.archived_at IS NULL", controller);
        Assert.Contains("a.client_id=c.id AND a.nutritionist_id=@user AND a.is_active=TRUE", controller);
        Assert.Contains("User.IsInRole(\"clinic_admin\") || owner || assigned", controller);
        Assert.Contains("JOIN tenant_specializations ts", controller);
        Assert.Contains("ts.tenant_id=@tenant AND ts.enabled=TRUE", controller);
    }

    [Fact]
    public void ClientSpecializations_AreReplacedAtomically()
    {
        var controller = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "SpecializationsController.cs"));

        Assert.Contains("BeginTransactionAsync(IsolationLevel.ReadCommitted)", controller);
        Assert.Contains("delete.Transaction = transaction", controller);
        Assert.Contains("validate.Transaction = transaction", controller);
        Assert.Contains("insert.Transaction = transaction", controller);
        Assert.Contains("await transaction.CommitAsync()", controller);
    }

    [Fact]
    public void SpecializationsUi_IsReachableFromClientDetail()
    {
        var routes = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "app.routes.ts"));
        var detail = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "client-detail", "client-detail.component.html"));
        var component = File.ReadAllText(Path.Combine(RepoRoot, "anguloso.client", "src", "app", "componentes", "client-specializations", "client-specializations.component.ts"));

        Assert.Contains("clients/:id/specializations", routes);
        Assert.Contains("['/clients', clientId, 'specializations']", detail);
        Assert.Contains("SpecializationService", component);
        Assert.Contains("setClientSpecializations", component);
    }

    [Fact]
    public void DietarySpecializations_AreSeededWithFoodExclusionRules()
    {
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));
        Assert.Contains("exclude_food_keywords", bootstrap);
        Assert.Contains("'vegan'", bootstrap);
        Assert.Contains("'vegetarian'", bootstrap);
        Assert.Contains("'pescatarian'", bootstrap);
        Assert.Contains("configuration::jsonb", bootstrap);
    }

    [Fact]
    public void DietaryCompatibility_UsesStructuredFoodFlags()
    {
        var bootstrap = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DatabaseBootstrap.cs"));
        var resolver = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "SpecializationRulesService.cs"));
        var generator = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));
        var validation = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietValidationService.cs"));

        Assert.Contains("dietary_flags TEXT[]", bootstrap);
        Assert.Contains("classify_food_dietary_flags", bootstrap);
        Assert.Contains("trigo|harina de trigo", bootstrap);
        Assert.Contains("lactosa|leche", bootstrap);
        Assert.Contains("'animal'", bootstrap);
        Assert.Contains("'gluten'", bootstrap);
        Assert.Contains("'lactose'", bootstrap);
        Assert.Contains("('celiac','exclude_food_gluten'", bootstrap);
        Assert.Contains("('lactose_intolerance','exclude_food_lactose'", bootstrap);
        Assert.Contains("\"required_flags\"", bootstrap);
        Assert.Contains("GetExcludedFoodIdsAsync", resolver);
        Assert.Contains("dietary_flags && @required_flags", resolver);
        Assert.Contains("structuredSpecializationExclusions", generator);
        Assert.Contains("structuredSpecializationExclusions", validation);
    }

    [Fact]
    public void DietGeneration_ConsumesSpecializationRules()
    {
        var generator = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietGeneratorService.cs"));
        var resolver = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "SpecializationRulesService.cs"));
        var validation = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Logica", "DietValidationService.cs"));

        Assert.Contains("SpecializationRulesService", generator);
        Assert.Contains("GetFoodExclusionsAsync", generator);
        Assert.Contains("client_specializations", resolver);
        Assert.Contains("tenant_specializations", resolver);
        Assert.Contains("food_exclusion", resolver);
        Assert.Contains("specializationExclusions", validation);
        Assert.Contains("AlertType = \"Specialization\"", validation);
    }
}
