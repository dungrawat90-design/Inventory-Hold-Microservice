using System.Diagnostics;
using FluentValidation;
using InventoryHold.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace InventoryHold.Api.Http;

/// <summary>
/// The one place that turns exceptions into ProblemDetails. Expected refusals answer 4xx and are not logged as errors;
/// anything else answers 500 with a trace id and is logged once, here.
/// </summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        var problem = exception switch
        {
            ValidationException validation => Validation(validation),
            InsufficientStockException e => Problem(StatusCodes.Status409Conflict, "Insufficient stock", $"Not enough stock available for SKU '{e.Sku}'."),
            SkuNotFoundException e => Problem(StatusCodes.Status404NotFound, "SKU not found", $"SKU '{e.Sku}' does not exist."),
            HoldNotFoundException => Problem(StatusCodes.Status404NotFound, "Hold not found", "No hold exists with that id."),
            BadHttpRequestException => Problem(StatusCodes.Status400BadRequest, "Bad request", "The request body could not be read."),
            _ => null,
        };

        if (problem is null)
        {
            logger.LogError(
                exception,
                "Unhandled exception for {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path.Value);
            problem = Problem(StatusCodes.Status500InternalServerError, "Unexpected error", "Something went wrong. Quote the trace id when reporting it.");
        }

        problem.Extensions["traceId"] = traceId;
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        // Serialize as the runtime type, otherwise ValidationProblemDetails would lose its Errors member.
        await httpContext.Response.WriteAsJsonAsync(problem, problem.GetType(), cancellationToken);
        return true;
    }

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };

    private static ValidationProblemDetails Validation(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        return new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
        };
    }
}
