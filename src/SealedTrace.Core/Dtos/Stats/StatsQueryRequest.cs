namespace Arovyx.SealedTrace.Core.Dtos.Stats;

public sealed class StatsQueryRequest
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public sealed class StatsDimension
{
    public string Name { get; set; } = string.Empty;
    public long Count { get; set; }
}

public sealed class StatsResponse
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public long TotalEvents { get; set; }
    public List<StatsDimension> Levels { get; set; } = new();
    public List<StatsDimension> EventsPerDay { get; set; } = new();
    public List<StatsDimension> TopTags { get; set; } = new();
    public List<StatsDimension> TopUserIds { get; set; } = new();
}
