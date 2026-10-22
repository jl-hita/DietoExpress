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
        Assert.Equal(800, DietTherapyRuleEngine.ApplyWeightManagementTarget(1000, 20, 1.5, null));
    }
}
