using System.Reflection;
using Anguloso.Server.Controllers;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>Altas de pacientes en la prueba cerrada de la app Android.</summary>
public sealed class AndroidTesterRequestTests
{
    [Theory]
    [InlineData("  Paciente@Gmail.com ", "paciente@gmail.com")]
    [InlineData("nombre.apellido@empresa.es", "nombre.apellido@empresa.es")]
    [InlineData("", null)]
    [InlineData("sin-arroba", null)]
    [InlineData("a@localhost", null)]
    [InlineData("a@gmail.com,b@gmail.com", null)]
    [InlineData("Nombre <a@gmail.com>", null)]
    [InlineData("a@gmail.com\r\nBcc: b@gmail.com", null)]
    public void OnlyASingleAddressIsAccepted(string input, string? expected)
        => Assert.Equal(expected, AndroidTestersController.NormalizeEmail(input));

    [Theory]
    [InlineData(nameof(AndroidTestersController.List))]
    [InlineData(nameof(AndroidTestersController.Invite))]
    public void AdminActionsAreLimitedToSuperAdmin(string action)
        => Assert.Equal("superadmin", Attribute(action).Roles);

    [Theory]
    [InlineData(nameof(AndroidTestersController.Get))]
    [InlineData(nameof(AndroidTestersController.RequestAccess))]
    public void ClientActionsRequireAProfessional(string action)
        => Assert.Equal("Professional", Attribute(action).Policy);

    private static AuthorizeAttribute Attribute(string action)
        => typeof(AndroidTestersController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>()!;
}
