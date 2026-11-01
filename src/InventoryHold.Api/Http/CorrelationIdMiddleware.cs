using System.Diagnostics;

namespace InventoryHold.Api.Http;

/// <summary>Reads or creates X-Correlation-Id, echoes it on the response and adds it to every log scope.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsAcceptable(supplied) ? supplied : Guid.NewGuid().ToString("N");

        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    // Accept only short, plain identifiers so a caller cannot inject odd characters into logs or headers.
    private static bool IsAcceptable(string value) =>
        value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
