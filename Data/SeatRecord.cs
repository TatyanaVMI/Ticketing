namespace Ticketing.Data;

/// <summary>One seat in the venue, with the price it is sold at and its durable status.</summary>
public class SeatRecord
{
    public int Id { get; set; }

    public int EventId { get; set; }

    public EventRecord Event { get; set; } = default!;

    /// <summary>Single letter, <c>A</c> through <c>J</c>.</summary>
    public required string Row { get; set; }

    /// <summary>Seat number in the row, <c>1</c> through <c>12</c>.</summary>
    public int Number { get; set; }

    public decimal Price { get; set; }

    public SeatStatus Status { get; set; }
}