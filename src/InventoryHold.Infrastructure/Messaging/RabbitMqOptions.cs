using System.ComponentModel.DataAnnotations;

namespace InventoryHold.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 5672;

    [Required]
    public string VirtualHost { get; set; } = "/";

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    [Range(0, 20)]
    public int RetryCount { get; set; } = 3;

    [Range(1, 600)]
    public int RetryIntervalSeconds { get; set; } = 10;
}
