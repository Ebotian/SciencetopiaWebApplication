public class LinkDTO
{
    public string? Source { get; set; }
    public string? Target { get; set; }
    public string? Relation { get; set; }
    public int? Weight { get; set; } = 1;
}