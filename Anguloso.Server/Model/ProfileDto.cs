namespace Anguloso.Server.Model;

public class ProfileDto
{
    public string Username { get; set; }
    public string Email { get; set; }
    public string FullName { get; set; }
    public string ClinicName { get; set; }
    public string ClinicAddress { get; set; }
    public string ClinicPhone { get; set; }
    public string ClinicLogo { get; set; }
}

public class UpdateProfileDto
{
    public string FullName { get; set; }
    public string ClinicName { get; set; }
    public string ClinicAddress { get; set; }
    public string ClinicPhone { get; set; }
    public string ClinicLogo { get; set; }
}
