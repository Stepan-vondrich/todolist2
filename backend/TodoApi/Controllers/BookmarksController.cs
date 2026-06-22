using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TodoApi.Data;
using TodoApi.Models;

namespace TodoApi.Controllers;

[ApiController]
[Route("api/bookmarks")]
public class BookmarksController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.FilterBookmarks.OrderBy(b => b.Id).ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FilterBookmark bookmark)
    {
        bookmark.Id = 0;
        db.FilterBookmarks.Add(bookmark);
        await db.SaveChangesAsync();
        return Ok(bookmark);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var bookmark = await db.FilterBookmarks.FindAsync(id);
        if (bookmark == null) return NotFound();
        db.FilterBookmarks.Remove(bookmark);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
