namespace TodoApi.Models;

public class TaskOverlap
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public int OverlappingSessionId { get; set; }
    public int TodoId { get; set; }
    public int OverlappingTodoId { get; set; }
    public DateTime OverlapStart { get; set; }
    public DateTime OverlapEnd { get; set; }
    public double OverlapMinutes { get; set; }
}
