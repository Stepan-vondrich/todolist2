using YamlDotNet.Serialization;

namespace TodoApi.Services;

/// <summary>
/// YAML projection of the manifest. Kept separate from the EF entities so the
/// Czech snake_case keys live only here (via [YamlMember(Alias=...)]).
/// </summary>
public class ManifestDto
{
    [YamlMember(Alias = "nastaveni")]
    public NastaveniDto? Nastaveni { get; set; }

    [YamlMember(Alias = "tasky")]
    public List<ManifestTaskDto> Tasky { get; set; } = new();
}

public class NastaveniDto
{
    [YamlMember(Alias = "horizont_planovani")]
    public string? HorizontPlanovani { get; set; }

    [YamlMember(Alias = "pracovni_doba")]
    public Dictionary<string, string>? PracovniDoba { get; set; }

    [YamlMember(Alias = "okna_dne")]
    public Dictionary<string, string>? OknaDne { get; set; }

    [YamlMember(Alias = "reakce_lidi")]
    public Dictionary<string, string>? ReakceLidi { get; set; }
}

public class ManifestTaskDto
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "title")]
    public string Title { get; set; } = string.Empty;

    [YamlMember(Alias = "odhad")]
    public string Odhad { get; set; } = string.Empty;

    // dependencies is required in spirit (key must be present) but may be an empty list.
    [YamlMember(Alias = "dependencies")]
    public List<string>? Dependencies { get; set; }

    [YamlMember(Alias = "muzu_zacit")]
    public string? MuzuZacit { get; set; }

    [YamlMember(Alias = "status")]
    public string? Status { get; set; }

    [YamlMember(Alias = "deadline")]
    public string? Deadline { get; set; }

    [YamlMember(Alias = "kdy")]
    public List<string>? Kdy { get; set; }

    [YamlMember(Alias = "jen_v_praci")]
    public bool? JenVPraci { get; set; }

    [YamlMember(Alias = "muze_bezet_s")]
    public List<string>? MuzeBezetS { get; set; }

    [YamlMember(Alias = "ceka_na_cloveka")]
    public CekaNaClovekaDto? CekaNaCloveka { get; set; }

    [YamlMember(Alias = "pevny_cas")]
    public string? PevnyCas { get; set; }

    [YamlMember(Alias = "periodicita")]
    public string? Periodicita { get; set; }
}

public class CekaNaClovekaDto
{
    [YamlMember(Alias = "kdo")]
    public string? Kdo { get; set; }

    [YamlMember(Alias = "reakce")]
    public string? Reakce { get; set; }
}
