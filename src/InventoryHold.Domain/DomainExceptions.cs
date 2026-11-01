namespace InventoryHold.Domain;

public abstract class DomainException(string message) : Exception(message);

public sealed class InsufficientStockException(string sku) : DomainException("Not enough stock available.")
{
    public string Sku { get; } = sku;
}

public sealed class SkuNotFoundException(string sku) : DomainException("SKU was not found.")
{
    public string Sku { get; } = sku;
}

public sealed class HoldNotFoundException(string holdId) : DomainException("Hold was not found.")
{
    public string HoldId { get; } = holdId;
}

public sealed class InventoryInconsistencyException(string sku) : DomainException("Held quantity is lower than the quantity to restore.")
{
    public string Sku { get; } = sku;
}
