namespace TodoApi.Models;

public class TaskLog
{
    public int Id { get; set; }
    public int TodoId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    /// <summary>
    /// create | column_change | subtask_added | subtask_moved_in | subtask_moved_out | subtask_deleted | moved
    /// </summary>
    public string EventType { get; set; } = string.Empty;
    /// <summary>JSON payload, schema depends on EventType</summary>
    public string? Detail { get; set; }
}
