using Xunit;

namespace DietoExpress.Security.Tests;

public class TrainingControllerStructureTests
{
    [Fact]
    public void EveryPartOfTheSplitControllerIsCoveredByTheSourceInspectionTests()
    {
        // Si se añade un archivo parcial nuevo hay que incluirlo en TrainingControllerSource; si no, los tests de
        // seguridad que leen el código dejarían de ver ese código sin avisar.
        var source = TrainingControllerSource.ReadAll();
        foreach (var file in TrainingControllerSource.ExistingPartFiles())
        {
            var name = Path.GetFileNameWithoutExtension(file!);
            if (name.EndsWith("Dtos", StringComparison.Ordinal)) continue;
            var topic = name["TrainingPeriodizationController.".Length..];
            Assert.Contains("partial class TrainingPeriodizationController", source);
            Assert.Contains(topic, string.Join(",", TrainingControllerSource.ExistingPartFiles()));
        }
        Assert.Equal(9, TrainingControllerSource.ExistingPartFiles().Count());
    }

    [Fact]
    public void NoPartGrowsBackIntoAGodFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
            directory = directory.Parent;
        var controllers = Path.Combine(directory!.FullName, "Anguloso.Server", "Controllers");
        var files = Directory.GetFiles(Path.Combine(controllers, "TrainingPeriodization"), "*.cs")
            .Append(Path.Combine(controllers, "TrainingPeriodizationController.cs"));
        foreach (var file in files)
            Assert.True(File.ReadAllLines(file).Length < 900, $"{Path.GetFileName(file)} supera 900 líneas: divídelo por tema.");
    }
}
