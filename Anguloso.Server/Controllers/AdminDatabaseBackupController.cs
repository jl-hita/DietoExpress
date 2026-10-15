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

    [HttpGet("drive")]
    public async Task<IActionResult> ListDrive(CancellationToken ct)
    {
        try { return Ok(new { configured = true, items = await _backups.ListDriveAsync(ct) }); }
        catch (InvalidOperationException ex) { return Ok(new { configured = false, items = Array.Empty<DriveDatabaseBackupInfo>(), error = ex.Message }); }
    }

    [HttpPost("drive/{archiveName}/restore")]
    public async Task<IActionResult> RestoreFromDrive(string archiveName, CancellationToken ct)
    {
        try { var result = await _backups.RestoreFromDriveAsync(archiveName, ct); return Accepted(new { fileName = result.FileName, restored = false, started = true, output = result.Output }); }
        catch (ArgumentException) { return BadRequest(); }
        catch (InvalidOperationException ex) { return Problem(title: "No se pudo restaurar desde Google Drive", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError); }
    }

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

    [HttpPost("{fileName}/verify")]
    public async Task<IActionResult> Verify(string fileName, CancellationToken ct)
    {
        try
        {
            var output = await _backups.VerifyAsync(fileName, ct);
            return Ok(new { fileName, verified = true, output });
        }
        catch (FileNotFoundException) { return NotFound(); }
        catch (ArgumentException) { return BadRequest(); }
        catch (InvalidOperationException ex) { return Problem(title: "No se pudo verificar el backup", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError); }
    }

    [HttpPost("{fileName}/restore")]
    public async Task<IActionResult> Restore(string fileName, CancellationToken ct)
    {
        try
        {
            var output = await _backups.RestoreAsync(fileName, ct);
            return Accepted(new { fileName, restored = false, started = true, output });
        }
        catch (FileNotFoundException) { return NotFound(); }
        catch (ArgumentException) { return BadRequest(); }
        catch (InvalidOperationException ex) { return Problem(title: "No se pudo restaurar el backup", detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError); }
    }

    [HttpGet("{fileName}/restore-status")]
    public async Task<IActionResult> RestoreStatus(string fileName, CancellationToken ct)
    {
        try { return Ok(await _backups.GetRestoreStatusAsync(fileName, ct)); }
        catch (ArgumentException) { return BadRequest(); }
        catch (InvalidOperationException ex) { return Problem(title: "No se pudo consultar la restauración", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
    }

    [HttpGet("{fileName}")]
    public IActionResult Download(string fileName)
    {
        try
        {
            var file = _backups.GetBundle(fileName);
            return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: true);
        }
        catch (FileNotFoundException) { return NotFound(); }
        catch (ArgumentException) { return BadRequest(); }
        catch (InvalidOperationException ex) { return Problem(title: "Copias no configuradas", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable); }
    }
}
