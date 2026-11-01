using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using InventoryHold.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace InventoryHold.Tests.Api;

public sealed class HoldApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public HoldApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient AuthorizedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiFactory.ApiKey);
        return client;
    }

    private static CreateHoldRequest Body(string sku, int quantity, int? ttl = null) =>
        new([new HoldItemRequest(sku, quantity)], ttl);

    [Fact]
    public async Task Post_WithoutApiKey_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/holds", Body("API-1", 1));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_WithWrongApiKey_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-wrong-wrong-wrong");

        var response = await client.GetAsync("/api/inventory");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostHold_ValidRequest_Returns201WithLocationAndBody()
    {
        _factory.Inventory.Seed("API-CREATE", 5);

        var response = await AuthorizedClient().PostAsJsonAsync("/api/holds", Body("API-CREATE", 2, 30));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var hold = (await response.Content.ReadFromJsonAsync<HoldResponse>())!;
        hold.Status.Should().Be("Active");
        hold.Items.Should().ContainSingle().Which.Quantity.Should().Be(2);
        response.Headers.Location!.ToString().Should().Be($"/api/holds/{hold.Id}");
        response.Headers.Contains("X-Correlation-Id").Should().BeTrue();
    }

    [Fact]
    public async Task PostHold_InvalidBody_Returns400WithFieldErrors()
    {
        var response = await AuthorizedClient().PostAsJsonAsync("/api/holds", new CreateHoldRequest([new HoldItemRequest("API-X", 0)], null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        problem.Errors.Should().NotBeEmpty();
        problem.Extensions.Should().ContainKey("traceId");
    }

    [Fact]
    public async Task PostHold_NotEnoughStock_Returns409()
    {
        _factory.Inventory.Seed("API-LOW", 1);

        var response = await AuthorizedClient().PostAsJsonAsync("/api/holds", Body("API-LOW", 2));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title.Should().Be("Insufficient stock");
    }

    [Fact]
    public async Task PostHold_UnknownSku_Returns404()
    {
        var response = await AuthorizedClient().PostAsJsonAsync("/api/holds", Body("API-NOPE", 1));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetHold_Existing_Returns200()
    {
        _factory.Inventory.Seed("API-GET", 3);
        var client = AuthorizedClient();
        var created = (await (await client.PostAsJsonAsync("/api/holds", Body("API-GET", 1))).Content.ReadFromJsonAsync<HoldResponse>())!;

        var response = await client.GetAsync($"/api/holds/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<HoldResponse>())!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetHold_Unknown_Returns404()
    {
        var response = await AuthorizedClient().GetAsync("/api/holds/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteHold_Active_ReleasesAndRestoresStock()
    {
        _factory.Inventory.Seed("API-DEL", 4);
        var client = AuthorizedClient();
        var created = (await (await client.PostAsJsonAsync("/api/holds", Body("API-DEL", 3))).Content.ReadFromJsonAsync<HoldResponse>())!;

        var response = await client.DeleteAsync($"/api/holds/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<HoldResponse>())!.Status.Should().Be("Released");
        _factory.Inventory.Get("API-DEL").Available.Should().Be(4);
    }

    [Fact]
    public async Task DeleteHold_Twice_StaysReleasedAndRestoresOnce()
    {
        _factory.Inventory.Seed("API-DEL2", 4);
        var client = AuthorizedClient();
        var created = (await (await client.PostAsJsonAsync("/api/holds", Body("API-DEL2", 3))).Content.ReadFromJsonAsync<HoldResponse>())!;

        await client.DeleteAsync($"/api/holds/{created.Id}");
        var second = await client.DeleteAsync($"/api/holds/{created.Id}");

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Inventory.Get("API-DEL2").Available.Should().Be(4);
    }

    [Fact]
    public async Task DeleteHold_Unknown_Returns404()
    {
        var response = await AuthorizedClient().DeleteAsync("/api/holds/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetInventory_ReturnsPagedStockWithHeldQuantity()
    {
        _factory.Inventory.Seed("API-INV", 10);
        var client = AuthorizedClient();
        await client.PostAsJsonAsync("/api/holds", Body("API-INV", 4));

        var page = (await client.GetFromJsonAsync<InventoryPage>("/api/inventory?take=200"))!;

        page.Items.Should().Contain(new InventoryItemResponse("API-INV", 6, 4, 10));
    }

    [Fact]
    public async Task Health_Live_IsAnonymous()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApiDocument_DescribesTheFourDocumentedOperations()
    {
        var document = await _factory.CreateClient().GetStringAsync("/openapi/v1.json");

        document.Should().Contain("\"/api/holds\"")
            .And.Contain("\"/api/holds/{holdId}\"")
            .And.Contain("\"/api/inventory\"")
            .And.Contain("\"operationId\": \"CreateHold\"")
            .And.Contain("\"operationId\": \"GetHold\"")
            .And.Contain("\"operationId\": \"ReleaseHold\"")
            .And.Contain("\"operationId\": \"GetInventory\"");
    }

    [Fact]
    public async Task Response_DoesNotAdvertiseTheServer()
    {
        var response = await _factory.CreateClient().GetAsync("/health/live");

        response.Headers.Contains("Server").Should().BeFalse();
    }

    [Fact]
    public async Task CorrelationId_SuppliedByCaller_IsEchoed()
    {
        var client = AuthorizedClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "abc-123");

        var response = await client.GetAsync("/api/inventory");

        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle("abc-123");
    }
}
