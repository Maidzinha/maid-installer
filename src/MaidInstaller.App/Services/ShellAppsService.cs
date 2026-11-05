using System.Windows.Media.Imaging;
using MaidInstaller.Core.Logging;
using MaidInstaller.Core.Shell;

namespace MaidInstaller.App.Services;

/// <summary>Lista do Menu Iniciar e ícones, lidos uma vez e compartilhados entre as telas.</summary>
public sealed class ShellAppsService(IAppLogger logger)
{
    private Task<IReadOnlyList<ShellApp>>? _apps;
    private readonly Dictionary<string, BitmapSource?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    public Task<IReadOnlyList<ShellApp>> GetAppsAsync()
    {
        lock (_lock)
        {
            return _apps ??= LoadAsync();
        }
    }

    /// <summary>Esquece a lista (ex.: depois de instalar algo) para ler de novo.</summary>
    public void Invalidate()
    {
        lock (_lock) _apps = null;
    }

    private async Task<IReadOnlyList<ShellApp>> LoadAsync()
    {
        try
        {
            var apps = await StaThread.Run(ShellAppsFolder.Enumerate);
            logger.Info($"Menu Iniciar: {apps.Count} itens.");
            return apps;
        }
        catch (Exception ex)
        {
            logger.Error("Não foi possível ler o Menu Iniciar.", ex);
            return [];
        }
    }

    /// <summary>Carrega (e guarda) os ícones dos apps informados numa thread STA.</summary>
    public async Task<IReadOnlyDictionary<string, BitmapSource?>> GetIconsAsync(IEnumerable<ShellApp> apps)
    {
        List<ShellApp> missing;
        lock (_lock) missing = apps.DistinctBy(a => a.ParsingName).Where(a => !_icons.ContainsKey(a.ParsingName)).ToList();

        if (missing.Count > 0)
        {
            var loaded = await StaThread.Run(() => missing.Select(a => (a.ParsingName, icon: ShellIconLoader.Load(a.ShellPath))).ToList());
            lock (_lock) foreach (var (key, icon) in loaded) _icons[key] = icon;
        }

        lock (_lock) return new Dictionary<string, BitmapSource?>(_icons, StringComparer.OrdinalIgnoreCase);
    }
}
