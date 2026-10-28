namespace CargoHub.Api.Domain;

public sealed class Shipment
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public DateOnly ShipmentDate { get; set; }
    public string ShipmentType { get; set; } = string.Empty;
    public string ShipmentStatus { get; set; } = string.Empty;
    public string CarrierName { get; set; } = string.Empty;
    public string ShippingMethod { get; set; } = string.Empty;
    public string PaymentType { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
