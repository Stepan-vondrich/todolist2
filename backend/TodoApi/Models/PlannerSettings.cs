namespace TodoApi.Models;

/// <summary>
/// Single-row store (Id is always 1) holding the planner's global <c>nastaveni</c>
/// block plus manifest-file sync state, all as one JSON blob. Read whole on every
/// plan computation and never queried by field, so a JSON blob is the right shape.
/// </summary>
public class PlannerSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>
    /// JSON object: { nastaveni: {...}, manifestFileHash: "...", manifestFileMtimeUtc: "..." }.
    /// See <c>PlannerSettingsData</c> in ManifestService for the parsed shape.
    /// </summary>
    public string Json { get; set; } = string.Empty;
}
