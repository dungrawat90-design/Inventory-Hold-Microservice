namespace InventoryHold.Domain;

public sealed record InventoryItem(string Sku, int Available, int Held)
{
    public int Total => Available + Held;
}
