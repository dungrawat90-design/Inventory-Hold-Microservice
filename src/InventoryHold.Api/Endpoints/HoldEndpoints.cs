using InventoryHold.Application;
using InventoryHold.Application.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace InventoryHold.Api.Endpoints;

public static class HoldEndpoints
{
    public static IEndpointRouteBuilder MapInventoryHoldEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        var holds = api.MapGroup("/holds").WithTags("Holds");

        holds.MapPost("/", CreateHold)
            .WithName("CreateHold")
            .WithSummary("Create an inventory hold")
            .WithDescription("Reserves the requested quantity of every SKU, or none of them. The hold expires after ttlMinutes (default 15, maximum 60).")
            .Produces<HoldResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        holds.MapGet("/{holdId}", GetHold)
            .WithName("GetHold")
            .WithSummary("Get a hold")
            .Produces<HoldResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        holds.MapDelete("/{holdId}", ReleaseHold)
            .WithName("ReleaseHold")
            .WithSummary("Release a hold")
            .WithDescription("Returns the held stock. Releasing a hold that is already released or expired changes nothing and returns its current state.")
            .Produces<HoldResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/inventory", GetInventory)
            .WithTags("Inventory")
            .WithName("GetInventory")
            .WithSummary("List stock")
            .WithDescription("Returns available, held and total quantity per SKU, paged. take defaults to 50 and is capped at 200.")
            .Produces<InventoryPage>();

        return app;
    }

    private static async Task<Created<HoldResponse>> CreateHold(CreateHoldRequest request, HoldService service, CancellationToken cancellationToken)
    {
        var hold = await service.CreateAsync(request, cancellationToken);
        return TypedResults.Created($"/api/holds/{hold.Id}", hold);
    }

    private static async Task<Ok<HoldResponse>> GetHold(string holdId, HoldService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetAsync(holdId, cancellationToken));

    private static async Task<Ok<HoldResponse>> ReleaseHold(string holdId, HoldService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ReleaseAsync(holdId, cancellationToken));

    private static async Task<Ok<InventoryPage>> GetInventory(int? skip, int? take, HoldService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.GetInventoryAsync(skip, take, cancellationToken));
}
