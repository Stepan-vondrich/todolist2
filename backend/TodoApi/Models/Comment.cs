namespace TodoApi.Models;

public class Comment
{
    public int Id { get; set; }
    public int TodoId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public ICollection<CommentAttachment> Attachments { get; set; } = new List<CommentAttachment>();
}
