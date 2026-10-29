using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/client-diagnostics")]
[Authorize]
[RequestSizeLimit(16_384)]
public sealed class ClientDiagnosticsController(ILogger<ClientDiagnosticsController> logger) : ControllerBase
{
    [HttpPost("runtime-error")]
    public IActionResult RuntimeError([FromBody] ClientRuntimeDiagnosticRequest request)
    {
        var eventId = SafeValue(request.EventId, 80);
        var path = SafePath(request.Path);
        var errorName = SafeValue(request.ErrorName, 100);
        var errorMessage = SafeDiagnosticText(request.ErrorMessage, 500);
        var stackTrace = string.Join(" | ", (request.StackTrace ?? string.Empty)
            .Split('\n')
            .Take(20)
            .Select(line => SafeDiagnosticText(line, 300)));

        logger.LogError(
            "Client runtime diagnostic {ClientEventId} occurred at {ClientOccurredAtUtc} on {ClientPath}: {ClientErrorName}: {ClientErrorMessage}. Stack: {ClientStackTrace}",
            eventId,
            SafeValue(request.OccurredAtUtc, 40),
            path,
            errorName,
            errorMessage,
            stackTrace);

        return NoContent();
    }

    private static string SafePath(string? value)
    {
        var path = (value ?? "/").Split('?', '#')[0];
        path = Regex.Replace(path, @"/(?:\d+|[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12})(?=/|$)", "/:id", RegexOptions.IgnoreCase);
        return path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal)
            ? path[..Math.Min(path.Length, 300)]
            : "/";
    }

    private static string SafeValue(string? value, int maxLength)
    {
        var cleaned = Regex.Replace(value ?? string.Empty, @"[\r\n\t]+", " ").Trim();
        return cleaned[..Math.Min(cleaned.Length, maxLength)];
    }

    private static string SafeDiagnosticText(string? value, int maxLength)
    {
        var cleaned = (value ?? string.Empty)
            .Replace("Bearer ", "Bearer [redacted]", StringComparison.OrdinalIgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[\r\t]+", " ");
        cleaned = Regex.Replace(cleaned, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", "[email]", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\b\d{7,}\b", "[number]");
        cleaned = Regex.Replace(cleaned, @"(?i)(token|password|authorization|cookie)\s*[:=]\s*\S+", "$1=[redacted]");
        cleaned = Regex.Replace(cleaned, @"https?://[^\s)]+", match => match.Value.Split('?', '#')[0], RegexOptions.IgnoreCase);
        return cleaned[..Math.Min(cleaned.Length, maxLength)];
    }
}

public sealed class ClientRuntimeDiagnosticRequest
{
    [Required, StringLength(80)]
    public string EventId { get; init; } = string.Empty;

    [Required, StringLength(300)]
    public string Path { get; init; } = "/";

    [Required, StringLength(40)]
    public string OccurredAtUtc { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string ErrorName { get; init; } = string.Empty;

    [Required, StringLength(500)]
    public string ErrorMessage { get; init; } = string.Empty;

    [StringLength(6000)]
    public string StackTrace { get; init; } = string.Empty;
}
