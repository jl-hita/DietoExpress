using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Anguloso.Server.Middleware;

/// <summary>
/// Normaliza los resultados HTTP 5xx devueltos explícitamente por controladores.
/// Complementa ErrorHandlingMiddleware, que cubre excepciones no controladas.
/// </summary>
public sealed class ApiServerErrorResultFilter : IAsyncResultFilter
{
    private readonly ILogger<ApiServerErrorResultFilter> _logger;

    public ApiServerErrorResultFilter(ILogger<ApiServerErrorResultFilter> logger)
    {
        _logger = logger;
    }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is IStatusCodeActionResult { StatusCode: >= 500 } result)
        {
            var status = result.StatusCode!.Value;
            var correlationId = context.HttpContext.TraceIdentifier;
            context.HttpContext.Response.Headers["X-Correlation-ID"] = correlationId;

            _logger.LogError(
                "Controller returned server error. StatusCode={StatusCode}, CorrelationId={CorrelationId}, Method={Method}, Path={Path}",
                status,
                correlationId,
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path);

            var problem = new ProblemDetails
            {
                Status = status,
                Title = status == StatusCodes.Status503ServiceUnavailable
                    ? "El servicio no está disponible temporalmente."
                    : "Se ha producido un error inesperado.",
                Type = status == StatusCodes.Status503ServiceUnavailable
                    ? "https://jlhitap.duckdns.org/problems/service-unavailable"
                    : "https://jlhitap.duckdns.org/problems/internal-error"
            };
            problem.Extensions["code"] = status == StatusCodes.Status503ServiceUnavailable
                ? "SERVICE_UNAVAILABLE"
                : "INTERNAL_ERROR";
            problem.Extensions["correlationId"] = correlationId;
            problem.Extensions["reportable"] = true;

            context.Result = new ObjectResult(problem) { StatusCode = status };
        }

        await next();
    }
}