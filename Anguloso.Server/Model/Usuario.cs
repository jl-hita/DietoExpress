namespace Anguloso.Server.Model;

public class Usuario
{
    public string? Username { get; set; }

    public string? FullName { get; set; }

    public string? PasswordPlain { get; set; }
    public string? Email { get; set; }
    public string? LegalDocumentKey { get; set; }
    public int? LegalDocumentVersion { get; set; }
    public string? LegalDocumentSha256 { get; set; }
}
