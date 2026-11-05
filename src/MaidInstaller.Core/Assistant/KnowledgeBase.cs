using MaidInstaller.Core.Storage;

namespace MaidInstaller.Core.Assistant;

/// <summary>
/// Base de conhecimento local da Maid Assistant (Catalog/knowledge.json).
/// É a ÚNICA fonte de fatos da assistente: ela não responde nada que não esteja aqui.
/// </summary>
public sealed class KnowledgeBase
{
    public int SchemaVersion { get; set; } = 1;
    public List<AppKnowledge> Apps { get; set; } = [];
    public List<TopicKnowledge> Topics { get; set; } = [];

    public static KnowledgeBase Load(string path) =>
        JsonStore.Read<KnowledgeBase>(path) ?? new KnowledgeBase();

    /// <summary>
    /// Passa os textos para o idioma do app (chamar antes de <see cref="AddFromCatalog"/>, com o catálogo já traduzido).
    /// A categoria continua em português: é a chave da busca; ela é traduzida só na hora de mostrar.
    /// As palavras-chave dos temas já valem nos dois idiomas.
    /// </summary>
    public void Localize()
    {
        foreach (var app in Apps)
        {
            app.Summary = L.T(app.Summary);
            app.License = L.T(app.License);
            app.Highlights = [.. app.Highlights.Select(L.T)];
            if (app.Notes is not null) app.Notes = L.T(app.Notes);
        }
        foreach (var topic in Topics) topic.Answer = L.T(topic.Answer);
    }

    /// <summary>
    /// Inclui os apps do catálogo que não têm ficha própria em knowledge.json, usando apenas
    /// os dados verificados do catálogo (descrição, desenvolvedor, preço, licença).
    /// </summary>
    public void AddFromCatalog(Catalog.AppCatalog catalog)
    {
        foreach (var app in catalog.Apps)
        {
            var existing = Apps.FirstOrDefault(a => a.Id == app.Id);
            if (existing is not null)
            {
                existing.HasWinGetPackage = app.HasWinGet;
                continue;
            }
            Apps.Add(new AppKnowledge
            {
                Id = app.Id,
                Name = app.Name,
                Aliases = app.Aliases,
                Category = catalog.FindCategory(app.Category)?.Name ?? app.Category,
                Developer = app.Developer,
                License = app.Price,
                Summary = $"{app.Name}: {app.Description}",
                Notes = app.Notes,
                OfficialUrl = app.OfficialUrl,
                HasWinGetPackage = app.HasWinGet,
            });
        }
    }
}

public sealed class AppKnowledge
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Formas como o usuário pode escrever o nome (ex.: "zap", "obs").</summary>
    public List<string> Aliases { get; set; } = [];
    public string Category { get; set; } = "";
    public string Developer { get; set; } = "";
    public string License { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<string> Highlights { get; set; } = [];
    public string? Notes { get; set; }
    public string? OfficialUrl { get; set; }

    /// <summary>Preenchido a partir do catálogo: dá para instalar pelo Maid Installer (WinGet)?</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool? HasWinGetPackage { get; set; }
}

public sealed class TopicKnowledge
{
    public string Id { get; set; } = "";
    /// <summary>Palavras-chave (sem acento, minúsculas ou não — são normalizadas).</summary>
    public List<string> Keywords { get; set; } = [];
    public string Answer { get; set; } = "";
}
