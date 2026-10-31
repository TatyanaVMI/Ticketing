namespace Ticketing.Models;

/// <summary>
/// A committed purchase, as read back out of the store. This is the only source of the confirmation
/// a visitor sees (spec FR-014).
/// </summary>
public sealed class PurchaseReceipt
{
    public required int PurchaseId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required IReadOnlyList<PurchaseReceiptLine> Seats { get; init; }

    /// <summary>The amount recorded with the purchase. Never recomputed from current prices (FR-006).</summary>
    public required decimal Total { get; init; }
}

/// <summary>One seat in a completed purchase, with the price that was paid for it.</summary>
public sealed record PurchaseReceiptLine(string Label, decimal PricePaid);