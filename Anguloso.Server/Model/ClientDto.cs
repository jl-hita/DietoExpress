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
}

public class CreateClientDto
{
    [Required] public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; } // accept ISO date
    public string? Notes { get; set; }
}

public class UpdateClientDto
{
    [Required] public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? Notes { get; set; }
}
