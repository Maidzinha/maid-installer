using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Logging;
using MaidInstaller.Core.Storage;

namespace MaidInstaller.Core.Collections;

/// <summary>Perfis prontos (PC Gamer, PC Trabalho, PC Completo), definidos em Catalog/profiles.json.</summary>
public sealed class DefaultProfilesFile
{
    public int SchemaVersion { get; set; } = 1;
    public List<AppCollection> Profiles { get; set; } = [];
}

public static class DefaultProfiles
{
    public static IReadOnlyList<AppCollection> Load(string path, AppCatalog catalog, IAppLogger logger)
    {
        var file = JsonStore.Read<DefaultProfilesFile>(path);
        if (file is null) return [];
        var result = new List<AppCollection>();
        foreach (var p in file.Profiles)
        {
            if (!AppCollection.IsValidId(p.BuiltInId) || AppCollection.CleanName(p.Name) is null)
            {
                logger.Warn($"Perfil pronto inválido ignorado: {p.Name}");
                continue;
            }
            var missing = p.AppIds.Where(id => catalog.FindApp(id) is null).ToList();
            if (missing.Count > 0) logger.Warn($"Perfil pronto {p.BuiltInId}: apps fora do catálogo ignorados: {string.Join(", ", missing)}");
            p.AppIds = [.. p.AppIds.Except(missing).Distinct()];
            p.Id = p.BuiltInId!;
            result.Add(p);
        }
        return result;
    }

    /// <summary>
    /// Grava os perfis prontos que ainda não foram criados nesta pasta de dados. Um perfil pronto que o usuário
    /// excluiu não volta sozinho (só pelo botão "Restaurar perfis prontos", com <paramref name="force"/>).
    /// </summary>
    /// <returns>Quantos perfis foram criados.</returns>
    public static int Seed(CollectionStore store, IReadOnlyList<AppCollection> defaults, IReadOnlyList<AppCollection> existing,
        ICollection<string> alreadySeeded, bool force = false)
    {
        var created = 0;
        foreach (var d in defaults)
        {
            if (existing.Any(e => e.BuiltInId == d.BuiltInId)) { if (!alreadySeeded.Contains(d.BuiltInId!)) alreadySeeded.Add(d.BuiltInId!); continue; }
            if (!force && alreadySeeded.Contains(d.BuiltInId!)) continue;

            var copy = d.Clone();
            copy.Name = CollectionStore.UniqueName(d.Name, existing.Select(e => e.Name));
            copy.Id = File.Exists(store.PathFor(d.BuiltInId!)) ? AppCollection.NewId(copy.Name) : d.BuiltInId!;
            copy.CreatedAt = DateTime.Now;
            store.Save(copy);
            if (!alreadySeeded.Contains(d.BuiltInId!)) alreadySeeded.Add(d.BuiltInId!);
            created++;
        }
        return created;
    }
}
