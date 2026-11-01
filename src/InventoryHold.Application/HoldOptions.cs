using System.ComponentModel.DataAnnotations;

namespace InventoryHold.Application;

public sealed class HoldOptions
{
    public const string SectionName = "Holds";

    [Range(1, 1440)]
    public int DefaultTtlMinutes { get; set; } = 15;

    [Range(1, 1440)]
    public int MaxTtlMinutes { get; set; } = 60;

    [Range(1, 500)]
    public int MaxItemsPerHold { get; set; } = 50;

    [Range(1, 1_000_000)]
    public int MaxQuantityPerItem { get; set; } = 10_000;

    [Range(1, 1000)]
    public int ExpiryBatchSize { get; set; } = 100;

    [Range(1, 3600)]
    public int ExpiryIntervalSeconds { get; set; } = 15;

    [Range(1, 200)]
    public int MaxInventoryPageSize { get; set; } = 200;

    [Range(1, 200)]
    public int DefaultInventoryPageSize { get; set; } = 50;
}
