namespace DietoExpress.Security.Tests;

/// <summary>
/// <c>TrainingPeriodizationController</c> está dividido en clases parciales por tema. Estos tests inspeccionan el código
/// fuente, así que leen todas las partes juntas, en el mismo orden que tenía el archivo original, para que las comprobaciones
/// que recortan entre dos métodos sigan funcionando.
/// </summary>
internal static class TrainingControllerSource
{
    private static readonly string[] PartsInOriginalOrder =
    [
        "TrainingPeriodizationController.cs",
        Path.Combine("TrainingPeriodization", "TrainingPeriodizationController.Generation.cs"),
        Path.Combine("TrainingPeriodization", "TrainingPeriodizationController.Templates.cs"),
        Path.Combine("TrainingPeriodization", "TrainingPeriodizationController.Plans.cs"),
        Path.Combine("TrainingPeriodization", "TrainingPeriodizationController.Adaptation.cs"),
        Path.Combine("TrainingPeriodization", "TrainingPeriodizationController.Sessions.cs"),
        Path.Combine("TrainingPeriodization", "TrainingPeriodizationController.Cycles.cs"),
        Path.Combine("TrainingPeriodization", "TrainingPeriodizationController.Collaborations.cs"),
        Path.Combine("TrainingPeriodization", "TrainingPeriodizationController.Support.cs"),
        Path.Combine("TrainingPeriodization", "TrainingPeriodizationDtos.cs"),
    ];

    public static string ReadAll()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
            directory = directory.Parent;
        var controllers = Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio."),
            "Anguloso.Server", "Controllers");
        return string.Join("\n", PartsInOriginalOrder.Select(part => File.ReadAllText(Path.Combine(controllers, part))));
    }

    /// <summary>Todos los archivos de las partes, para comprobar que no se añade código fuera de la lista esperada.</summary>
    public static IEnumerable<string> ExistingPartFiles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
            directory = directory.Parent;
        var controllers = Path.Combine(directory!.FullName, "Anguloso.Server", "Controllers");
        return Directory.GetFiles(Path.Combine(controllers, "TrainingPeriodization"), "*.cs").Select(Path.GetFileName)!;
    }
}
