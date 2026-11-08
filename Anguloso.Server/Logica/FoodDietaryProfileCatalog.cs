using Anguloso.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Logica;

/// <summary>
/// Instantánea en memoria de <c>food_dietary_profile</c> (apto vegano / vegetariano por alimento). Es la fuente de
/// verdad para las dietas veganas y vegetarianas; solo si un alimento no tiene perfil se recurre a la heurística por
/// nombre. Se recarga al arrancar, tras aplicar el seed de curación. Lectura sin bloqueos (se sustituye la referencia).
/// </summary>
public static class FoodDietaryProfileCatalog
{
    public readonly record struct Profile(bool IsVegan, bool IsVegetarian);

    private static IReadOnlyDictionary<int, Profile> _profiles = new Dictionary<int, Profile>();

    public static int Count => _profiles.Count;

    public static bool TryGet(int foodId, out Profile profile) => _profiles.TryGetValue(foodId, out profile);

    public static async Task ReloadAsync(angulosodbContext context, CancellationToken cancellationToken = default)
    {
        _profiles = await context.food_dietary_profile.AsNoTracking()
            .ToDictionaryAsync(p => p.food_id, p => new Profile(p.is_vegan, p.is_vegetarian), cancellationToken);
    }

    /// <summary>Solo para pruebas: sustituye la instantánea.</summary>
    public static void SetForTests(IReadOnlyDictionary<int, Profile> profiles) => _profiles = profiles;
}
