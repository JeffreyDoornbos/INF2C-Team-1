namespace CargoHub.Api.Domain;

public sealed class Inventory
{
    public int ItemId { get; set; }
    public int LocationId { get; set; }
    public int QuantityOnHand { get; set; }
    public int QuantityExpected { get; set; }
    public int QuantityOrdered { get; set; }
    public int QuantityAllocated { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
