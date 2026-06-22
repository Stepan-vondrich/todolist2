namespace TodoApi.Models;

public class TaskSession
{
    public int Id { get; set; }
    public int TodoId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int ActiveCountAtStart { get; set; }
    public string? Comment { get; set; }
}
