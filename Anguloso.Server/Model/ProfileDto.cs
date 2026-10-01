using System.ComponentModel.DataAnnotations;

namespace Anguloso.Server.Model;

public class ProfileDto
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;
    [StringLength(150)]
    public string ClinicName { get; set; } = string.Empty;
    [StringLength(250)]
    public string ClinicAddress { get; set; } = string.Empty;
    [StringLength(50)]
    public string ClinicPhone { get; set; } = string.Empty;
    [StringLength(1_000_000)]
    public string ClinicLogo { get; set; } = string.Empty;
}

public class UpdateProfileDto
{
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;
    [StringLength(150)]
    public string ClinicName { get; set; } = string.Empty;
    [StringLength(250)]
    public string ClinicAddress { get; set; } = string.Empty;
    [StringLength(50)]
    public string ClinicPhone { get; set; } = string.Empty;
    [StringLength(1_000_000)]
    public string ClinicLogo { get; set; } = string.Empty;
}
