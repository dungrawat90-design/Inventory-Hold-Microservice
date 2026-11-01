using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryHold.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddInventoryHoldApplication(this IServiceCollection services)
    {
        services.AddOptions<HoldOptions>()
            .BindConfiguration(HoldOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IValidator<Contracts.CreateHoldRequest>, CreateHoldRequestValidator>();
        services.AddScoped<HoldService>();
        return services;
    }
}
