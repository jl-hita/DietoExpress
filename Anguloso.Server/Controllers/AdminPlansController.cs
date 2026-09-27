using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Anguloso.Server.Controllers;
[ApiController]
[Route("api/admin/plans")]
[Authorize(Roles="superadmin")]
public class AdminPlansController : ControllerBase
{
 private readonly angulosodbContext _context; public AdminPlansController(angulosodbContext context)=>_context=context;
 [HttpGet] public async Task<IActionResult> GetAll()=>Ok(await _context.subscription_plans.AsNoTracking().Include(p=>p.features).OrderBy(p=>p.id).ToListAsync());
 [HttpPost] public async Task<IActionResult> Create([FromBody] PlanRequest r){if(string.IsNullOrWhiteSpace(r.Code)||string.IsNullOrWhiteSpace(r.Name))return BadRequest();if(await _context.subscription_plans.AnyAsync(p=>p.code==r.Code))return Conflict("El código ya existe.");var p=new subscription_plans{code=r.Code.Trim().ToLowerInvariant(),name=r.Name,description=r.Description,monthly_price=r.MonthlyPrice,yearly_price=r.YearlyPrice,max_nutritionists=r.MaxNutritionists,max_clients_per_nutritionist=r.MaxClientsPerNutritionist,max_total_clients=r.MaxTotalClients,trial_days=r.TrialDays,active=r.Active};_context.subscription_plans.Add(p);await _context.SaveChangesAsync();return Ok(p);}
 [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id,[FromBody] PlanRequest r){var p=await _context.subscription_plans.FindAsync(id);if(p==null)return NotFound();p.name=r.Name;p.description=r.Description;p.monthly_price=r.MonthlyPrice;p.yearly_price=r.YearlyPrice;p.max_nutritionists=r.MaxNutritionists;p.max_clients_per_nutritionist=r.MaxClientsPerNutritionist;p.max_total_clients=r.MaxTotalClients;p.trial_days=r.TrialDays;p.active=r.Active;await _context.SaveChangesAsync();return Ok(p);}
 [HttpPut("{id:int}/features")] public async Task<IActionResult> Features(int id,[FromBody] List<FeatureRequest> features){var p=await _context.subscription_plans.FindAsync(id);if(p==null)return NotFound();var old=await _context.subscription_plan_features.Where(f=>f.plan_id==id).ToListAsync();_context.subscription_plan_features.RemoveRange(old);_context.subscription_plan_features.AddRange(features.Select(f=>new subscription_plan_features{plan_id=id,feature_code=f.FeatureCode,enabled=f.Enabled}));await _context.SaveChangesAsync();return Ok();}
}
public record PlanRequest(string Code,string Name,string? Description,decimal MonthlyPrice,decimal YearlyPrice,int? MaxNutritionists,int? MaxClientsPerNutritionist,int? MaxTotalClients,int? TrialDays,bool Active);
public record FeatureRequest(string FeatureCode,bool Enabled);