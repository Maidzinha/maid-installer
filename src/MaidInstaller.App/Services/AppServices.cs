using System.Diagnostics;
using MaidInstaller.Core.Assistant;
using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Logging;
using MaidInstaller.Core.Settings;
using MaidInstaller.Core.Storage;
using MaidInstaller.Core.SystemInfo;
using MaidInstaller.Core.WinGet;

namespace MaidInstaller.App.Services;

/// <summary>Serviços compartilhados, montados uma vez no início (composição manual, sem container).</summary>
public sealed class AppServices
{
    public required AppPaths Paths { get; init; }
    public required IAppLogger Logger { get; init; }
    public required SettingsService SettingsService { get; init; }
    public required AppSettings Settings { get; init; }
    public required ISystemMonitor SystemMonitor { get; init; }
    public required KnowledgeBase Knowledge { get; init; }
    public required HybridResponder Responder { get; init; }
    public required LocalAiService Ai { get; init; }
    public required AppCatalog Catalog { get; init; }
    public required WinGetClient WinGet { get; init; }
    public required ShellAppsService ShellApps { get; init; }
    public required Core.Installation.InstallationService Installer { get; init; }
    public required Core.Installation.InstallHistory History { get; init; }
    public required Core.Collections.CollectionStore Profiles { get; init; }
    public required Core.Collections.CollectionStore Lists { get; init; }

    /// <summary>Perfis prontos do Catalog/profiles.json (para "Restaurar perfis prontos").</summary>
    public IReadOnlyList<Core.Collections.AppCollection> DefaultProfiles { get; init; } = [];

    /// <summary>Ícones reais já encontrados (id do app → ícone), compartilhados entre as telas.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, System.Windows.Media.ImageSource?> AppIcons { get; } = new();

    public string Version { get; } =
        typeof(AppServices).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public void SaveSettings() => SettingsService.Save(Settings);

    /// <summary>Abre um link no navegador padrão. Só aceita HTTPS (links do catálogo validado).</summary>
    public void OpenOfficialUrl(string url)
    {
        if (!CatalogValidator.IsHttps(url))
        {
            Logger.Warn($"Link recusado (não é https): {url}");
            return;
        }
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        Logger.Info($"Abrindo site oficial: {url}");
    }
}
