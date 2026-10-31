namespace Ticketing.Models;

public enum PurchaseOutcomeKind
{
    /// <summary>Nothing was selected; the store was not touched.</summary>
    NothingSelected,

    /// <summary>A selected seat was taken before the purchase committed; nothing was written.</summary>
    SeatNoLongerAvailable,

    /// <summary>The purchase, its seats, and their new statuses were committed together.</summary>
    Succeeded
}

/// <summary>
/// What a purchase attempt produced. <see cref="PurchaseId"/> is set only when it succeeded, and the
/// confirmation is read back from the store with it (service contract §3).
/// </summary>
public readonly record struct PurchaseOutcome(PurchaseOutcomeKind Kind, int? PurchaseId)
{
    public static PurchaseOutcome NothingSelected => new(PurchaseOutcomeKind.NothingSelected, null);

    public static PurchaseOutcome SeatNoLongerAvailable => new(PurchaseOutcomeKind.SeatNoLongerAvailable, null);

    public static PurchaseOutcome Succeeded(int purchaseId) => new(PurchaseOutcomeKind.Succeeded, purchaseId);
}