using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class TrazoLibraryImportTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;
            return directory!.FullName;
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] Webp()
    {
        var bytes = new byte[40];
        "RIFF"u8.CopyTo(bytes);
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        return bytes;
    }

    private static object Exercise(string id = "pendlay-row", string[]? instructions = null, string pattern = "HorizontalPull") => new
    {
        id, version = 29000000, nameEs = "Remo Pendlay", nameEn = "Pendlay Row", descriptionEs = "Remo explosivo desde el suelo.",
        instructionsEs = instructions ?? ["Inclina el tronco.", "Tira de la barra al abdomen.", "Devuélvela al suelo."],
        tipsEs = new[] { "Espalda neutra." }, forceType = "pull", mechanic = "compound", difficulty = "advanced", equipment = "barbell",
        bodyPart = "back", met = 4.0, isUnilateral = false, isBodyweight = false, movementPattern = pattern,
        primaryMuscles = new[] { "Lats", "UpperBack" }, secondaryMuscles = new[] { "Biceps" }, minimumExperience = "Advanced",
        suitableGoals = new[] { "Strength" }, requiredEquipment = new[] { "barbell" }, selectionPriority = 60
    };

    private sealed record Options(string Source = "TRAZO", string? ExercisesHash = null, string ImagePathOverride = "", string ImageType = "image/webp", bool OmitImage = false);

    private static MemoryStream Archive(object[]? exercises = null, bool withImage = true, Options? options = null)
    {
        options ??= new Options();
        exercises ??= [Exercise()];
        var exerciseBytes = JsonSerializer.SerializeToUtf8Bytes(exercises, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var image = Webp();
        var images = withImage
            ? new[] { new { exerciseId = "pendlay-row", slot = 0, path = options.ImagePathOverride.Length > 0 ? options.ImagePathOverride : "images/pendlay-row/0.webp", contentType = options.ImageType, source = "custom", size = image.Length, sha256 = Hash(image) } }
            : [];
        var manifest = new
        {
            schemaVersion = 1, source = options.Source, exportedAtUtc = DateTimeOffset.UtcNow, exerciseCount = exercises.Length,
            imageCount = images.Length, attribution = "Exercises created in TRAZO", exercisesSha256 = options.ExercisesHash ?? Hash(exerciseBytes), images
        };
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Write(zip, "exercises.json", exerciseBytes);
            if (withImage && !options.OmitImage) Write(zip, images[0].path, image);
        }
        stream.Position = 0;
        return stream;
    }

    private static void Write(ZipArchive zip, string path, byte[] bytes)
    {
        using var entry = zip.CreateEntry(path).Open();
        entry.Write(bytes);
    }

    [Fact]
    public void Read_AcceptsAValidArchiveAndReturnsExercisesAndImages()
    {
        var content = TrazoLibraryArchive.Read(Archive());

        var exercise = Assert.Single(content.Exercises);
        Assert.Equal("pendlay-row", exercise.Id);
        Assert.Equal("HorizontalPull", exercise.MovementPattern);
        var image = Assert.Single(content.Images);
        Assert.Equal(("pendlay-row", 0, "image/webp"), (image.ExerciseId, image.Slot, image.ContentType));
        Assert.Equal(40, image.Data.Length);
    }

    [Fact]
    public void Read_RejectsAnythingThatDoesNotMatchTheManifest()
    {
        Assert.Throws<InvalidDataException>(() => TrazoLibraryArchive.Read(new MemoryStream("not a zip"u8.ToArray())));
        Assert.Throws<InvalidDataException>(() => TrazoLibraryArchive.Read(Archive(options: new Options(Source: "DietoExpress"))));
        Assert.Throws<InvalidDataException>(() => TrazoLibraryArchive.Read(Archive(options: new Options(ExercisesHash: new string('0', 64)))));
        Assert.Throws<InvalidDataException>(() => TrazoLibraryArchive.Read(Archive(options: new Options(OmitImage: true))));
        Assert.Throws<InvalidDataException>(() => TrazoLibraryArchive.Read(Archive(options: new Options(ImageType: "image/png"))));
        Assert.Throws<InvalidDataException>(() => TrazoLibraryArchive.Read(Archive(options: new Options(ImagePathOverride: "images/other/0.webp"))));
    }

    [Fact]
    public void Read_AppliesTheSameRulesAsTheCuratedAdditions()
    {
        Assert.Throws<InvalidDataException>(() => TrazoLibraryArchive.Read(Archive([Exercise(instructions: ["Solo un paso."])], withImage: false)));
        Assert.Throws<InvalidDataException>(() => TrazoLibraryArchive.Read(Archive([Exercise(pattern: "Cardio")], withImage: false)));
        Assert.Throws<InvalidDataException>(() => TrazoLibraryArchive.Read(Archive([Exercise("Bad_Id")], withImage: false)));
        Assert.Throws<InvalidDataException>(() => TrazoLibraryArchive.Read(Archive([Exercise(), Exercise()], withImage: false)));
    }

    [Fact]
    public void Controller_IsAServerToServerEndpointProtectedByTheSharedKey()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "TrazoLibraryImportController.cs"));

        Assert.Contains("[AllowAnonymous]", source);
        Assert.Contains("TrazoTransfer:ApiKey", source);
        Assert.Contains("CryptographicOperations.FixedTimeEquals", source);
        Assert.Contains("RequestSizeLimit(TrazoLibraryArchive.MaximumArchiveBytes)", source);
        Assert.Contains("StatusCodes.Status503ServiceUnavailable", source);
        Assert.True(source.IndexOf("FixedTimeEquals", StringComparison.Ordinal) < source.IndexOf("TrazoLibraryArchive.Read", StringComparison.Ordinal),
            "The key must be checked before the archive is read.");
    }

    [Fact]
    public void Seeder_StillUsesDietoExpressLabelsByDefault_AndLabelsTransfersSeparately()
    {
        Assert.Equal("DietoExpress curated additions v", ExerciseCatalogAdditionsSeeder.DefaultOptions.ReviewerPrefix);
        Assert.Equal("DietoExpress", ExerciseCatalogAdditionsSeeder.DefaultOptions.Origin);
        var source = File.ReadAllText(Path.Combine(RepoRoot, "Anguloso.Server", "Controllers", "TrazoLibraryImportController.cs"));
        Assert.Contains("ReviewerPrefix: \"TRAZO transfer v\"", source);
    }
}
