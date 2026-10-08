using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Anguloso.Server.Controllers;

[ApiController]
public class PublicSitemapController : ControllerBase
{
    private readonly angulosodbContext _context;
    private readonly IConfiguration _configuration;

    public PublicSitemapController(angulosodbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    [AllowAnonymous]
    [HttpGet("/sitemap.xml")]
    [Produces("application/xml")]
    public async Task<IActionResult> Sitemap(CancellationToken ct)
    {
        var baseUrl = (_configuration["PLATFORM_FRONTEND_URL"] ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/');
        var published = _context.users.AsNoTracking().Where(u =>
            u.archived_at == null && u.role == "nutritionist" &&
            u.directory_enabled == true && u.directory_publication_status == "published");

        var profiles = await published.Where(u => u.directory_slug != null && u.directory_slug != "")
            .Select(u => u.directory_slug!).Take(5000).ToListAsync(ct);
        var cities = await published.Where(u => u.directory_city != null && u.directory_city != "")
            .GroupBy(u => u.directory_city!).Select(g => new { Value = g.Key, Count = g.Count() })
            .Where(x => x.Count >= 3).Take(100).ToListAsync(ct);
        var citySpecialties = await published
            .Where(u => u.directory_city != null && u.directory_city != "" && u.directory_specialties != null && u.directory_specialties != "")
            .Select(u => new { City = u.directory_city!, Specialties = u.directory_specialties! })
            .Take(10000).ToListAsync(ct);
        var specialties = await published.Where(u => u.directory_specialties != null && u.directory_specialties != "")
            .SelectMany(u => u.directory_specialties!.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(x => x.Trim()).Where(x => x != "").GroupBy(x => x).Select(g => new { Value = g.Key, Count = g.Count() })
            .Where(x => x.Count >= 3).Take(100).ToListAsync(ct);
        var online = await published.CountAsync(u => u.online_consultations == true, ct);

        var urls = new List<string> { "/nutricionistas" };
        if (online >= 3) urls.Add("/nutricionistas/online");
        urls.AddRange(cities.Select(x => "/nutricionistas/ciudad/" + Slugify(x.Value)));
        urls.AddRange(specialties.Select(x => "/nutricionistas/especialidad/" + Slugify(x.Value)));

        // Solo indexamos combinaciones ciudad+especialidad con oferta suficiente para evitar páginas SEO vacías.
        var combinations = citySpecialties
            .SelectMany(x => x.Specialties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => new { x.City, Speciality = s }))
            .GroupBy(x => new { City = x.City.Trim(), Speciality = x.Speciality.Trim() })
            .Where(g => g.Count() >= 3)
            .Take(500)
            .Select(g => "/nutricionistas/" + Slugify(g.Key.City) + "/" + Slugify(g.Key.Speciality));
        urls.AddRange(combinations);

        // Solo indexamos combinaciones ciudad+especialidad con oferta suficiente para evitar páginas SEO vacías.
        var combinations = citySpecialties
            .SelectMany(x => x.Specialties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => new { x.City, Speciality = s }))
            .GroupBy(x => new { City = x.City.Trim(), Speciality = x.Speciality.Trim() }, StringComparerAnonymous.Instance)
            .Where(g => g.Count() >= 3)
            .Take(500)
            .Select(g => "/nutricionistas/" + Slugify(g.Key.City) + "/" + Slugify(g.Key.Speciality));
        urls.AddRange(combinations);
        urls.AddRange(profiles.Select(x => "/nutricionistas/" + Uri.EscapeDataString(x)));

        var xml = new System.Text.StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var path in urls.Distinct())
            xml.Append("<url><loc>").Append(System.Net.WebUtility.HtmlEncode(baseUrl + path)).Append("</loc><changefreq>weekly</changefreq></url>");
        xml.Append("</urlset>");
        return Content(xml.ToString(), "application/xml; charset=utf-8");
    }

    private static string Slugify(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}