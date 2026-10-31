namespace Ticketing.Data;

/// <summary>The performance being sold. Exactly one event exists in this iteration.</summary>
public class EventRecord
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Artist { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    public required string Venue { get; set; }
}