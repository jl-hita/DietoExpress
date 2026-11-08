using Anguloso.Server.Logica;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Volcado de solo lectura del catálogo de ejercicios para su curación externa. Misma clave compartida en el cuerpo
/// JSON que <see cref="FoodCurationController"/>; con clave incorrecta responde 404. Paginado por cursor (exercise_id).
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/exercise-curation")]
public class ExerciseCurationController : ControllerBase
{
    private const int MaxTake = 300;
    private readonly ExerciseCatalogService _catalog;

    public ExerciseCurationController(ExerciseCatalogService catalog) => _catalog = catalog;

    public sealed class ExportRequest
    {
        public string? Key { get; set; }
        public string? AfterId { get; set; }
        public int Take { get; set; } = 100;
        public string? Source { get; set; }
        public string? ReviewStatus { get; set; }
    }

    [HttpPost("export")]
    [EnableRateLimiting("expensive")]
    public async Task<IActionResult> Export([FromBody] ExportRequest request, CancellationToken ct)
    {
        if (!FoodCurationController.IsAuthorized(request?.Key)) return NotFound();
        var (total, rows) = await _catalog.ExportCurationPageAsync(
            string.IsNullOrWhiteSpace(request!.AfterId) ? null : request.AfterId,
            Math.Clamp(request.Take, 1, MaxTake),
            string.IsNullOrWhiteSpace(request.Source) ? null : request.Source,
            string.IsNullOrWhiteSpace(request.ReviewStatus) ? null : request.ReviewStatus, ct);
        return Ok(new { total, nextAfterId = rows.Count == 0 ? null : rows[^1].Id, items = rows });
    }
}
