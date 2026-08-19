namespace Anguloso.Server.Model;

using System.ComponentModel.DataAnnotations;

public class ClientListDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; } // ISO date (yyyy-MM-dd) in JSON
    public DateTime? CreatedAt { get; set; }
}

public class ClientDetailDto : ClientListDto
{
    public string? Notes { get; set; }
    public List<BiometricsDto> Biometrics { get; set; } = new();
    public MedicalHistoryDto? MedicalHistory { get; set; }
    public DigestiveHealthDto? DigestiveHealth { get; set; }
    public FoodPreferencesDto? FoodPreferences { get; set; }
    public LifestyleHistoryDto? LifestyleHistory { get; set; }
}

public class CreateClientDto
{
    [Required] public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; } // accept ISO date
    public string? Notes { get; set; }
    public MedicalHistoryDto? MedicalHistory { get; set; }
    public DigestiveHealthDto? DigestiveHealth { get; set; }
    public FoodPreferencesDto? FoodPreferences { get; set; }
    public LifestyleHistoryDto? LifestyleHistory { get; set; }
}

public class UpdateClientDto
{
    [Required] public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? Notes { get; set; }
    public MedicalHistoryDto? MedicalHistory { get; set; }
    public DigestiveHealthDto? DigestiveHealth { get; set; }
    public FoodPreferencesDto? FoodPreferences { get; set; }
    public LifestyleHistoryDto? LifestyleHistory { get; set; }
}

public class MedicalHistoryDto
{
    public int Id { get; set; }
    public bool Diabetes { get; set; }
    public bool Hypertension { get; set; }
    public bool Hypothyroidism { get; set; }
    public string? Surgeries { get; set; }
    public string? RoutineMedication { get; set; }
    public string? OtherPathologies { get; set; }
}

public class DigestiveHealthDto
{
    public int Id { get; set; }
    public string? IntestinalHabits { get; set; }
    public bool Bloating { get; set; }
    public bool Heartburn { get; set; }
    public bool GlutenIntolerance { get; set; }
    public bool LactoseIntolerance { get; set; }
    public bool FodmapsIntolerance { get; set; }
    public string? OtherIntolerances { get; set; }
    public string? Notes { get; set; }
}

public class FoodPreferencesDto
{
    public int Id { get; set; }
    public string? PreferredFoods { get; set; }
    public string? DislikedFoods { get; set; }
    public string? Allergies { get; set; }
}

public class LifestyleHistoryDto
{
    public int Id { get; set; }
    public string? WorkSchedule { get; set; }
    public string? SleepHabits { get; set; }
    public string? WaterConsumption { get; set; }
    public string? AlcoholConsumption { get; set; }
    public string? TobaccoConsumption { get; set; }
}
