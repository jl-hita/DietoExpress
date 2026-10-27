using Anguloso.Server.Logica;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

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
            var ownerUserId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId) ? parsedUserId : (int?)null;
            return Ok(await _catalog.SearchAsync(q, bodyPart, equipment, limit, cancellationToken, ownerUserId: ownerUserId));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to query the PostgreSQL exercise catalog.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "La biblioteca de ejercicios no está disponible temporalmente." });
        }
    }


    [HttpGet("review-queue")]
    [Authorize(Roles = "superadmin")]
    public async Task<ActionResult<IReadOnlyList<ExerciseCatalogItem>>> ReviewQueue([FromQuery] string? q, [FromQuery] string status = "pending", [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        try { return Ok(await _catalog.SearchAsync(q, null, null, Math.Clamp(limit, 1, 100), cancellationToken, status, includeAllOwners: true)); }
        catch (Exception exception) { _logger.LogError(exception, "Failed to load exercise review queue."); return StatusCode(503, new { error = "No se pudo cargar la cola de revisión." }); }
    }

    [HttpPut("{id}/review")]
    [Authorize(Roles = "superadmin")]
    public async Task<IActionResult> Review(string id, [FromBody] ExerciseReviewRequest review, CancellationToken cancellationToken)
    {
        try
        {
            var reviewer = User.Identity?.Name ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "superadmin";
            return await _catalog.SaveReviewAsync(id, review, reviewer, cancellationToken) ? NoContent() : NotFound();
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (Exception exception) { _logger.LogError(exception, "Failed to save review for exercise {ExerciseId}.", id); return StatusCode(500, new { error = "No se pudo guardar la revisión." }); }
    }

    [HttpPost("contributions")]
    [Authorize(Roles = "nutritionist,clinic_admin")]
    public async Task<IActionResult> Contribute([FromBody] ExerciseContributionRequest contribution, CancellationToken cancellationToken)
    {
        try
        {
            var contributor = User.Identity?.Name ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "professional";
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var contributorUserId)) return Unauthorized();
            var id = await _catalog.AddProfessionalContributionAsync(contribution.Name, contribution.Description, contribution.Equipment, contribution.BodyPart, contributor, contributorUserId, cancellationToken);
            return Created($"/api/exercise-catalog/{id}", new { id, status = "pending" });
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (Exception exception) { _logger.LogError(exception, "Failed to submit exercise contribution."); return StatusCode(500, new { error = "No se pudo enviar el ejercicio a revisión." }); }
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

public sealed record ExerciseContributionRequest(string Name, string? Description, string? Equipment, string? BodyPart);
