namespace Ticketing.Data;

/// <summary>
/// One completed transaction. The amount is fixed when it is written and is never recomputed from
/// current prices (spec FR-006).
/// </summary>
public class PurchaseRecord
{
    public int Id { get; set; }

    public int EventId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public decimal TotalAmount { get; set; }

    public ICollection<PurchaseLineRecord> Lines { get; } = new List<PurchaseLineRecord>();
}