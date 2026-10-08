namespace Anguloso.Server.Model;

public class DirectoryProfileDto
{
    public int NutritionistId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string ClinicName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string ClinicLogo { get; set; } = string.Empty;
    public string PublicBio { get; set; } = string.Empty;
    public string Specialties { get; set; } = string.Empty;
    public bool OnlineConsultations { get; set; }
    public string[] SpecialtyList { get; set; } = Array.Empty<string>();
    public int ProfileCompleteness { get; set; }
    public int RankingScore { get; set; }
    public bool IsVerified { get; set; }
    public int AvailableRuleCount { get; set; }
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
}

public class DirectorySearchDto
{
    public string? City { get; set; }
    public string? Province { get; set; }
    public bool? Online { get; set; }
    public string? Speciality { get; set; }
    public string? Goal { get; set; }
    public bool? AvailableOnly { get; set; }
}

public class DirectoryReviewDto
{
    public long Id { get; set; }
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool Verified { get; set; } = true;
}

public class DirectoryReviewSummaryDto
{
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public List<DirectoryReviewDto> Reviews { get; set; } = new();
}

public class DirectoryReviewRequestDto
{
    public int AppointmentId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
}
