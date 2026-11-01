using InventoryHold.Application.Abstractions;
using InventoryHold.Infrastructure.Health;
using InventoryHold.Infrastructure.Messaging;
using InventoryHold.Infrastructure.Mongo;
using InventoryHold.Infrastructure.Redis;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace InventoryHold.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInventoryHoldInfrastructure(this IServiceCollection services)
    {
        services.AddOptions<MongoOptions>().BindConfiguration(MongoOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<RedisOptions>().BindConfiguration(RedisOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<RabbitMqOptions>().BindConfiguration(RabbitMqOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName);

        services.AddSingleton<IMongoClient>(sp =>
        {
            var settings = MongoClientSettings.FromConnectionString(sp.GetRequiredService<IOptions<MongoOptions>>().Value.ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);
            return new MongoClient(settings);
        });
        services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(sp.GetRequiredService<IOptions<MongoOptions>>().Value.Database));
        services.AddSingleton<MongoCollections>();
        services.AddSingleton<IInventoryRepository, MongoInventoryRepository>();
        services.AddSingleton<IHoldRepository, MongoHoldRepository>();
        services.AddHostedService<MongoInitializer>();

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var configuration = ConfigurationOptions.Parse(sp.GetRequiredService<IOptions<RedisOptions>>().Value.ConnectionString);
            configuration.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(configuration);
        });
        services.AddSingleton<IInventoryCache, RedisInventoryCache>();

        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<HoldEventLogConsumer>();
            bus.UsingRabbitMq((context, cfg) =>
            {
                var rabbit = context.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
                cfg.Host(rabbit.Host, (ushort)rabbit.Port, rabbit.VirtualHost, host =>
                {
                    host.Username(rabbit.Username);
                    host.Password(rabbit.Password);
                });
                cfg.UseMessageRetry(retry => retry.Interval(rabbit.RetryCount, TimeSpan.FromSeconds(rabbit.RetryIntervalSeconds)));
                cfg.ConfigureEndpoints(context);
            });
        });
        services.AddScoped<IHoldEventPublisher, MassTransitHoldEventPublisher>();

        services.AddHostedService<HoldExpiryWorker>();

        services.AddHealthChecks()
            .AddCheck<MongoHealthCheck>("mongodb", tags: ["ready"])
            .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

        return services;
    }
}
