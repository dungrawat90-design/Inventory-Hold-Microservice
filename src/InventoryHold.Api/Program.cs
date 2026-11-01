using System.Threading.RateLimiting;
using InventoryHold.Api.Endpoints;
using InventoryHold.Api.Http;
using InventoryHold.Api.Security;
using InventoryHold.Application;
using InventoryHold.Infrastructure;
using InventoryHold.Infrastructure.InMemory;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

builder.Services.AddOptions<ApiKeyOptions>()
    .BindConfiguration(ApiKeyOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(cors => cors.AddPolicy("web", policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins)
            .WithHeaders("Content-Type", ApiKeyOptions.HeaderName, CorrelationIdMiddleware.HeaderName)
            .WithMethods("GET", "POST", "DELETE")
            .WithExposedHeaders(CorrelationIdMiddleware.HeaderName);
    }
}));

builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy("api", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:PermitLimit", 300),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

builder.Services.AddHealthChecks();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddInventoryHoldApplication();
var inMemoryDemo = builder.Configuration.GetValue<bool>("Demo:InMemory");
if (inMemoryDemo)
{
    // Demo only: no MongoDB, Redis or RabbitMQ. State is lost on restart.
    builder.Services.AddInventoryHoldInMemoryDemo();
}
else
{
    builder.Services.AddInventoryHoldInfrastructure();
}

var app = builder.Build();

if (inMemoryDemo)
{
    app.Logger.LogWarning("Running in IN-MEMORY DEMO mode: no MongoDB, Redis or RabbitMQ; data is lost on restart.");
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors("web");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Docs:OpenApi"))
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapInventoryHoldEndpoints();

app.Run();

public partial class Program;
