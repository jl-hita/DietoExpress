using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Anguloso.Server.Logica;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anguloso.Server.Controllers;

/// <summary>
/// Private, server-to-server transfer of DietoExpress' approved global exercise library to TRAZO.
/// The archive is never returned to the browser or exposed as a public download.
/// </summary>
[ApiController]
[Route("api/admin/exercise-library-transfer")]
[Authorize(Roles = "superadmin")]
public sealed class ExerciseLibraryTransferController(
    ExerciseCatalogService catalog,
    ExerciseImageService images,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ExerciseLibraryTransferController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost("trazo")]
    [RequestSizeLimit(1024L * 1024L * 1024L)]
    public async Task<IActionResult> TransferToTrazo(CancellationToken cancellationToken)
    {
        var endpoint = configuration["TrazoTransfer:Endpoint"];
        var apiKey = configuration["TrazoTransfer:ApiKey"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var target) ||
            target.Scheme != Uri.UriSchemeHttps && !target.IsLoopback ||
            string.IsNullOrWhiteSpace(apiKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "La transferencia a TRAZO no está configurada. Define TrazoTransfer:Endpoint y TrazoTransfer:ApiKey en el servidor."
            });

        var tempPath = Path.Combine(Path.GetTempPath(), $"dietoexpress-trazo-{Guid.NewGuid():N}.zip");
        try
        {
            var exercises = await catalog.ExportCatalogAsync(cancellationToken, approvedGlobalOnly: true);
            if (exercises.Count == 0)
                return Conflict(new { error = "No hay ejercicios globales aprobados que transferir." });

            var imageManifest = new List<object>();
            string exerciseHash;
            await using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
                {
                    var exerciseBytes = JsonSerializer.SerializeToUtf8Bytes(exercises, JsonOptions);
                    exerciseHash = Convert.ToHexString(SHA256.HashData(exerciseBytes)).ToLowerInvariant();
                    var exerciseEntry = archive.CreateEntry("exercises.json", CompressionLevel.Optimal);
                    await using (var entryStream = exerciseEntry.Open())
                        await entryStream.WriteAsync(exerciseBytes, cancellationToken);

                    var imageCount = 0;
                    foreach (var exercise in exercises)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(exercise.Id) ||
                            exercise.Id.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')))
                            throw new InvalidDataException($"Invalid exercise identifier '{exercise.Id}'.");

                        for (var slot = 0; slot <= ExerciseImageService.MaximumSlot; slot++)
                        {
                            var image = await images.GetImageAsync(exercise.Id, slot, cancellationToken);
                            if (image is null) continue;

                            var extension = image.ContentType switch
                            {
                                "image/webp" => "webp",
                                "image/png" => "png",
                                "image/jpeg" => "jpg",
                                "image/svg+xml" => "svg",
                                _ => throw new InvalidDataException($"Unsupported image type for {exercise.Id}/{slot}.")
                            };
                            var path = $"images/{exercise.Id}/{slot}.{extension}";
                            var bytes = image.Data;
                            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                            var imageEntry = archive.CreateEntry(path, CompressionLevel.Optimal);
                            await using (var imageStream = imageEntry.Open())
                                await imageStream.WriteAsync(bytes, cancellationToken);
                            imageManifest.Add(new { exerciseId = exercise.Id, slot, path, contentType = image.ContentType, source = image.Source, size = bytes.Length, sha256 = hash });
                            imageCount++;
                        }
                    }

                    var manifest = new
                    {
                        schemaVersion = 1,
                        source = "DietoExpress",
                        exportedAtUtc = DateTimeOffset.UtcNow,
                        exerciseCount = exercises.Count,
                        imageCount,
                        attribution = "Exercise data by RepDB (repdb.co)",
                        exercisesSha256 = exerciseHash,
                        images = imageManifest
                    };
                    var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                    await using var manifestStream = manifestEntry.Open();
                    await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken);
                }
            }

            var importUri = new Uri(target, "api/internal/exercise-catalog/import");
            using var request = new HttpRequestMessage(HttpMethod.Post, importUri);
            request.Headers.Add("X-Trazo-Import-Key", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StreamContent(new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

            var client = httpClientFactory.CreateClient(nameof(ExerciseLibraryTransferController));
            client.Timeout = TimeSpan.FromMinutes(15);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("TRAZO exercise-library import returned {StatusCode}: {Response}", (int)response.StatusCode,
                    responseBody.Length > 1000 ? responseBody[..1000] : responseBody);
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = "TRAZO no ha aceptado la importación. No se ha confirmado la transferencia.",
                    targetStatus = (int)response.StatusCode
                });
            }

            logger.LogInformation("Transferred {ExerciseCount} approved global exercises and {ImageCount} images to TRAZO.",
                exercises.Count, imageManifest.Count);
            return Content(responseBody, "application/json");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Private DietoExpress to TRAZO exercise-library transfer failed.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "No se pudo completar la transferencia de la biblioteca de ejercicios." });
        }
        finally
        {
            try { if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); }
            catch (Exception exception) { logger.LogWarning(exception, "Could not remove temporary exercise transfer archive."); }
        }
    }
}
