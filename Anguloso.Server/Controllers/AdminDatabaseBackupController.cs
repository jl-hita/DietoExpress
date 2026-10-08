using Anguloso.Server.Logica;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/admin/database-backups")]
[Authorize(Roles = "superadmin")]
public sealed class AdminDatabaseBackupController : ControllerBase
{
    private readonly DatabaseBackupService _backups;

    public AdminDatabaseBackupController(DatabaseBackupService backups) => _backups = backups;

    [HttpGet]
    public IActionResult List() => Ok(new { configured = _backups.IsConfigured, items = _backups.List() });

    [HttpPost]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        try
        {
            return Ok(await _backups.CreateAsync(ct));
        }
        catch (Exception ex)
        {
            return Problem(title: "No se pudo crear la copia de seguridad", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("{fileName}")]
    public IActionResult Download(string fileName)
    {
        try
        {
            var file = _backups.GetDump(fileName);
            return PhysicalFile(file.Path, file.ContentType, file.FileName, enableRangeProcessing: true);
        }
        catch (FileNotFoundException) { return NotFound(); }
        catch (ArgumentException) { return BadRequest(); }
        catch (InvalidOperationException ex) { return Problem(title: "Copias no configuradas", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
    }
}
