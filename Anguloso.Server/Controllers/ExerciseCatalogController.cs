using Anguloso.Server.Logica;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/exercise-catalog")]
[Authorize(Roles = "nutritionist,clinic_admin,superadmin")]
public sealed class ExerciseCatalogController : ControllerBase
{
    private readonly ExerciseCatalogService _catalog;
    private readonly ILogger<ExerciseCatalogController> _logger;

    public ExerciseCatalogController(ExerciseCatalogService catalog, ILogger<ExerciseCatalogController> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ExerciseCatalogItem>>> Search(
        [FromQuery] string? q,
        [FromQuery] string? bodyPart,
        [FromQuery] string? equipment,
        [FromQuery] int limit = 80,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await _catalog.SearchAsync(q, bodyPart, equipment, limit, cancellationToken));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to query the PostgreSQL exercise catalog.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "La biblioteca de ejercicios no está disponible temporalmente." });
        }
    }

    [HttpPost("sync")]
    [Authorize(Roles = "superadmin")]
    public async Task<ActionResult<ExerciseCatalogSyncResult>> Synchronize(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _catalog.SyncFromSourceAsync(cancellationToken));
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "RepDB exercise catalog synchronization failed.");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "No se pudo descargar el catálogo de RepDB. No se han confirmado cambios parciales." });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Exercise catalog synchronization failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "No se pudo importar el catálogo de ejercicios." });
        }
    }
}
