using Anguloso.Server.Logica;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anguloso.Server.Controllers;

[ApiController]
[Route("api/legal-governance")]
[Authorize(Policy = "Professional")]
public sealed class LegalGovernanceController : ControllerBase
{
    private readonly LegalGovernanceService _service;
    public LegalGovernanceController(LegalGovernanceService service)=>_service=service;

    [HttpGet("retention")]
    public async Task<ActionResult<IReadOnlyList<LegalRetentionPolicyDto>>> Retention(CancellationToken ct)
        => Ok(await _service.ListRetentionAsync(ct));

    [HttpPut("retention")]
    public async Task<IActionResult> SaveRetention([FromBody] SaveLegalRetentionPolicy request,CancellationToken ct)
    {
        try { await _service.UpsertRetentionAsync(request,ct); return NoContent(); }
        catch(ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet("rat")]
    public async Task<IActionResult> Rat(CancellationToken ct)=>Ok(await _service.ListRatAsync(ct));
    [HttpPost("rat")]
    public async Task<IActionResult> CreateRat([FromBody] SaveLegalRatActivity request,CancellationToken ct)=>await Run(async()=>Ok(new{id=await _service.CreateRatAsync(request,ct)}));
    [HttpPut("rat/{id:long}")]
    public async Task<IActionResult> UpdateRat(long id,[FromBody] SaveLegalRatActivity request,CancellationToken ct)=>await Run(async()=>await _service.UpdateRatAsync(id,request,ct)?NoContent():NotFound());
    [HttpDelete("rat/{id:long}")]
    public async Task<IActionResult> DeleteRat(long id,CancellationToken ct)=>await Run(async()=>await _service.DeleteRatAsync(id,ct)?NoContent():NotFound());

    [HttpGet("risks")]
    public async Task<IActionResult> Risks(CancellationToken ct)=>Ok(await _service.ListRisksAsync(ct));
    [HttpPost("risks")]
    public async Task<IActionResult> CreateRisk([FromBody] SaveLegalRisk request,CancellationToken ct)=>await Run(async()=>Ok(new{id=await _service.CreateRiskAsync(request,ct)}));
    [HttpPut("risks/{id:long}")]
    public async Task<IActionResult> UpdateRisk(long id,[FromBody] SaveLegalRisk request,CancellationToken ct)=>await Run(async()=>await _service.UpdateRiskAsync(id,request,ct)?NoContent():NotFound());

    [HttpGet("eipd")]
    public async Task<IActionResult> Eipd(CancellationToken ct)=>Ok(await _service.ListEipdAsync(ct));
    [HttpPost("eipd")]
    public async Task<IActionResult> CreateEipd([FromBody] SaveLegalEipd request,CancellationToken ct)=>await Run(async()=>Ok(new{id=await _service.CreateEipdAsync(request,ct)}));

    private static async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    { try{return await action();}catch(ArgumentException ex){return new BadRequestObjectResult(ex.Message);} }
}