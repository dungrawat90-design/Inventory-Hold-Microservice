using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using InventoryHold.Application.Contracts;

namespace InventoryHold.IntegrationTests;

/// <summary>
/// End-to-end checks against the real, running compose stack: API, MongoDB, Redis and RabbitMQ together.
/// Start the stack first with <c>docker compose up --build</c>.
/// </summary>
[Trait("Category", "Stack")]
public sealed class LiveStackTests : IClassFixture<StackFixture>
{
    private readonly StackFixture _stack;

    public LiveStackTests(StackFixture stack)
    {
        _stack = stack;
    }

    private static CreateHoldRequest Hold(string sku, int quantity, int? ttlMinutes = null) =>
        new([new HoldItemRequest(sku, quantity)], ttlMinutes);

    private async Task<InventoryItemResponse> StockAsync(string sku)
    {
        var page = await _stack.Api.GetFromJsonAsync<InventoryPage>("/api/inventory?take=200");
        return page!.Items.Single(i => i.Sku == sku);
    }

    private async Task<HoldResponse> CreateAsync(CreateHoldRequest request)
    {
        var response = await _stack.Api.PostAsJsonAsync("/api/holds", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<HoldResponse>())!;
    }

    [Fact]
    public async Task Ready_MongoRedisAndRabbitMqAreAllHealthy()
    {
        var response = await _stack.Api.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Inventory_ContainsAtLeastFiveSeededProducts()
    {
        var page = await _stack.Api.GetFromJsonAsync<InventoryPage>("/api/inventory?take=200");

        page!.Items.Select(i => i.Sku).Should().Contain(["DEMO-1001", "DEMO-1002", "DEMO-1003", "DEMO-1004", "DEMO-1005"]);
    }

    [Fact]
    public async Task Api_WithoutKey_IsRejected()
    {
        using var anonymous = new HttpClient { BaseAddress = _stack.Api.BaseAddress };

        var response = await anonymous.GetAsync("/api/inventory");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateReadRelease_RoundTripsThroughMongoAndKeepsTheRedisCacheCurrent()
    {
        var sku = await _stack.SeedSkuAsync(available: 5);
        (await StockAsync(sku)).Available.Should().Be(5); // fills the Redis cache

        var hold = await CreateAsync(Hold(sku, 2));
        var fetched = await _stack.Api.GetFromJsonAsync<HoldResponse>($"/api/holds/{hold.Id}");

        fetched!.Status.Should().Be("Active");
        (await StockAsync(sku)).Should().Be(new InventoryItemResponse(sku, 3, 2, 5)); // cache was invalidated

        var released = await (await _stack.Api.DeleteAsync($"/api/holds/{hold.Id}")).Content.ReadFromJsonAsync<HoldResponse>();
        released!.Status.Should().Be("Released");
        (await StockAsync(sku)).Should().Be(new InventoryItemResponse(sku, 5, 0, 5));
    }

    [Fact]
    public async Task ConcurrentHolds_NeverOversellRealMongo()
    {
        var sku = await _stack.SeedSkuAsync(available: 10);

        var responses = await Task.WhenAll(Enumerable.Range(0, 60)
            .Select(_ => Task.Run(() => _stack.Api.PostAsJsonAsync("/api/holds", Hold(sku, 1)))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(10);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(50);
        (await StockAsync(sku)).Should().Be(new InventoryItemResponse(sku, 0, 10, 10));
    }

    [Fact]
    public async Task MultiSkuHold_IsAllOrNothing()
    {
        var plenty = await _stack.SeedSkuAsync(available: 5);
        var none = await _stack.SeedSkuAsync(available: 0);

        var response = await _stack.Api.PostAsJsonAsync(
            "/api/holds",
            new CreateHoldRequest([new HoldItemRequest(plenty, 2), new HoldItemRequest(none, 1)], null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await StockAsync(plenty)).Should().Be(new InventoryItemResponse(plenty, 5, 0, 5));
    }

    [Fact]
    public async Task ReleasingTwice_RestoresStockOnce()
    {
        var sku = await _stack.SeedSkuAsync(available: 4);
        var hold = await CreateAsync(Hold(sku, 3));

        await _stack.Api.DeleteAsync($"/api/holds/{hold.Id}");
        var second = await _stack.Api.DeleteAsync($"/api/holds/{hold.Id}");

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StockAsync(sku)).Should().Be(new InventoryItemResponse(sku, 4, 0, 4));
    }

    [Fact]
    public async Task HoldEvents_ReachRabbitMqAndAreConsumed()
    {
        var sku = await _stack.SeedSkuAsync(available: 2);
        await CreateAsync(Hold(sku, 1));

        // The management API refreshes its statistics every few seconds, so poll instead of sleeping.
        var consumed = await WaitUntilAsync(TimeSpan.FromSeconds(45), async () =>
        {
            var queues = await _stack.RabbitManagement.GetFromJsonAsync<JsonElement>("/api/queues");
            return queues.EnumerateArray().Any(q =>
                q.GetProperty("name").GetString()!.Contains("HoldEventLog", StringComparison.OrdinalIgnoreCase)
                && q.TryGetProperty("message_stats", out var stats)
                && stats.TryGetProperty("ack", out var ack)
                && ack.GetInt32() > 0);
        });

        consumed.Should().BeTrue("a HoldCreated event should have been published and acknowledged by the consumer queue");
    }

    /// <summary>A hold with the minimum lifetime expires on its own and its stock comes back. Takes about 75 seconds.</summary>
    [Fact]
    [Trait("Category", "Slow")]
    public async Task HoldExpiry_ReturnsStockWithoutAnyCall()
    {
        var sku = await _stack.SeedSkuAsync(available: 3);
        var hold = await CreateAsync(Hold(sku, 2, ttlMinutes: 1));
        (await StockAsync(sku)).Held.Should().Be(2);

        var expired = await WaitUntilAsync(TimeSpan.FromSeconds(150), async () =>
            (await _stack.Api.GetFromJsonAsync<HoldResponse>($"/api/holds/{hold.Id}"))!.Status == "Expired");

        expired.Should().BeTrue("the expiry worker should expire the hold shortly after its one-minute lifetime");
        (await StockAsync(sku)).Should().Be(new InventoryItemResponse(sku, 3, 0, 3));
    }

    private static async Task<bool> WaitUntilAsync(TimeSpan timeout, Func<Task<bool>> condition)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (await condition())
            {
                return true;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return false;
    }
}
