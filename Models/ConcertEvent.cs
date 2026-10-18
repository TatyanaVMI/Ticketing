namespace Ticketing.Models;

public class ConcertEvent
{
    public required string Name { get; init; }
    public required string Artist { get; init; }
    public required DateTime Date { get; init; }
    public required string Venue { get; init; }

    public string DateText => Date.ToString("dddd, MMMM d, yyyy");
    public string TimeText => Date.ToString("t");
}
