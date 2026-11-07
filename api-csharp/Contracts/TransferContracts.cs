using System.ComponentModel.DataAnnotations;

namespace CargoHub.Api.Contracts;

public sealed class TransferLineRequest
{
    [Range(1, int.MaxValue)] public int ItemId { get; set; }
    [Range(1, int.MaxValue)] public int Amount { get; set; }
}

public sealed class TransferWriteRequest
{
    [Range(0, int.MaxValue)] public int Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    [Range(1, int.MaxValue)] public int FromLocationId { get; set; }
    [Range(1, int.MaxValue)] public int ToLocationId { get; set; }
    // POST overrides the supplied status, exactly as Python add_transfer does.
    public string TransferStatus { get; set; } = "Scheduled";
    // A missing/null items property on PUT preserves existing lines.
    public List<TransferLineRequest>? Items { get; set; }
}

public sealed record TransferLineResponse(int ItemId, int Amount);
public sealed record TransferResponse(int Id, string Reference, int FromLocationId, int ToLocationId,
    string TransferStatus, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<TransferLineResponse> Items);
