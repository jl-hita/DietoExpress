using System.Security.Cryptography;
using System.Text;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Endpoint de mantenimiento para curar el catálogo de alimentos fuera de la aplicación. Se autoriza con una clave
/// compartida en el cuerpo JSON (<c>key</c>), no con sesión de usuario. Solo expone alimentos pendientes de curar
/// (sin fila en <c>food_dietary_profile</c>), paginados por id. Con clave incorrecta responde 404 para no revelar su existencia.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/food-curation")]
public class FoodCurationController : ControllerBase
{
    // SHA-256 de la clave de curación. La clave en claro no se guarda en el repositorio.
    // Se puede sustituir sin recompilar con la variable de entorno FOOD_CURATION_KEY_SHA256.
    private const string DefaultKeyHashHex = "a809f2840557daad2dfb42d0fbc91976fedc0d2a0617231bce105ad458449d07";
    private const int MaxTake = 500;

    private readonly angulosodbContext _db;

    public FoodCurationController(angulosodbContext db) => _db = db;

    public sealed class PendingRequest
    {
        public string? Key { get; set; }
        /// <summary>Último id recibido en la página anterior (paginación por cursor).</summary>
        public int AfterId { get; set; }
        public int Take { get; set; } = 200;
        public string? Source { get; set; }
    }

    [HttpPost("pending")]
    [EnableRateLimiting("expensive")]
    public async Task<IActionResult> Pending([FromBody] PendingRequest request, CancellationToken ct)
    {
        if (!IsAuthorized(request?.Key)) return NotFound();

        var take = Math.Clamp(request!.Take, 1, MaxTake);
        var pendingQuery = _db.foods.AsNoTracking()
            .Where(f => !_db.food_dietary_profile.Any(p => p.food_id == f.id));
        if (!string.IsNullOrWhiteSpace(request.Source))
            pendingQuery = pendingQuery.Where(f => f.source == request.Source);

        var total = await _db.foods.CountAsync(ct);
        var curated = await _db.food_dietary_profile.CountAsync(ct);
        var pendingTotal = await pendingQuery.CountAsync(ct);

        var items = await pendingQuery
            .Where(f => f.id > request.AfterId)
            .OrderBy(f => f.id)
            .Take(take)
            .Select(f => new
            {
                f.id,
                f.source,
                externalId = f.external_id,
                f.name,
                f.category,
                f.brands,
                f.kcal,
                f.protein,
                f.carbs,
                f.fat,
                dietaryFlags = f.dietary_flags,
                allergenTags = f.allergen_tags,
                allergenDataStatus = f.allergen_data_status
            })
            .ToListAsync(ct);

        return Ok(new
        {
            total,
            curated,
            pendingTotal,
            nextAfterId = items.Count == 0 ? (int?)null : items[^1].id,
            items
        });
    }

    private static bool IsAuthorized(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 256) return false;
        var expectedHex = Environment.GetEnvironmentVariable("FOOD_CURATION_KEY_SHA256");
        if (string.IsNullOrWhiteSpace(expectedHex)) expectedHex = DefaultKeyHashHex;
        byte[] expected;
        try { expected = Convert.FromHexString(expectedHex.Trim()); }
        catch (FormatException) { return false; }
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
