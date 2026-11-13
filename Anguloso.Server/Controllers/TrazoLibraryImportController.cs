using System.Security.Cryptography;
using System.Text;
using Anguloso.Server.Logica;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Private, server-to-server receiver for the exercises created in TRAZO (the reverse of
/// <see cref="ExerciseLibraryTransferController"/>). It is not a user endpoint: it is protected by the shared secret
/// <c>TrazoTransfer:ApiKey</c>, the same one used for the transfer to TRAZO, sent in <c>X-Trazo-Transfer-Key</c>.
/// </summary>
[ApiController]
[Route("api/internal/trazo-exercise-library")]
[AllowAnonymous]
public sealed class TrazoLibraryImportController(
    angulosodbContext context,
    ExerciseImageService images,
    IConfiguration configuration,
    ILogger<TrazoLibraryImportController> logger) : ControllerBase
{
    public const string KeyHeader = "X-Trazo-Transfer-Key";

    private static readonly ExerciseCatalogAdditionsSeeder.UpsertOptions Options = new(
        ReviewerPrefix: "TRAZO transfer v",
        Origin: "TRAZO",
        SchemaPrefix: "trazo-transfer-v",
        Notes: "Ejercicio creado en TRAZO y transferido a DietoExpress.");

    [HttpPost("import")]
    [RequestSizeLimit(TrazoLibraryArchive.MaximumArchiveBytes)]
    public async Task<IActionResult> Import(CancellationToken cancellationToken)
    {
        var expected = configuration["TrazoTransfer:ApiKey"];
        if (string.IsNullOrWhiteSpace(expected))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "La importación desde TRAZO no está configurada en este servidor." });

        var supplied = Request.Headers[KeyHeader].ToString();
        if (string.IsNullOrWhiteSpace(supplied) ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
        {
            logger.LogWarning("Rejected a TRAZO exercise-library import with a missing or wrong key.");
            return Unauthorized();
        }
        if (!string.Equals(Request.ContentType?.Split(';')[0], "application/zip", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "El Content-Type debe ser application/zip." });

        TrazoLibraryArchive.Content content;
        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[128 * 1024];
            int read;
            while ((read = await Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > TrazoLibraryArchive.MaximumArchiveBytes)
                    return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = "El archivo supera el tamaño permitido." });
                buffer.Write(chunk, 0, read);
            }
            buffer.Position = 0;
            content = TrazoLibraryArchive.Read(buffer);
        }
        catch (InvalidDataException exception)
        {
            logger.LogWarning(exception, "Rejected an invalid TRAZO exercise-library archive.");
            return BadRequest(new { error = exception.Message });
        }

        try
        {
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            var result = await ExerciseCatalogAdditionsSeeder.UpsertEntriesAsync(connection, logger, content.Exercises, Options, cancellationToken);
            await images.EnsureSchemaAsync(cancellationToken);
            var stored = 0;
            foreach (var image in content.Images)
                if (await images.StoreImportedImageAsync(image.ExerciseId, image.Slot, image.Data, cancellationToken)) stored++;

            logger.LogInformation(
                "Imported the TRAZO exercise library: {Inserted} new, {Updated} refreshed, {Skipped} kept, {Images} images stored.",
                result.Inserted, result.Updated, result.Skipped, stored);
            return Ok(new
            {
                exercisesInserted = result.Inserted, exercisesUpdated = result.Updated, exercisesSkipped = result.Skipped,
                imagesStored = stored, imagesSkipped = content.Images.Count - stored
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The TRAZO exercise-library import failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "No se pudo importar la biblioteca de ejercicios de TRAZO." });
        }
    }
}
