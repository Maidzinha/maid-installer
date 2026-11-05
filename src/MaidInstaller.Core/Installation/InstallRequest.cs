using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.WinGet;

namespace MaidInstaller.Core.Installation;

/// <summary>Como um pedido de instalação foi separado antes da confirmação.</summary>
public sealed record InstallRequestPlan(
    IReadOnlyList<CatalogApp> ToInstall,
    IReadOnlyList<CatalogApp> AlreadyInstalled,
    IReadOnlyList<CatalogApp> InQueue,
    IReadOnlyList<CatalogApp> NoPackage);

/// <summary>
/// Separa um pedido de instalação (Biblioteca, Perfis, Listas, Kit) em: o que será instalado, o que já está
/// instalado (pulado), o que já está na fila e o que não tem pacote WinGet válido. Cada app cai em um só grupo.
/// </summary>
public static class InstallRequest
{
    public static InstallRequestPlan Plan(IEnumerable<CatalogApp> apps, Func<CatalogApp, bool> isInstalled, IEnumerable<InstallJob> jobs)
    {
        var active = jobs.Where(j => !j.IsFinished).Select(j => j.App.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<CatalogApp> toInstall = [], already = [], inQueue = [], noPackage = [];
        foreach (var app in apps.DistinctBy(a => a.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (!WinGetClient.IsValidPackage(app)) noPackage.Add(app);
            else if (active.Contains(app.Id)) inQueue.Add(app);
            else if (isInstalled(app)) already.Add(app);
            else toInstall.Add(app);
        }
        return new InstallRequestPlan(toInstall, already, inQueue, noPackage);
    }
}
