using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Anguloso.Server.Middleware;

/// <summary>
/// Normaliza las excepciones no controladas de la API sin exponer detalles internos.
/// El identificador permite relacionar la respuesta HTTP con los logs del servidor.
/// </summary>
public sealed class ErrorHandlingMiddleware
{
    private static readonly JsonSerializerOptions ProblemJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.TraceIdentifier;
        if (context.Request.Path.StartsWithSegments("/api"))
            context.Response.Headers["X-Correlation-ID"] = correlationId;

        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception,
                "Unhandled request exception. CorrelationId={CorrelationId}, Method={Method}, Path={Path}",
                correlationId, context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
                throw;

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            context.Response.Headers["X-Correlation-ID"] = correlationId;

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Se ha producido un error inesperado.",
                Type = "https://jlhitap.duckdns.org/problems/internal-error"
            };
            problem.Extensions["code"] = "INTERNAL_ERROR";
            problem.Extensions["correlationId"] = correlationId;
            problem.Extensions["reportable"] = true;

            // WriteAsJsonAsync establece application/json por defecto. Serializar directamente
            // conserva el media type RFC 9457 y usa nombres JSON camelCase.
            await JsonSerializer.SerializeAsync(
                context.Response.Body,
                problem,
                ProblemJsonOptions,
                context.RequestAborted);
        }
    }
}