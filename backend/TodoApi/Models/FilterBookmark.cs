namespace TodoApi.Models;

public class FilterBookmark
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#6366f1";
    public string NameFilter { get; set; } = string.Empty;
    public string ListFilter { get; set; } = "[]";        // JSON int[]
    public string StatusFilter { get; set; } = "[]";      // JSON string[]
    public string PrioritaExcluded { get; set; } = "[]";  // JSON string[]
    public string RelatedFilter { get; set; } = string.Empty;
    public string DetailRelatedFilter { get; set; } = string.Empty;
    public string DateFrom { get; set; } = string.Empty;
    public string DateTo { get; set; } = string.Empty;
    // JSON int[] of collapsed todo ids — captures the exact expand/collapse state of every
    // tree at save time. Empty string = view state not captured (legacy bookmarks): applying
    // such a bookmark leaves the current expand/collapse state untouched.
    public string CollapsedIds { get; set; } = string.Empty;
}
