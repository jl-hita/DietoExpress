using Anguloso.Server.Models;
using Anguloso.Server.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Anguloso.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/food-exchange-groups")]
public class FoodExchangeGroupController : ControllerBase
{
    private readonly angulosodbContext _context;

    public FoodExchangeGroupController(angulosodbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<FoodExchangeGroupDto>>> GetGroups()
    {
        var groups = await _context.food_exchange_groups
            .OrderBy(g => g.id)
            .Select(g => new FoodExchangeGroupDto
            {
                Id = g.id,
                Name = g.name,
                Kcal = g.kcal,
                Protein = g.protein,
                Carbs = g.carbs,
                Fat = g.fat
            })
            .ToListAsync();

        return Ok(groups);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<FoodExchangeGroupDto>> GetGroup(int id)
    {
        var g = await _context.food_exchange_groups.FindAsync(id);
        if (g == null) return NotFound();

        return Ok(new FoodExchangeGroupDto
        {
            Id = g.id,
            Name = g.name,
            Kcal = g.kcal,
            Protein = g.protein,
            Carbs = g.carbs,
            Fat = g.fat
        });
    }

    [HttpGet("{id:int}/foods")]
    public async Task<ActionResult> GetFoodsInGroup(int id)
    {
        var groupExists = await _context.food_exchange_groups.AnyAsync(g => g.id == id);
        if (!groupExists) return NotFound("Grupo de intercambio no encontrado.");

        var list = await _context.foods
            .Where(f => f.exchange_group_id == id && f.grams_per_exchange.HasValue)
            .OrderBy(f => f.name)
            .Select(f => new
            {
                Id = f.id,
                Name = f.name,
                GramsPerExchange = f.grams_per_exchange,
                Kcal = f.kcal,
                Protein = f.protein,
                Carbs = f.carbs,
                Fat = f.fat
            })
            .ToListAsync();

        return Ok(list);
    }
}
