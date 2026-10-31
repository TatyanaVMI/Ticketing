namespace Ticketing.Data;

/// <summary>
/// The durable status of a seat. Only two states exist: a seat is on sale, or a purchase took it.
/// A visitor's current selection is not durable and is not represented here (spec FR-004, FR-013).
/// </summary>
public enum SeatStatus
{
    Available,
    Taken
}