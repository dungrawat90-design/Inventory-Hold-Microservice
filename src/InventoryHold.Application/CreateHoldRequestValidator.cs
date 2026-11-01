using FluentValidation;
using InventoryHold.Application.Contracts;
using Microsoft.Extensions.Options;

namespace InventoryHold.Application;

public sealed class CreateHoldRequestValidator : AbstractValidator<CreateHoldRequest>
{
    public CreateHoldRequestValidator(IOptions<HoldOptions> options)
    {
        var settings = options.Value;

        RuleFor(r => r.Items)
            .NotNull().WithMessage("At least one item is required.")
            .Must(items => items is { Count: > 0 }).WithMessage("At least one item is required.")
            .Must(items => items is null || items.Count <= settings.MaxItemsPerHold)
            .WithMessage($"A hold can contain at most {settings.MaxItemsPerHold} items.");

        RuleForEach(r => r.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.Sku)
                .NotEmpty().WithMessage("Sku is required.")
                .MaximumLength(64).WithMessage("Sku must be at most 64 characters.")
                .Matches("^[A-Za-z0-9._-]+$").WithMessage("Sku may contain only letters, digits, '.', '_' and '-'.");

            item.RuleFor(i => i.Quantity)
                .InclusiveBetween(1, settings.MaxQuantityPerItem)
                .WithMessage($"Quantity must be between 1 and {settings.MaxQuantityPerItem}.");
        });

        RuleFor(r => r.TtlMinutes)
            .InclusiveBetween(1, settings.MaxTtlMinutes)
            .When(r => r.TtlMinutes.HasValue)
            .WithMessage($"TtlMinutes must be between 1 and {settings.MaxTtlMinutes}.");
    }
}
