using Microsoft.AspNetCore.Mvc;
using TodoApi.Services;

namespace TodoApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ManifestController(ManifestService manifest) : ControllerBase
{
    public record YamlDto(string Yaml);

    /// <summary>Canonical YAML rendered from the DB (the source of truth).</summary>
    [HttpGet]
    public async Task<IActionResult> Get() =>
        Ok(new YamlDto(manifest.Serialize(await manifest.BuildFromDbAsync())));

    /// <summary>Accept edited YAML text → validate → apply to DB → mirror to file verbatim.</summary>
    [HttpPut]
    public async Task<IActionResult> Put([FromBody] YamlDto body)
    {
        try
        {
            var dto = manifest.Deserialize(body.Yaml ?? string.Empty);
            await manifest.ApplyToDbAsync(dto);
            await manifest.WriteTextAndRecordAsync(body.Yaml ?? string.Empty);
            return Ok(new YamlDto(manifest.Serialize(await manifest.BuildFromDbAsync())));
        }
        catch (ManifestValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Has the file on disk been hand-edited since we last wrote it?</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status() => Ok(await manifest.GetStatusAsync());

    /// <summary>Adopt the on-disk file into the DB.</summary>
    [HttpPost("reload")]
    public async Task<IActionResult> Reload()
    {
        try
        {
            await manifest.ReloadFromFileAsync();
            return Ok(new YamlDto(manifest.Serialize(await manifest.BuildFromDbAsync())));
        }
        catch (ManifestValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
