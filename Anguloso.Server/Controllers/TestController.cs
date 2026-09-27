using Anguloso.Server.Logica;
using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("[controller]")]
public class TestController : ControllerBase
{
    private readonly LogServ _logServ;
    private readonly angulosodbContext _dbContext;

    public TestController(LogServ logServ, angulosodbContext dbContext)
    {
        _logServ = logServ;
        _dbContext = dbContext;
    }

    [HttpGet("logTest")]
    public BoolMensaje LogTest([FromQuery] string log)
    {
        try
        {
            _logServ.LogInfo("Esto es Info");
            _logServ.LogWarning("Esto es Warning");
            _logServ.LogError("Esto es Error");
            _logServ.LogInfo($"Log pasado en parámetro -> {log}");
            return new BoolMensaje
            {
                Exito = true,
                Mensaje = "Test de log ejecutado correctamente."
            };
        }
        catch (Exception e)
        {
            _logServ.LogError($"Excepción en TestController.LogTest -> {e.Message}");
            return new BoolMensaje
            {
                Exito = false,
                Mensaje = "Error ejecutando el test de log."
            };
        }
    }

    [HttpGet("BEDCAImporter")]
    public async Task<IActionResult> BEDCAImporter()
    {
        try
        {
            var bedcaClient = new BEDCAClient(new HttpClient(), _logServ, _dbContext);
            return Ok(await bedcaClient.Importador());
        }
        catch (Exception e)
        {
            _logServ.LogError($"Excepción en TestController.BEDCATest -> {e.Message}");
            return BadRequest("Error ejecutando el importador BEDCA.");
        }
    }
}
