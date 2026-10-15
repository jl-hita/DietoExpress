namespace Anguloso.Server.Model;

public class PatientProfileDto
{
    public int ClientId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public double? CurrentWeight { get; set; }
    public double? CurrentHeight { get; set; }
    public bool HasActiveDiet { get; set; }
    public int? ActiveDietAssignmentId { get; set; }
    public DateTime? ActiveDietStartDate { get; set; }
    public List<WeightEntryDto> WeightHistory { get; set; } = new();

    // Branding de la clínica / nutricionista
    public string? ClinicName { get; set; }
    public string? ClinicPhone { get; set; }
    public string? ClinicLogo { get; set; }
    public string? NutritionistName { get; set; }
}

public class PatientAuthRequestDto
{
    public string? Token { get; set; }
    public string? EmailOrPhone { get; set; }
    public string? Passcode { get; set; }
}

public class PatientAuthResponseDto
{
    public string? Token { get; set; }
    public int ClientId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? ClinicName { get; set; }
    public string? ClinicLogo { get; set; }
}

public class SetClientPasscodeDto
{
    public string Passcode { get; set; } = string.Empty;
}

public class ClientPortalAccessDto
{
    public int ClientId { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string MagicLink { get; set; } = string.Empty;
    public bool HasPasscode { get; set; }
    public DateTime? LastPortalAccess { get; set; }
}

public class WeightEntryDto
{
    public DateTime Date { get; set; }
    public double Weight { get; set; }
}

public class ShoppingCategoryDto
{
    public string Category { get; set; } = string.Empty;
    public List<ShoppingItemDto> Items { get; set; } = new();
}

public class ShoppingItemDto
{
    public int FoodId { get; set; }
    public string FoodName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public double TotalGrams { get; set; }
    public double RoundedGrams { get; set; }
    public string CommercialDescription { get; set; } = string.Empty;
}


public class PatientAccessLinkRequestDto
{
    public string? Email { get; set; }
}

    
public class PatientOnboardingRequestDto
{
    public DateTime? BirthDate { get; set; }
    public string? Gender { get; set; }
    public double? Weight { get; set; }
    public double? Height { get; set; }
    public bool AcceptConsent { get; set; }
    public string? ConsentVersion { get; set; }
}
