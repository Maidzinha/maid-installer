using System.Text.Json;
using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Storage;

namespace MaidInstaller.Core.Collections;

/// <summary>Formato do arquivo exportado (.maidlist.json), para levar perfis e listas para outro PC.</summary>
public sealed class CollectionFile
{
    public const string FormatName = "maid-installer/colecao";

    public string Format { get; set; } = FormatName;
    public int SchemaVersion { get; set; } = 1;
    public string Kind { get; set; } = "list";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime ExportedAt { get; set; } = DateTime.Now;

    /// <summary>Id do catálogo + ID WinGet + nome: o arquivo continua legível para humanos e casa com versões futuras do catálogo.</summary>
    public List<CollectionFileApp> Apps { get; set; } = [];
}

public sealed class CollectionFileApp
{
    public string Id { get; set; } = "";
    public string? WinGetId { get; set; }
    public string? Name { get; set; }
}

/// <param name="Collection">Coleção pronta para salvar (id e nome ainda serão ajustados pelo app).</param>
/// <param name="Unknown">Apps do arquivo que não existem neste catálogo (são descartados, nunca instalados).</param>
public sealed record ImportResult(AppCollection Collection, IReadOnlyList<string> Unknown, CollectionKind SourceKind);

public static class CollectionTransfer
{
    public const string FileExtension = ".maidlist.json";
    public const long MaxFileBytes = 512 * 1024;
    public const int MaxApps = 500;

    public static string KindToText(CollectionKind kind) => kind == CollectionKind.Profile ? "profile" : "list";

    public static void Export(AppCollection collection, CollectionKind kind, AppCatalog catalog, string path)
    {
        var file = new CollectionFile
        {
            Kind = KindToText(kind),
            Name = collection.Name,
            Description = collection.Description,
            Apps = [.. collection.AppIds.Select(id => catalog.FindApp(id) is { } app
                ? new CollectionFileApp { Id = app.Id, WinGetId = app.WinGetId, Name = app.Name }
                : new CollectionFileApp { Id = id })],
        };
        JsonStore.WriteAtomic(path, file);
    }

    /// <summary>
    /// Lê um arquivo exportado. Só aceita apps que existem no catálogo verificado (casando pelo id ou pelo ID WinGet):
    /// um arquivo de outra pessoa não consegue fazer o app instalar algo fora do catálogo.
    /// </summary>
    /// <exception cref="InvalidDataException">Arquivo inválido, grande demais ou de outro formato (mensagem em português).</exception>
    public static ImportResult Import(string path, CollectionKind targetKind, AppCatalog catalog)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new InvalidDataException(L.T("O arquivo não foi encontrado."));
        if (info.Length > MaxFileBytes) throw new InvalidDataException(L.T("O arquivo é grande demais para ser uma lista do Maid Installer."));

        CollectionFile? file;
        try { file = JsonStore.Read<CollectionFile>(path); }
        catch (JsonException) { throw new InvalidDataException(L.T("O arquivo não é um JSON válido.")); }

        if (file is null || file.Format != CollectionFile.FormatName)
            throw new InvalidDataException(L.T("Este arquivo não foi exportado pelo Maid Installer."));
        if (file.SchemaVersion > 1)
            throw new InvalidDataException(L.T("Este arquivo foi criado por uma versão mais nova do Maid Installer."));
        if (file.Apps is null || file.Apps.Count > MaxApps)
            throw new InvalidDataException(L.T("A lista de aplicativos do arquivo é inválida."));

        var ids = new List<string>();
        var unknown = new List<string>();
        foreach (var entry in file.Apps)
        {
            if (entry is null) continue;
            var app = catalog.FindApp(entry.Id ?? "")
                      ?? (entry.WinGetId is { Length: > 0 } wid
                          ? catalog.Apps.FirstOrDefault(a => string.Equals(a.WinGetId, wid, StringComparison.OrdinalIgnoreCase))
                          : null);
            if (app is null) unknown.Add(Shorten(entry.Name ?? entry.WinGetId ?? entry.Id ?? "?"));
            else if (!ids.Contains(app.Id)) ids.Add(app.Id);
        }

        var sourceKind = file.Kind == "profile" ? CollectionKind.Profile : CollectionKind.List;
        var name = AppCollection.CleanName(file.Name) ?? "Importado";
        var description = file.Description is { Length: > 0 } d ? Shorten(d, 300) : null;
        var collection = new AppCollection
        {
            Name = name,
            Description = description,
            Glyph = targetKind == CollectionKind.Profile ? "" : "",
            AppIds = ids,
        };
        return new ImportResult(collection, unknown, sourceKind);
    }

    private static string Shorten(string text, int max = 80)
    {
        text = new string([.. text.Where(c => !char.IsControl(c))]).Trim();
        return text.Length > max ? text[..max] + "…" : text;
    }
}
