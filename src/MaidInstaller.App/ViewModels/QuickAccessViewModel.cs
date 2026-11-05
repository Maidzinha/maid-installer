using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media;
using MaidInstaller.App.Mvvm;
using MaidInstaller.App.Services;
using MaidInstaller.Core.Settings;
using MaidInstaller.Core.Shell;

namespace MaidInstaller.App.ViewModels;

/// <summary>Barra de Acesso Rápido: localiza os favoritos instalados (Menu Iniciar) e os abre.</summary>
public sealed class QuickAccessViewModel(AppServices services, AssistantViewModel assistant, Action openLibrary) : ObservableObject
{
    private bool _isLoading = true;
    private int _loadVersion;

    public ObservableCollection<FavoriteItemViewModel> Items { get; } = [];
    public ICommand AddCommand { get; } = new RelayCommand(openLibrary);

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsFavorite(string name) =>
        services.Settings.Favorites.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void Add(FavoriteApp favorite)
    {
        if (IsFavorite(favorite.Name)) return;
        services.Settings.Favorites.Add(favorite);
        services.SaveSettings();
        _ = LoadAsync();
    }

    public void Remove(string name)
    {
        services.Settings.Favorites.RemoveAll(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        services.SaveSettings();
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var favorites = services.Settings.Favorites.ToList();
            var apps = await services.ShellApps.GetAppsAsync();
            var matched = favorites
                .Select(f => (favorite: f, app: ShellAppsFolder.Match(apps, f.MatchNames.Count > 0 ? f.MatchNames : [f.Name])))
                .ToList();
            var icons = await services.ShellApps.GetIconsAsync(matched.Where(m => m.app is not null).Select(m => m.app!));
            if (version != _loadVersion) return; // outra recarga começou depois

            Items.Clear();
            foreach (var (favorite, app) in matched)
                Items.Add(new FavoriteItemViewModel(favorite, app, app is null ? null : icons.GetValueOrDefault(app.ParsingName), Launch));

            services.Logger.Info($"Acesso rápido: {matched.Count(m => m.app is not null)}/{matched.Count} favoritos instalados.");
        }
        catch (Exception ex)
        {
            services.Logger.Error("Falha ao montar o acesso rápido.", ex);
        }
        finally
        {
            if (version == _loadVersion) IsLoading = false;
        }
    }

    private void Launch(FavoriteItemViewModel item)
    {
        if (item.App is null)
        {
            assistant.Post($"O {item.Name} não parece estar instalado neste PC, {services.Settings.UserName}. " +
                           "Você encontra ele na Biblioteca de Apps ♡");
            return;
        }
        try
        {
            // Abre pelo Shell, exatamente como clicar no Menu Iniciar.
            Process.Start(new ProcessStartInfo("explorer.exe", item.App.ShellPath) { UseShellExecute = true });
            services.Logger.Info($"Abrindo favorito: {item.Name} ({item.App.ParsingName})");
        }
        catch (Exception ex)
        {
            services.Logger.Error($"Falha ao abrir {item.Name}.", ex);
            assistant.Post($"Ops, não consegui abrir o {item.Name}. O erro foi registrado nos logs.");
        }
    }
}

public sealed class FavoriteItemViewModel
{
    public FavoriteItemViewModel(FavoriteApp favorite, ShellApp? app, ImageSource? icon, Action<FavoriteItemViewModel> launch)
    {
        Name = favorite.Name;
        App = app;
        Icon = icon;
        LaunchCommand = new RelayCommand(() => launch(this));
    }

    public string Name { get; }
    public ShellApp? App { get; }
    public ImageSource? Icon { get; }
    public bool IsInstalled => App is not null;
    public string Monogram => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public string Tooltip => IsInstalled ? $"Abrir {Name}" : $"{Name} — não instalado";
    public ICommand LaunchCommand { get; }
}
