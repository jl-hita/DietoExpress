using Microsoft.Extensions.Caching.Memory;

namespace Anguloso.Server.Logica;

/// <summary>
/// Caché en memoria de corta duración para respuestas públicas y anónimas (directorio, sitemap). Tiene un tope de entradas
/// para que un atacante que varíe los filtros no pueda llenar la memoria, y solo debe usarse con datos que no dependen del usuario.
/// </summary>
public sealed class PublicResponseCache : IDisposable
{
    private const int MaxEntries = 500;
    private const int MaxKeyLength = 512;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = MaxEntries });

    /// <summary>
    /// Devuelve el valor cacheado o lo calcula. Un resultado nulo o una excepción no se guardan. Las peticiones simultáneas
    /// para la misma clave comparten un único cálculo.
    /// </summary>
    public async Task<T?> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<Task<T?>> factory) where T : class
    {
        if (key.Length > MaxKeyLength) return await factory();

        var lazy = _cache.GetOrCreate(key, entry =>
        {
            entry.Size = 1;
            entry.AbsoluteExpirationRelativeToNow = ttl;
            return new Lazy<Task<object?>>(async () => (object?)await factory(), LazyThreadSafetyMode.ExecutionAndPublication);
        })!;

        try
        {
            var value = await lazy.Value;
            if (value is null) _cache.Remove(key);
            return (T?)value;
        }
        catch
        {
            _cache.Remove(key);
            throw;
        }
    }

    public void Dispose() => _cache.Dispose();
}
