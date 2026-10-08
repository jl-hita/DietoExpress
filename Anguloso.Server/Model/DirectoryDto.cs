namespace Anguloso.Server.Model;

public class DirectoryProfileDto
{
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
}

public class DirectorySearchDto
{
    public string? City { get; set; }
    public string? Province { get; set; }
    public bool? Online { get; set; }
    public string? Speciality { get; set; }
}
