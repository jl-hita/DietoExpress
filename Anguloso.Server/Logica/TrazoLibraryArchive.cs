using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Anguloso.Server.Logica;

/// <summary>
/// Reads and validates the archive TRAZO sends to DietoExpress (schemaVersion 1): <c>manifest.json</c>,
/// <c>exercises.json</c> (entries in the format of <c>exercise-catalog-additions.json</c>) and
/// <c>images/{exerciseId}/{slot}.{ext}</c>. Everything is checked before any database work: sizes, SHA-256 hashes,
/// identifiers, paths, image signatures and the same rules the curated additions must satisfy.
/// </summary>
public static partial class TrazoLibraryArchive
{
    public const int MaximumArchiveBytes = 64 * 1024 * 1024;
    public const int MaximumExercises = 2000;
    private const int MaximumJsonBytes = 16 * 1024 * 1024;

    public sealed record Image(string ExerciseId, int Slot, string ContentType, byte[] Data);

    public sealed record Content(IReadOnlyList<ExerciseCatalogAdditionsSeeder.Entry> Exercises, IReadOnlyList<Image> Images);

    private sealed record ManifestImage(string ExerciseId, int Slot, string Path, string ContentType, string Source, int Size, string Sha256);

    private sealed record Manifest(int SchemaVersion, string Source, int ExerciseCount, int ImageCount, string ExercisesSha256, List<ManifestImage>? Images);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^[a-z0-9-]{1,100}$")]
    private static partial Regex SafeId();

    public static Content Read(Stream archiveStream)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        ZipArchive archive;
        try { archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException) { throw new InvalidDataException("El archivo no es un ZIP válido."); }

        using (archive)
        {
            var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Falta manifest.json.");
            var exercisesEntry = archive.GetEntry("exercises.json") ?? throw new InvalidDataException("Falta exercises.json.");
            var manifest = Parse<Manifest>(ReadEntry(manifestEntry, 1024 * 1024), "manifest.json");
            if (manifest.SchemaVersion != 1 || !string.Equals(manifest.Source, "TRAZO", StringComparison.Ordinal))
                throw new InvalidDataException("El manifiesto no es de TRAZO o su versión no está soportada.");
            if (manifest.ExerciseCount is < 1 or > MaximumExercises || manifest.ImageCount < 0 ||
                manifest.ImageCount > manifest.ExerciseCount * 4 || manifest.Images is null || manifest.Images.Count != manifest.ImageCount ||
                archive.Entries.Count != manifest.ImageCount + 2)
                throw new InvalidDataException("El manifiesto no coincide con el contenido del archivo.");

            var exerciseBytes = ReadEntry(exercisesEntry, MaximumJsonBytes);
            if (!Hash(exerciseBytes).Equals(manifest.ExercisesSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La suma SHA-256 de exercises.json no coincide.");
            var exercises = Parse<List<ExerciseCatalogAdditionsSeeder.Entry>>(exerciseBytes, "exercises.json");
            if (exercises.Count != manifest.ExerciseCount)
                throw new InvalidDataException("El número de ejercicios no coincide con el manifiesto.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var exercise in exercises)
            {
                if (!SafeId().IsMatch(exercise.Id ?? "") || !ids.Add(exercise.Id!))
                    throw new InvalidDataException($"El ejercicio '{exercise.Id}' tiene un identificador inválido o repetido.");
                var problem = ExerciseCatalogAdditionsSeeder.Validate(exercise);
                if (problem is not null) throw new InvalidDataException($"El ejercicio '{exercise.Id}' no es válido: {problem}.");
            }

            var images = new List<Image>();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var meta in manifest.Images)
            {
                var extension = meta.ContentType switch { "image/webp" => "webp", "image/png" => "png", "image/jpeg" => "jpg", _ => null };
                if (extension is null || !ids.Contains(meta.ExerciseId ?? "") || meta.Slot is < 0 or > ExerciseImageService.MaximumSlot ||
                    meta.Size is <= 0 or > ExerciseImageService.MaximumImageBytes ||
                    meta.Path != $"images/{meta.ExerciseId}/{meta.Slot}.{extension}" || !paths.Add(meta.Path))
                    throw new InvalidDataException("Una entrada de imagen del manifiesto no es válida.");
                var entry = archive.GetEntry(meta.Path) ?? throw new InvalidDataException($"Falta la imagen '{meta.Path}'.");
                var bytes = ReadEntry(entry, ExerciseImageService.MaximumImageBytes);
                if (bytes.Length != meta.Size || !Hash(bytes).Equals(meta.Sha256, StringComparison.OrdinalIgnoreCase) ||
                    ExerciseImageService.DetectImageContentType(bytes) != meta.ContentType)
                    throw new InvalidDataException($"La imagen '{meta.Path}' no coincide con el manifiesto.");
                images.Add(new Image(meta.ExerciseId!, meta.Slot, meta.ContentType, bytes));
            }
            return new Content(exercises, images);
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // The declared entry length is not trusted: read at most the allowed number of bytes.
    private static byte[] ReadEntry(ZipArchiveEntry entry, int maximumBytes)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > maximumBytes) throw new InvalidDataException($"'{entry.FullName}' supera el tamaño permitido.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static T Parse<T>(byte[] bytes, string name)
    {
        try { return JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new InvalidDataException($"{name} está vacío."); }
        catch (JsonException) { throw new InvalidDataException($"{name} no es un JSON válido."); }
    }
}
