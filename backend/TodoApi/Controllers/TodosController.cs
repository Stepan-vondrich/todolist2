using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TodosController(AppDbContext db) : ControllerBase
{
    static readonly JsonSerializerOptions J = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    void Log(int todoId, string eventType, object? detail = null) =>
        db.TaskLogs.Add(new TaskLog
        {
            TodoId    = todoId,
            Timestamp = DateTime.UtcNow,
            EventType = eventType,
            Detail    = detail is null ? null : JsonSerializer.Serialize(detail, J),
        });

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.Todos.OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedAt).ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create(TodoItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Title))
            return BadRequest("Title is required.");

        item.Id = 0;
        item.CreatedAt = DateTime.UtcNow;

        var maxOrder = await db.Todos
            .Where(t => t.ParentId == item.ParentId)
            .Select(t => (int?)t.SortOrder)
            .MaxAsync() ?? -1;
        item.SortOrder = maxOrder + 1;

        db.Todos.Add(item);
        await db.SaveChangesAsync(); // need Id assigned before logging

        // Log creation on the new task itself
        if (item.ParentId.HasValue)
        {
            var parentTitle = (await db.Todos.FindAsync(item.ParentId.Value))?.Title;
            Log(item.Id, "create", new { parentId = item.ParentId, parentTitle });
        }
        else
        {
            Log(item.Id, "create");
        }

        // Log on parent that a subtask was added
        if (item.ParentId.HasValue)
            Log(item.ParentId.Value, "subtask_added", new { childId = item.Id, title = item.Title });

        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new { id = item.Id }, item);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, TodoItem updated)
    {
        var item = await db.Todos.FindAsync(id);
        if (item is null) return NotFound();

        // ── column change logs (compare before updating) ──────────────────────
        if (item.Title != updated.Title && !string.IsNullOrWhiteSpace(updated.Title))
            Log(id, "column_change", new { column = "title", from = item.Title, to = updated.Title });

        if (item.Status != updated.Status)
            Log(id, "column_change", new { column = "status", from = item.Status, to = updated.Status });

        var oldDue = item.DueDate?.ToString("yyyy-MM-dd");
        var newDue = updated.DueDate?.ToString("yyyy-MM-dd");
        if (oldDue != newDue)
            Log(id, "column_change", new { column = "dueDate", from = oldDue, to = newDue });

        if (item.Priority != updated.Priority)
            Log(id, "column_change", new { column = "priority", from = item.Priority, to = updated.Priority });

        if (item.Related != updated.Related)
            Log(id, "column_change", new { column = "related", from = item.Related, to = updated.Related });

        if (item.DetailRelated != updated.DetailRelated)
            Log(id, "column_change", new { column = "detailRelated", from = item.DetailRelated, to = updated.DetailRelated });

        // ── parent change (move) ─────────────────────────────────────────────
        if (item.ParentId != updated.ParentId)
        {
            string? oldParentTitle = item.ParentId.HasValue
                ? (await db.Todos.FindAsync(item.ParentId.Value))?.Title : null;
            string? newParentTitle = updated.ParentId.HasValue
                ? (await db.Todos.FindAsync(updated.ParentId.Value))?.Title : null;

            // Log on the moved task
            Log(id, "moved", new { toParentId = updated.ParentId, toParentTitle = newParentTitle });

            // Log on old parent: subtask moved away
            if (item.ParentId.HasValue)
                Log(item.ParentId.Value, "subtask_moved_out",
                    new { childId = id, title = item.Title, toParentTitle = newParentTitle });

            // Log on new parent: subtask moved in
            if (updated.ParentId.HasValue)
                Log(updated.ParentId.Value, "subtask_moved_in",
                    new { childId = id, title = item.Title, fromParentTitle = oldParentTitle });
        }

        // ── apply changes ────────────────────────────────────────────────────
        item.Title        = updated.Title;
        item.IsCompleted  = updated.IsCompleted;
        item.Status       = updated.Status;
        item.DueDate      = updated.DueDate;
        item.Priority     = updated.Priority;
        item.Related      = updated.Related;
        item.DetailRelated = updated.DetailRelated;
        item.ParentId     = updated.ParentId;

        await db.SaveChangesAsync();
        return Ok(item);
    }

    public record MoveDto(string Direction);

    [HttpPost("{id}/move")]
    public async Task<IActionResult> Move(int id, [FromBody] MoveDto dto)
    {
        var todo = await db.Todos.FindAsync(id);
        if (todo is null) return NotFound();

        var siblings = await db.Todos
            .Where(t => t.ParentId == todo.ParentId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .ToListAsync();

        for (int i = 0; i < siblings.Count; i++) siblings[i].SortOrder = i;

        int idx     = siblings.FindIndex(t => t.Id == id);
        int swapIdx = dto.Direction == "up" ? idx - 1 : idx + 1;

        if (swapIdx < 0 || swapIdx >= siblings.Count)
        {
            await db.SaveChangesAsync();
            return Ok(siblings);
        }

        (siblings[idx].SortOrder, siblings[swapIdx].SortOrder) =
            (siblings[swapIdx].SortOrder, siblings[idx].SortOrder);

        await db.SaveChangesAsync();
        return Ok(siblings);
    }

    public record ReorderDto(int TargetId, string Position);

    // Drag-and-drop reorder: drop `id` relative to `targetId`.
    //   position "before"/"after" → make `id` a sibling of the target, placed
    //     immediately before/after it (re-parenting if the target lives elsewhere).
    //   position "inside"        → make `id` the last child of the target.
    // SortOrder is re-densified per affected parent so the new order is stable.
    [HttpPost("{id}/reorder")]
    public async Task<IActionResult> Reorder(int id, [FromBody] ReorderDto dto)
    {
        var moved = await db.Todos.FindAsync(id);
        if (moved is null) return NotFound();

        var target = await db.Todos.FindAsync(dto.TargetId);
        if (target is null) return NotFound();

        if (id == dto.TargetId) return BadRequest("Cannot drop a task onto itself.");

        // Guard against cycles: target must not be the moved task's descendant.
        var all = await db.Todos.ToListAsync();
        if (IsDescendant(all, dto.TargetId, id))
            return BadRequest("Cannot drop a task into its own descendant.");

        int? newParentId;
        int insertIndex;

        if (dto.Position == "inside")
        {
            newParentId = target.Id;
            insertIndex = int.MaxValue; // append to the end of the target's children
        }
        else if (dto.Position == "before" || dto.Position == "after")
        {
            newParentId = target.ParentId;
            var siblings = all
                .Where(t => t.ParentId == newParentId && t.Id != id)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .ToList();
            int targetIdx = siblings.FindIndex(t => t.Id == target.Id);
            insertIndex = dto.Position == "before" ? targetIdx : targetIdx + 1;
        }
        else
        {
            return BadRequest("Position must be 'before', 'after', or 'inside'.");
        }

        var oldParentId = moved.ParentId;
        if (oldParentId != newParentId)
        {
            string? oldParentTitle = oldParentId.HasValue
                ? all.FirstOrDefault(t => t.Id == oldParentId.Value)?.Title : null;
            string? newParentTitle = newParentId.HasValue
                ? all.FirstOrDefault(t => t.Id == newParentId.Value)?.Title : null;

            Log(id, "moved", new { toParentId = newParentId, toParentTitle = newParentTitle });
            if (oldParentId.HasValue)
                Log(oldParentId.Value, "subtask_moved_out",
                    new { childId = id, title = moved.Title, toParentTitle = newParentTitle });
            if (newParentId.HasValue)
                Log(newParentId.Value, "subtask_moved_in",
                    new { childId = id, title = moved.Title, fromParentTitle = oldParentTitle });
        }

        moved.ParentId = newParentId;

        // Rebuild the destination sibling list with `moved` inserted at insertIndex,
        // then re-densify SortOrder so values are 0,1,2,… and gaps from the move close.
        var dest = all
            .Where(t => t.ParentId == newParentId && t.Id != id)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .ToList();
        insertIndex = Math.Clamp(insertIndex, 0, dest.Count);
        dest.Insert(insertIndex, moved);
        for (int i = 0; i < dest.Count; i++) dest[i].SortOrder = i;

        // If we re-parented, the old parent's remaining children may have a gap; close it.
        if (oldParentId != newParentId)
        {
            var oldSiblings = all
                .Where(t => t.ParentId == oldParentId && t.Id != id)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .ToList();
            for (int i = 0; i < oldSiblings.Count; i++) oldSiblings[i].SortOrder = i;
        }

        await db.SaveChangesAsync();
        return Ok(all.OrderBy(t => t.SortOrder));
    }

    // Is `candidateDescendantId` somewhere in the subtree rooted at `ancestorId`?
    static bool IsDescendant(List<TodoItem> all, int candidateDescendantId, int ancestorId)
    {
        int? cur = all.FirstOrDefault(t => t.Id == candidateDescendantId)?.ParentId;
        while (cur.HasValue)
        {
            if (cur.Value == ancestorId) return true;
            cur = all.FirstOrDefault(t => t.Id == cur.Value)?.ParentId;
        }
        return false;
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await db.Todos.FindAsync(id);
        if (item is null) return NotFound();

        // Log on parent that a direct subtask was deleted — but only if the parent
        // still exists. An orphaned subtask (parent already gone) would otherwise
        // hit the TaskLogs→Todos FK constraint.
        if (item.ParentId.HasValue && await db.Todos.AnyAsync(t => t.Id == item.ParentId.Value))
            Log(item.ParentId.Value, "subtask_deleted", new { title = item.Title });

        await db.SaveChangesAsync(); // save the log entry before the deletes below

        // ParentId isn't a real FK, so subtasks don't cascade — gather the whole subtree
        // (this task + all descendant subtasks) ourselves.
        var links = await db.Todos.Select(t => new { t.Id, t.ParentId }).ToListAsync();
        var childrenOf = links.Where(t => t.ParentId.HasValue)
            .GroupBy(t => t.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());
        var subtree = new List<int>();
        var stack = new Stack<int>();
        stack.Push(id);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            subtree.Add(cur);
            if (childrenOf.TryGetValue(cur, out var kids))
                foreach (var k in kids) stack.Push(k);
        }

        // Comment→Todo isn't a FK either, so comments don't cascade. Delete every comment under
        // the subtree AND its attachment files (Path + Preview), so nothing is orphaned on disk.
        var comments = await db.Comments.Include(c => c.Attachments)
            .Where(c => subtree.Contains(c.TodoId)).ToListAsync();
        var uploadsRoot = TodoApi.DataPaths.Uploads;
        foreach (var att in comments.SelectMany(c => c.Attachments))
            foreach (var path in new[] { att.Path, att.Preview })
            {
                if (string.IsNullOrEmpty(path)) continue;
                try
                {
                    var fp = System.IO.Path.Combine(uploadsRoot, System.IO.Path.GetFileName(path));
                    if (System.IO.File.Exists(fp)) System.IO.File.Delete(fp);
                }
                catch { /* ignore a locked/missing file */ }
            }
        db.Comments.RemoveRange(comments); // cascades to CommentAttachments (that IS a FK)
        await db.SaveChangesAsync();

        // Remove the whole subtree of todos. TaskSessions/TaskLogs/TaskManifests cascade (real FKs).
        var todos = await db.Todos.Where(t => subtree.Contains(t.Id)).ToListAsync();
        db.Todos.RemoveRange(todos);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
