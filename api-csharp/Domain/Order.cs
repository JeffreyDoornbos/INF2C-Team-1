namespace CargoHub.Api.Domain;

public sealed class Order
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public DateOnly OrderDate { get; set; }
    public DateOnly RequestDate { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string CustomerPoNumber { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public string? ShippingNotes { get; set; }
    public int WarehouseId { get; set; }
    public int ShipToClientId { get; set; }
    public int BillToClientId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
