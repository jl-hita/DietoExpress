using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Planificación deportiva opcional por cliente. La existencia de un perfil es la activación explícita del módulo.
/// </summary>
[ApiController]
[Route("api/training")]
[Authorize(Policy = "Professional")]
public sealed partial class TrainingPeriodizationController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly EmailServ _emailServ;
    private readonly ExerciseCatalogService _exerciseCatalog;

    public TrainingPeriodizationController(angulosodbContext context, EmailServ emailServ, ExerciseCatalogService exerciseCatalog)
    {
        _context = context;
        _emailServ = emailServ;
        _exerciseCatalog = exerciseCatalog;
    }
}
