using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class DietTherapyRuleEngineTests
{
    [Fact]
    public void ApplyWeightManagementTarget_UsesDefaultDeficitWhenProfileValueIsMissing()
    {
        Assert.Equal(2125, DietTherapyRuleEngine.ApplyWeightManagementTarget(2500, null, 0.5, null));
    }

    [Fact]
    public void ApplyWeightManagementTarget_ClampsInvalidAndExtremeInputs()
    {
        Assert.Equal(5000, DietTherapyRuleEngine.ApplyWeightManagementTarget(double.NaN, 100, 0, 5000));
        Assert.Equal(1200, DietTherapyRuleEngine.ApplyWeightManagementTarget(1000, 20, 1.5, null));
        // Mantenimiento 1200 kcal con déficit del 20 %: antes daba 960 kcal, ahora se respeta el suelo de seguridad.
        Assert.Equal(1200, DietTherapyRuleEngine.ApplyWeightManagementTarget(1200, 20, null, null));
        // El mínimo del perfil del profesional puede subir el suelo, nunca bajarlo de 1200.
        Assert.Equal(1500, DietTherapyRuleEngine.ApplyWeightManagementTarget(1800, 20, null, 1500));
        Assert.Equal(1200, DietTherapyRuleEngine.ApplyWeightManagementTarget(1300, 20, null, 900));
    }
}
