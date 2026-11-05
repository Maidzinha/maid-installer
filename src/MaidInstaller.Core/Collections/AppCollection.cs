using System.Text.RegularExpressions;

namespace MaidInstaller.Core.Collections;

/// <summary>Perfis de Instalação e Minhas Listas usam o mesmo formato: um nome e os apps do catálogo.</summary>
public enum CollectionKind { Profile, List }

/// <summary>
/// Um perfil ou uma lista salva em JSON (Profiles/&lt;id&gt;.json ou Lists/&lt;id&gt;.json).
/// Guarda só os ids do catálogo — nunca comandos nem IDs WinGet soltos.
/// </summary>
public sealed partial class AppCollection
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxNameLength = 60;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Glyph { get; set; } = "";

    /// <summary>Id do perfil pronto de origem (pc-gamer, pc-trabalho…); null para os criados pelo usuário.</summary>
    public string? BuiltInId { get; set; }

    public List<string> AppIds { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>Campos gravados por uma versão mais nova do app; são regravados sem alteração.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtraFields { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsBuiltIn => BuiltInId is not null;

    public AppCollection Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        ExtraFields = ExtraFields is null ? null : new(ExtraFields),
        Id = Id,
        Name = Name,
        Description = Description,
        Glyph = Glyph,
        BuiltInId = BuiltInId,
        AppIds = [.. AppIds],
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
    };

    /// <summary>Id seguro para nome de arquivo: letras minúsculas, números e hífens.</summary>
    public static bool IsValidId(string? id) => id is { Length: > 0 and <= 80 } && IdRegex().IsMatch(id);

    /// <summary>Cria um id novo a partir do nome (ex.: "Meu PC Gamer" → "meu-pc-gamer-3fa1c2").</summary>
    public static string NewId(string name)
    {
        var slug = Assistant.OfflineResponder.Normalize(name);
        slug = SlugRegex().Replace(slug, "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        if (slug.Length == 0) slug = "colecao";
        return $"{slug}-{Guid.NewGuid():N}"[..(slug.Length + 7)];
    }

    /// <summary>Limpa o nome digitado. Retorna null se ficar vazio.</summary>
    public static string? CleanName(string? name)
    {
        if (name is null) return null;
        var clean = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        clean = new string([.. clean.Where(c => !char.IsControl(c))]);
        if (clean.Length > MaxNameLength) clean = clean[..MaxNameLength].TrimEnd();
        return clean.Length == 0 ? null : clean;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdRegex();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugRegex();
}
