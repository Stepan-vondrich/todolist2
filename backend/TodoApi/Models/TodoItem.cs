namespace TodoApi.Models;

public class TodoItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public string Status { get; set; } = "";
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? ParentId { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string Related { get; set; } = string.Empty;
    public string DetailRelated { get; set; } = string.Empty;
    public int SortOrder { get; set; } = 0;
}
