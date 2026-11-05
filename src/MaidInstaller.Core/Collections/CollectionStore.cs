using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Logging;
using MaidInstaller.Core.Storage;

namespace MaidInstaller.Core.Collections;

/// <summary>
/// Lê e grava perfis ou listas, um arquivo JSON por coleção. Os arquivos ficam na pasta de dados do usuário
/// (ou do pendrive), separados dos arquivos do programa — por isso sobrevivem a atualizações do app.
/// </summary>
public sealed class CollectionStore(string directory, CollectionKind kind, IAppLogger logger)
{
    public string Directory { get; } = directory;
    public CollectionKind Kind { get; } = kind;

    private string KindName => Kind == CollectionKind.Profile ? "perfil" : "lista";

    public string PathFor(string id) =>
        AppCollection.IsValidId(id) ? Path.Combine(Directory, id + ".json") : throw new ArgumentException($"Id inválido: {id}", nameof(id));

    /// <summary>Carrega todas as coleções válidas. Arquivos com erro são ignorados (e registrados no log), nunca apagados.</summary>
    public IReadOnlyList<AppCollection> LoadAll(AppCatalog? catalog = null)
    {
        var result = new List<AppCollection>();
        if (!System.IO.Directory.Exists(Directory)) return result;

        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            try
            {
                var c = JsonStore.Read<AppCollection>(file);
                var expectedId = Path.GetFileNameWithoutExtension(file);
                if (c is null || AppCollection.CleanName(c.Name) is not { } name || c.Id != expectedId || !AppCollection.IsValidId(c.Id))
                {
                    logger.Warn($"Arquivo de {KindName} ignorado (formato inválido): {file}");
                    continue;
                }
                c.Name = name;
                c.AppIds = Normalize(c.AppIds, catalog, file);
                result.Add(c);
            }
            catch (Exception ex)
            {
                logger.Error($"Não foi possível ler o arquivo de {KindName}: {file}", ex);
            }
        }
        return [.. result.OrderBy(c => c.IsBuiltIn ? 0 : 1).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Remove repetidos e (se houver catálogo) apps que não existem mais nele.</summary>
    private List<string> Normalize(List<string>? ids, AppCatalog? catalog, string file)
    {
        var clean = (ids ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        if (catalog is null) return clean;
        var missing = clean.Where(id => catalog.FindApp(id) is null).ToList();
        if (missing.Count > 0) logger.Warn($"{file}: apps fora do catálogo ignorados: {string.Join(", ", missing)}");
        return [.. clean.Except(missing)];
    }

    public void Save(AppCollection collection)
    {
        var name = AppCollection.CleanName(collection.Name) ?? throw new ArgumentException("O nome não pode ficar vazio.", nameof(collection));
        if (!AppCollection.IsValidId(collection.Id)) collection.Id = AppCollection.NewId(name);
        collection.Name = name;
        collection.AppIds = [.. collection.AppIds.Distinct(StringComparer.Ordinal)];
        // Nunca rebaixa: um arquivo de uma versão mais nova continua marcado como tal.
        collection.SchemaVersion = Math.Max(collection.SchemaVersion, AppCollection.CurrentSchemaVersion);
        collection.UpdatedAt = DateTime.Now;
        JsonStore.WriteAtomic(PathFor(collection.Id), collection);
        logger.Info($"{KindName} salvo(a): {collection.Name} ({collection.AppIds.Count} apps) → {collection.Id}.json");
    }

    public void Delete(AppCollection collection)
    {
        var path = PathFor(collection.Id);
        if (File.Exists(path)) File.Delete(path);
        logger.Info($"{KindName} excluído(a): {collection.Name} ({collection.Id}.json)");
    }

    /// <summary>Cria uma cópia com nome e id novos (a cópia nunca é um "perfil pronto").</summary>
    public AppCollection Duplicate(AppCollection source, IEnumerable<string> existingNames)
    {
        var copy = source.Clone();
        copy.Name = UniqueName($"{source.Name} (cópia)", existingNames);
        copy.Id = AppCollection.NewId(copy.Name);
        copy.BuiltInId = null;
        copy.CreatedAt = DateTime.Now;
        Save(copy);
        return copy;
    }

    /// <summary>"Nome", "Nome 2", "Nome 3"… sem repetir um nome existente (sem diferenciar maiúsculas).</summary>
    public static string UniqueName(string baseName, IEnumerable<string> existingNames)
    {
        var name = AppCollection.CleanName(baseName) ?? "Sem nome";
        var taken = existingNames.ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!taken.Contains(name)) return name;
        for (var i = 2; ; i++)
        {
            var suffix = $" {i}";
            var candidate = (name.Length + suffix.Length > AppCollection.MaxNameLength ? name[..(AppCollection.MaxNameLength - suffix.Length)] : name) + suffix;
            if (!taken.Contains(candidate)) return candidate;
        }
    }
}
