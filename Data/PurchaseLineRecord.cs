namespace Ticketing.Data;

/// <summary>
/// The link between a purchase and one seat, carrying the price paid for that seat at that moment so
/// a later price change cannot rewrite history (spec FR-006).
/// </summary>
public class PurchaseLineRecord
{
    public int Id { get; set; }

    public int PurchaseId { get; set; }

    public PurchaseRecord Purchase { get; set; } = default!;

    public int SeatId { get; set; }

    public decimal PricePaid { get; set; }
}