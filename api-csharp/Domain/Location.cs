namespace CargoHub.Api.Domain;

public sealed class Location
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
