using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace TodoApi.Controllers;

[ApiController]
[Route("api/files")]
public class FilesController : ControllerBase
{
    /// <summary>
    /// Opens a local file or folder in its default application (equivalent to double-clicking in Explorer).
    /// Only reachable from localhost, so no auth is needed.
    /// </summary>
    [HttpGet("open")]
    public IActionResult Open([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return BadRequest("Cesta je prázdná.");

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
