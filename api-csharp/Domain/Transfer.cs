namespace CargoHub.Api.Domain;

public sealed class Transfer
{
    public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public int FromLocationId { get; set; }
    public int ToLocationId { get; set; }
    public string TransferStatus { get; set; } = "Scheduled";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
