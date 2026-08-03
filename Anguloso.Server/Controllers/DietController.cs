using Anguloso.Server.Model;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/dietas")]
public class DietController : ControllerBase
{
    private readonly angulosodbContext _context;

    public DietController(angulosodbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<DietListDto>>> GetDiets()
    {
        var list = await _context.diets
            .OrderByDescending(d => d.created_at)
            .Select(d => new DietListDto
            {
                Id = d.id,
                Name = d.name,
                TargetKcal = d.target_kcal,
                TargetProtein = d.target_protein,
                TargetCarbs = d.target_carbs,
                TargetFat = d.target_fat,
                Notes = d.notes,
                CreatedAt = d.created_at
            })
            .ToListAsync();

        return Ok(list);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DietDetailDto>> GetDiet(int id)
    {
        var d = await _context.diets
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
                        .ThenInclude(i => i.food)
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
                        .ThenInclude(i => i.exchange_group)
            .FirstOrDefaultAsync(d => d.id == id);

        if (d == null) return NotFound();

        return Ok(new DietDetailDto
        {
            Id = d.id,
            Name = d.name,
            TargetKcal = d.target_kcal,
            TargetProtein = d.target_protein,
            TargetCarbs = d.target_carbs,
            TargetFat = d.target_fat,
            Notes = d.notes,
            CreatedAt = d.created_at,
            Days = d.diet_days.OrderBy(dd => dd.day_index).Select(dd => new DietDayDto
            {
                Id = dd.id,
                DayIndex = dd.day_index,
                Meals = dd.meals.OrderBy(m => m.meal_index).Select(m => new MealDto
                {
                    Id = m.id,
                    Name = m.name,
                    MealIndex = m.meal_index,
                    Items = m.meal_items.Select(i => new MealItemDto
                    {
                        Id = i.id,
                        FoodId = i.food_id,
                        Grams = i.grams,
                        Kcal = i.kcal,
                        Protein = i.protein,
                        Carbs = i.carbs,
                        Fat = i.fat,
                        FoodName = i.food != null ? i.food.name : null,
                        ExchangeGroupId = i.exchange_group_id,
                        ExchangeGroupName = i.exchange_group != null ? i.exchange_group.name : null,
                        ExchangeCount = i.exchange_count
                    }).ToList()
                }).ToList()
            }).ToList()
        });
    }

    [HttpPost]
    public async Task<ActionResult<DietListDto>> CreateDiet([FromBody] CreateDietDto dto)
    {
        var diet = new diets
        {
            name = dto.Name,
            target_kcal = dto.TargetKcal,
            target_protein = dto.TargetProtein,
            target_carbs = dto.TargetCarbs,
            target_fat = dto.TargetFat,
            notes = dto.Notes ?? "",
            created_at = DateTime.UtcNow
        };

        if (dto.Days != null)
        {
            foreach (var dayDto in dto.Days)
            {
                var day = new diet_days { day_index = dayDto.DayIndex };
                foreach (var mealDto in dayDto.Meals)
                {
                    var meal = new meals { name = mealDto.Name, meal_index = mealDto.MealIndex };
                    foreach (var itemDto in mealDto.Items)
                    {
                        meal.meal_items.Add(new meal_items
                        {
                            food_id = itemDto.FoodId,
                            grams = itemDto.Grams,
                            kcal = itemDto.Kcal,
                            protein = itemDto.Protein,
                            carbs = itemDto.Carbs,
                            fat = itemDto.Fat,
                            exchange_group_id = itemDto.ExchangeGroupId,
                            exchange_count = itemDto.ExchangeCount
                        });
                    }
                    day.meals.Add(meal);
                }
                diet.diet_days.Add(day);
            }
        }

        _context.diets.Add(diet);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetDiet), new { id = diet.id }, new DietListDto
        {
            Id = diet.id,
            Name = diet.name,
            CreatedAt = diet.created_at
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateDiet(int id, [FromBody] UpdateDietDto dto)
    {
        var diet = await _context.diets
            .Include(d => d.diet_days)
                .ThenInclude(dd => dd.meals)
                    .ThenInclude(m => m.meal_items)
            .FirstOrDefaultAsync(d => d.id == id);

        if (diet == null) return NotFound();

        diet.name = dto.Name;
        diet.target_kcal = dto.TargetKcal;
        diet.target_protein = dto.TargetProtein;
        diet.target_carbs = dto.TargetCarbs;
        diet.target_fat = dto.TargetFat;
        diet.notes = dto.Notes ?? diet.notes ?? "";

        // Remover todo el árbol anterior
        _context.diet_days.RemoveRange(diet.diet_days);
        diet.diet_days.Clear();

        // Construir nuevo árbol
        if (dto.Days != null)
        {
            foreach (var dayDto in dto.Days)
            {
                var day = new diet_days { day_index = dayDto.DayIndex };
                foreach (var mealDto in dayDto.Meals)
                {
                    var meal = new meals { name = mealDto.Name, meal_index = mealDto.MealIndex };
                    foreach (var itemDto in mealDto.Items)
                    {
                        meal.meal_items.Add(new meal_items
                        {
                            food_id = itemDto.FoodId,
                            grams = itemDto.Grams,
                            kcal = itemDto.Kcal,
                            protein = itemDto.Protein,
                            carbs = itemDto.Carbs,
                            fat = itemDto.Fat,
                            exchange_group_id = itemDto.ExchangeGroupId,
                            exchange_count = itemDto.ExchangeCount
                        });
                    }
                    day.meals.Add(meal);
                }
                diet.diet_days.Add(day);
            }
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteDiet(int id)
    {
        var diet = await _context.diets
            .Include(d => d.diet_days)
            .FirstOrDefaultAsync(d => d.id == id);
        if (diet == null) return NotFound();

        _context.diet_days.RemoveRange(diet.diet_days);
        _context.diets.Remove(diet);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
