using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CargoHub.Api.Contracts;

public sealed class ShipmentLineRequest
{
    [Range(1, int.MaxValue)] public int ItemId { get; set; }
    [Range(1, int.MaxValue)] public int Amount { get; set; }
}

public sealed class ShipmentWriteRequest
{
    [Range(0, int.MaxValue)] public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    [JsonConverter(typeof(ShipmentDateConverter))]
    public DateTimeOffset ShipmentDate { get; set; }
    public string ShipmentType { get; set; } = string.Empty;
    [RegularExpression("^(Scheduled|Transit|Delivered)$")]
    public string ShipmentStatus { get; set; } = "Scheduled";
    public string CarrierName { get; set; } = string.Empty;
    public string ShippingMethod { get; set; } = string.Empty;
    public string PaymentType { get; set; } = string.Empty;
    // Python: missing/null leaves lines unchanged on PUT; [] clears them.
    public List<ShipmentLineRequest>? Items { get; set; }
}

public sealed record ShipmentLineResponse(int ItemId, int Amount);
public sealed record ShipmentResponse(int Id, string Reference, int? OrderId,
    DateTimeOffset ShipmentDate, string ShipmentType, string ShipmentStatus,
    string CarrierName, string ShippingMethod, string PaymentType,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<ShipmentLineResponse> Items);

// Accept explicit-offset legacy timestamps and existing date-only clients.
// Never interpret an unspecified local time using the machine's time zone.
public sealed class ShipmentDateConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Expected shipment date or timestamp.");
        var text = reader.GetString()!;
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"(Z|[+-]\d{2}:\d{2})$") &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            return timestamp.ToUniversalTime();
        throw new JsonException("Shipment timestamp must include Z or an explicit UTC offset.");
    }
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToUniversalTime());
}
