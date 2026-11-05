using MaidInstaller.Core.Shell;
using MaidInstaller.Core.Storage;
using MaidInstaller.Core.WinGet;

namespace MaidInstaller.Core.Catalog;

public enum InstallState
{
    /// <summary>Ainda verificando.</summary>
    Unknown,
    NotInstalled,
    /// <summary>Reconhecido pelo WinGet (com versão).</summary>
    Installed,
    /// <summary>Não reconhecido pelo WinGet, mas presente no Menu Iniciar.</summary>
    DetectedInStartMenu,
    /// <summary>Sem WinGet e fora do Menu Iniciar: não dá para afirmar se está instalado.</summary>
    NotVerified,
}

public sealed record AppInstallStatus(InstallState State, string? InstalledVersion);

/// <summary>Cruza o catálogo com o que o WinGet e o Menu Iniciar dizem estar instalado.</summary>
public static class InstallStatusResolver
{
    public static AppInstallStatus Resolve(CatalogApp app, IReadOnlyList<InstalledPackage>? winget, IReadOnlyList<ShellApp>? shell)
    {
        if (winget is not null && app.WinGetId is not null)
        {
            var pkg = winget.FirstOrDefault(p => p.Id.Equals(app.WinGetId, StringComparison.OrdinalIgnoreCase));
            if (pkg is not null) return new AppInstallStatus(InstallState.Installed, pkg.Version);
        }
        if (shell is not null && app.StartMenuNames.Count > 0 && ShellAppsFolder.Match(shell, app.StartMenuNames) is not null)
            return new AppInstallStatus(InstallState.DetectedInStartMenu, null);

        if (winget is null && shell is null) return new AppInstallStatus(InstallState.Unknown, null);
        // Sem a lista do WinGet, "não achei no Menu Iniciar" não prova que não está instalado.
        return winget is null
            ? new AppInstallStatus(InstallState.NotVerified, null)
            : new AppInstallStatus(InstallState.NotInstalled, null);
    }
}

/// <summary>Cache da última leitura do WinGet (Cache/installed-cache.json), para a Biblioteca abrir na hora.</summary>
public sealed class InstalledCache
{
    public DateTime CheckedAt { get; set; }
    public List<InstalledPackage> Packages { get; set; } = [];

    public static InstalledCache? Load(string file)
    {
        try { return JsonStore.Read<InstalledCache>(file); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { return null; }
    }

    public void Save(string file)
    {
        try { JsonStore.WriteAtomic(file, this); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
