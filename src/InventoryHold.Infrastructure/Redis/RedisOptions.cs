using System.ComponentModel.DataAnnotations;

namespace InventoryHold.Infrastructure.Redis;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    [Range(1, 3600)]
    public int InventoryTtlSeconds { get; set; } = 30;
}
