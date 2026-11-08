using Anguloso.Server.Logica;
using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class ExerciseImageServiceTests
{
    [Fact]
    public void RepDbSlotsMapStartOrMainToZeroAndPeakToOne()
    {
        var two = ExerciseImageService.ResolveRepDbSlotPaths("""{"flat":{"start":"images/flat/db-fly-start.webp","peak":"images/flat/db-fly-peak.webp"}}""");
        Assert.Equal(["images/flat/db-fly-start.webp", "images/flat/db-fly-peak.webp"], two.OrderBy(x => x.Key).Select(x => x.Value));
        var single = ExerciseImageService.ResolveRepDbSlotPaths("""{"flat":{"main":"images/flat/side-plank-main.webp"}}""");
        Assert.Equal(new[] { 0 }, single.Keys.ToArray());
    }

    [Theory]
    [InlineData("""{"flat":{"start":"https://evil.example/x.webp"}}""")]
    [InlineData("""{"flat":{"start":"../../etc/passwd.webp"}}""")]
    [InlineData("""{"flat":{"start":"images/x.exe"}}""")]
    [InlineData("""{"flat":{"peak":"images/only-peak.webp"}}""")]
    [InlineData("not json")]
    [InlineData("")]
    public void UnsafeOrIncompletePathsNeverProduceASlot(string json) =>
        Assert.Empty(ExerciseImageService.ResolveRepDbSlotPaths(json));

    [Fact]
    public void ContentTypeIsDetectedFromMagicBytesOnly()
    {
        var webp = "RIFF\0\0\0\0WEBPVP8 "u8.ToArray();
        Assert.Equal("image/webp", ExerciseImageService.DetectImageContentType(webp));
        Assert.Equal("image/png", ExerciseImageService.DetectImageContentType([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
        Assert.Equal("image/jpeg", ExerciseImageService.DetectImageContentType([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Null(ExerciseImageService.DetectImageContentType("<svg onload=alert(1)>"u8));
        Assert.Null(ExerciseImageService.DetectImageContentType("<html>"u8));
        Assert.Null(ExerciseImageService.DetectImageContentType([]));
    }

    [Fact]
    public void ImagesAreOnlyServedToAuthenticatedUsersAndNeverListedInBulk()
    {
        var root = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(root);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Anguloso.sln"))) dir = dir.Parent;
        var portal = File.ReadAllText(Path.Combine(dir!.FullName, "Anguloso.Server", "Controllers", "TrainingPortalController.cs"));
        Assert.Contains("[Authorize(Roles = \"patient\")]", portal);
        Assert.Contains("exercise_prescription_data @> jsonb_build_object('exercises'", portal);
        var catalog = File.ReadAllText(Path.Combine(dir.FullName, "Anguloso.Server", "Controllers", "ExerciseCatalogController.cs"));
        Assert.Contains("[Authorize(Roles = \"nutritionist,clinic_admin,superadmin\")]", catalog);
        var service = File.ReadAllText(Path.Combine(dir.FullName, "Anguloso.Server", "Logica", "ExerciseImageService.cs"));
        Assert.Contains("RepDbImageHost = \"https://exercise-dataset.com/\"", service);
    }
}

public sealed class ExerciseImageSeedTests
{
    private static string Line(byte[] data, string? sha = null, string source = "repdb-free", int slot = 0, string id = "db-fly") =>
        System.Text.Json.JsonSerializer.Serialize(new ExerciseImageService.SeedRow(
            id, slot, source, sha ?? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant(),
            Convert.ToBase64String(data)));

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    [Fact]
    public void ValidLineIsAccepted()
    {
        Assert.True(ExerciseImageService.TryParseSeedLine(Line(Png), out var row, out var data));
        Assert.Equal("db-fly", row!.ExerciseId);
        Assert.Equal(Png, data);
    }

    [Fact]
    public void ChecksumMismatchUnknownContentBadSourceSlotOrJsonAreRejected()
    {
        Assert.False(ExerciseImageService.TryParseSeedLine(Line(Png, sha: new string('0', 64)), out _, out _));
        Assert.False(ExerciseImageService.TryParseSeedLine(Line("<svg/>"u8.ToArray()), out _, out _));
        Assert.False(ExerciseImageService.TryParseSeedLine(Line(Png, source: "other"), out _, out _));
        Assert.False(ExerciseImageService.TryParseSeedLine(Line(Png, slot: 9), out _, out _));
        Assert.False(ExerciseImageService.TryParseSeedLine("not json", out _, out _));
        Assert.False(ExerciseImageService.TryParseSeedLine("{}", out _, out _));
    }

    [Fact]
    public void SeedFilesAreNeverCommittedAndStartupImportsThemAfterTheCatalogUpsert()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Anguloso.sln"))) dir = dir.Parent;
        var gitignore = File.ReadAllText(Path.Combine(dir!.FullName, ".gitignore"));
        Assert.Contains("*.jsonl.gz", gitignore);
        var program = File.ReadAllText(Path.Combine(dir.FullName, "Anguloso.Server", "Program.cs"));
        Assert.True(program.IndexOf("ExerciseCatalogAdditionsSeeder.UpsertAsync", StringComparison.Ordinal) <
                    program.IndexOf("imageService.ImportSeedAsync", StringComparison.Ordinal));
        Assert.Contains("ApplicationStarted.Register", program);
    }
}
