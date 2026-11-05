using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MaidInstaller.App.Mvvm;
using MaidInstaller.App.Services;
using MaidInstaller.Core.Assistant;
using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Settings;
using MaidInstaller.Core.Shell;

namespace MaidInstaller.App.ViewModels;

public enum StatusFilter { All, Installed, NotInstalled }

public sealed record CategoryFilter(string? Id, string Name, string Glyph, int Count)
{
    public override string ToString() => $"{Name} ({Count})";
}

/// <summary>
/// Biblioteca de Apps: catálogo verificado, categorias, pesquisa, filtros e seleção.
/// Os botões Instalar passam pela confirmação da <see cref="MainViewModel"/> antes de ir para a fila.
/// </summary>
public sealed class LibraryViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly QuickAccessViewModel _quickAccess;
    private readonly MainViewModel _main;
    private readonly string _cacheFile;
    private CategoryFilter _selectedCategory;
    private string _searchText = "";
    private StatusFilter _statusFilter = StatusFilter.All;
    private bool _onlyFree, _onlyWinGet;
    private AppItemViewModel? _current;
    private bool _isRefreshing;
    private string _statusLine = L.T("Verificando programas instalados…");
    private int _selectedCount;
    private IReadOnlyList<Core.WinGet.InstalledPackage>? _winget;
    private IReadOnlyList<ShellApp>? _shell;

    public LibraryViewModel(AppServices services, QuickAccessViewModel quickAccess, MainViewModel main)
    {
        _services = services;
        _quickAccess = quickAccess;
        _main = main;
        _cacheFile = Path.Combine(services.Paths.CacheDir, "installed-cache.json");
        var catalog = services.Catalog;

        foreach (var app in catalog.Apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new AppItemViewModel(app, L.T(catalog.FindCategory(app.Category)?.Name ?? app.Category), OnSelectionChanged);
            item.IsFavorite = quickAccess.IsFavorite(app.Name);
            item.Icon = BundledIcons.Get(app.Id); // ícone oficial que vem com o app; o do Windows substitui quando existir
            Apps.Add(item);
        }

        Categories =
        [
            new CategoryFilter(null, L.T("Todas"), "", catalog.Apps.Count),
            .. catalog.Categories.Select(c => new CategoryFilter(c.Id, L.T(c.Name), c.Glyph, catalog.Apps.Count(a => a.Category == c.Id))),
        ];
        _selectedCategory = Categories[0];

        AppsView = CollectionViewSource.GetDefaultView(Apps);
        AppsView.Filter = o => o is AppItemViewModel item && Matches(item);
        Current = Apps.FirstOrDefault();

        SelectVisibleCommand = new RelayCommand(() => { foreach (var a in AppsView.Cast<AppItemViewModel>()) a.IsSelected = true; });
        ClearSelectionCommand = new RelayCommand(() => { foreach (var a in Apps) a.IsSelected = false; }, () => SelectedCount > 0);
        RefreshCommand = new RelayCommand(() => _ = RefreshStatusAsync(), () => !IsRefreshing);
        OpenSiteCommand = new RelayCommand(p => { if (p is AppItemViewModel a) _services.OpenOfficialUrl(a.App.OfficialUrl); });
        CheckVersionCommand = new RelayCommand(p => { if (p is AppItemViewModel a) _ = CheckLiveVersionAsync(a); },
            p => p is AppItemViewModel { App.HasWinGet: true, IsCheckingVersion: false });
        ToggleFavoriteCommand = new RelayCommand(p => { if (p is AppItemViewModel a) ToggleFavorite(a); });
        InstallCurrentCommand = new RelayCommand(
            p => { if (p is AppItemViewModel a) _ = RequestInstallAsync([a]); },
            p => p is AppItemViewModel { CanInstall: true });
        InstallSelectedCommand = new RelayCommand(
            () => _ = RequestInstallAsync([.. Apps.Where(a => a.IsSelected)]),
            () => SelectedCount > 0);
        SaveAsListCommand = new RelayCommand(() =>
        {
            var ids = Apps.Where(a => a.IsSelected).Select(a => a.App.Id).ToList();
            foreach (var a in Apps) a.IsSelected = false;
            _main.SaveAsList(ids);
        }, () => SelectedCount > 0);
        ClearFiltersCommand = new RelayCommand(() =>
        {
            SearchText = "";
            StatusFilter = StatusFilter.All;
            OnlyFree = OnlyWinGet = false;
            SelectedCategory = Categories[0];
        });

        LoadCachedStatus();
        _initTask = InitializeAsync();
    }

    public ObservableCollection<AppItemViewModel> Apps { get; } = [];
    public ICollectionView AppsView { get; }
    public IReadOnlyList<CategoryFilter> Categories { get; }

    public ICommand SelectVisibleCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenSiteCommand { get; }
    public ICommand CheckVersionCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand ClearFiltersCommand { get; }
    public ICommand InstallCurrentCommand { get; }
    public ICommand InstallSelectedCommand { get; }
    public ICommand SaveAsListCommand { get; }

    public AppItemViewModel? Find(string appId) => Apps.FirstOrDefault(a => a.App.Id == appId);

    private readonly Task _initTask;

    /// <summary>Espera a primeira verificação de instalados (WinGet + Menu Iniciar) terminar.</summary>
    public async Task WaitForStatusAsync()
    {
        try { await _initTask; } catch (Exception ex) { _services.Logger.Error("Falha na verificação inicial.", ex); }
    }

    private async Task RequestInstallAsync(IReadOnlyList<AppItemViewModel> items)
    {
        var installed = items.Where(i => i.IsInstalled).Select(i => i.App.Id).ToHashSet();
        var queued = await _main.RequestInstallAsync(items.Select(i => i.App), a => installed.Contains(a.Id));
        if (queued) foreach (var a in Apps) a.IsSelected = false;
    }

    public string Subtitle =>
        L.T($"{_services.Catalog.Apps.Count} aplicativos · identificadores WinGet verificados em {_services.Catalog.VerifiedOn ?? "—"}");

    public CategoryFilter SelectedCategory
    {
        get => _selectedCategory;
        set { if (value is not null && SetProperty(ref _selectedCategory, value)) ApplyFilter(); }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value ?? "")) ApplyFilter(); }
    }

    public StatusFilter StatusFilter
    {
        get => _statusFilter;
        set { if (SetProperty(ref _statusFilter, value)) ApplyFilter(); }
    }

    public bool OnlyFree
    {
        get => _onlyFree;
        set { if (SetProperty(ref _onlyFree, value)) ApplyFilter(); }
    }

    public bool OnlyWinGet
    {
        get => _onlyWinGet;
        set { if (SetProperty(ref _onlyWinGet, value)) ApplyFilter(); }
    }

    public AppItemViewModel? Current
    {
        get => _current;
        set => SetProperty(ref _current, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetProperty(ref _isRefreshing, value);
    }

    public string StatusLine
    {
        get => _statusLine;
        private set => SetProperty(ref _statusLine, value);
    }

    public int SelectedCount
    {
        get => _selectedCount;
        private set
        {
            if (SetProperty(ref _selectedCount, value)) OnPropertyChanged(nameof(SelectionText));
        }
    }

    public string SelectionText => SelectedCount switch
    {
        0 => L.T("Nenhum aplicativo selecionado"),
        1 => L.T("1 aplicativo selecionado"),
        var n => L.T($"{n} aplicativos selecionados"),
    };

    public int VisibleCount => AppsView.Cast<object>().Count();

    public IEnumerable<CatalogApp> SelectedApps => Apps.Where(a => a.IsSelected).Select(a => a.App);

    // ---------- Filtro ----------

    private bool Matches(AppItemViewModel item)
    {
        var app = item.App;
        if (SelectedCategory.Id is not null && app.Category != SelectedCategory.Id) return false;
        if (OnlyFree && !app.IsFree) return false;
        if (OnlyWinGet && !app.HasWinGet) return false;
        if (StatusFilter == StatusFilter.Installed && !item.IsInstalled) return false;
        if (StatusFilter == StatusFilter.NotInstalled && (item.IsInstalled || item.Status.State is InstallState.Unknown or InstallState.NotVerified)) return false;

        var q = OfflineResponder.Normalize(SearchText);
        if (q.Length == 0) return true;
        var haystack = OfflineResponder.Normalize(string.Join(' ',
            [app.Name, app.Developer, app.Description, item.CategoryName, app.WinGetId ?? "", .. app.Aliases]));
        return q.Split(' ').All(token => haystack.Contains(Stem(token)));
    }

    /// <summary>Radical simples para o português: "gravar" encontra "gravação", "navegadores" encontra "navegador".</summary>
    private static string Stem(string token) => token.Length >= 6 ? token[..^2] : token;

    private void ApplyFilter()
    {
        AppsView.Refresh();
        OnPropertyChanged(nameof(VisibleCount));
        if (Current is null || !Matches(Current)) Current = AppsView.Cast<AppItemViewModel>().FirstOrDefault();
    }

    private void OnSelectionChanged() => SelectedCount = Apps.Count(a => a.IsSelected);

    // ---------- Status de instalação ----------

    private void LoadCachedStatus()
    {
        var cache = InstalledCache.Load(_cacheFile);
        if (cache is null) return;
        _winget = cache.Packages;
        UpdateStatuses();
        StatusLine = L.T($"Última verificação: {cache.CheckedAt.ToLocalTime():dd/MM HH:mm} · atualizando…");
    }

    private async Task InitializeAsync()
    {
        _shell = await _services.ShellApps.GetAppsAsync();
        UpdateStatuses();

        var matches = Apps
            .Select(a => (item: a, shell: ShellAppsFolder.Match(_shell, a.App.StartMenuNames)))
            .Where(x => x.shell is not null && x.item.App.StartMenuNames.Count > 0)
            .ToList();
        var icons = await _services.ShellApps.GetIconsAsync(matches.Select(m => m.shell!));
        foreach (var (item, shell) in matches)
        {
            if (icons.GetValueOrDefault(shell!.ParsingName) is not { } real) continue;
            item.Icon = real;
            _services.AppIcons[item.App.Id] = real; // reaproveitado nas telas de instalações e atualizações
        }

        await RefreshStatusAsync();
    }

    public async Task RefreshStatusAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        StatusLine = L.T("Perguntando ao WinGet o que está instalado… (pode levar alguns segundos)");
        try
        {
            if (!await _services.WinGet.IsAvailableAsync())
            {
                StatusLine = L.T("WinGet não encontrado neste PC — status pelo Menu Iniciar. (Instale o 'Instalador de Aplicativo' da Microsoft Store.)");
                _winget = null;
                UpdateStatuses();
                return;
            }
            var packages = await _services.WinGet.GetInstalledAsync();
            _winget = packages;
            new InstalledCache { CheckedAt = DateTime.UtcNow, Packages = [.. packages] }.Save(_cacheFile);
            UpdateStatuses();
            StatusLine = L.T($"Verificado pelo WinGet às {DateTime.Now:HH:mm} · {Apps.Count(a => a.IsInstalled)} do catálogo instalados");
        }
        catch (Exception ex)
        {
            _services.Logger.Error("Falha ao verificar programas instalados.", ex);
            StatusLine = L.T("Não consegui consultar o WinGet agora. Detalhes nos logs.");
        }
        finally
        {
            IsRefreshing = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void UpdateStatuses()
    {
        foreach (var item in Apps)
            item.Status = InstallStatusResolver.Resolve(item.App, _winget, _shell);
        if (StatusFilter != StatusFilter.All) ApplyFilter();
    }

    private async Task CheckLiveVersionAsync(AppItemViewModel item)
    {
        item.IsCheckingVersion = true;
        try
        {
            var version = await _services.WinGet.GetAvailableVersionAsync(item.App);
            item.LiveVersion = version ?? L.T("não informada pela fonte");
        }
        catch (Exception ex)
        {
            _services.Logger.Error($"Falha ao consultar versão de {item.App.Name}.", ex);
            item.LiveVersion = L.T("falha na consulta");
        }
        finally
        {
            item.IsCheckingVersion = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ToggleFavorite(AppItemViewModel item)
    {
        if (item.IsFavorite) _quickAccess.Remove(item.App.Name);
        else _quickAccess.Add(new FavoriteApp
        {
            Name = item.App.Name,
            MatchNames = item.App.StartMenuNames.Count > 0 ? [.. item.App.StartMenuNames] : [item.App.Name],
        });
        item.IsFavorite = _quickAccess.IsFavorite(item.App.Name);
    }
}

public sealed class AppItemViewModel(CatalogApp app, string categoryName, Action selectionChanged) : ObservableObject
{
    private bool _isSelected, _isFavorite, _isCheckingVersion;
    private ImageSource? _icon;
    private AppInstallStatus _status = new(InstallState.Unknown, null);
    private string? _liveVersion;

    public CatalogApp App { get; } = app;
    public string CategoryName { get; } = categoryName;
    public string Monogram => App.Name[..1].ToUpperInvariant();
    public override string ToString() => $"{App.Name} — {StatusText}";

    public bool IsSelected
    {
        get => _isSelected;
        set { if (SetProperty(ref _isSelected, value)) selectionChanged(); }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set { if (SetProperty(ref _isFavorite, value)) OnPropertyChanged(nameof(FavoriteText)); }
    }

    public string FavoriteText => IsFavorite ? L.T("Remover do Acesso Rápido") : L.T("Adicionar ao Acesso Rápido");

    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public AppInstallStatus Status
    {
        get => _status;
        set
        {
            if (!SetProperty(ref _status, value)) return;
            OnPropertyChanged(nameof(IsInstalled));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(InstallButtonText));
        }
    }

    public bool CanInstall => App.HasWinGet && !IsInstalled;

    public string InstallButtonText =>
        !App.HasWinGet ? L.T("Sem pacote WinGet — use o site oficial")
        : IsInstalled ? L.T("Já instalado")
        : L.T("Instalar");

    public bool IsInstalled => Status.State is InstallState.Installed or InstallState.DetectedInStartMenu;

    public string StatusText => Status.State switch
    {
        InstallState.Installed => Status.InstalledVersion is { Length: > 0 } v && v != "Unknown" ? L.T($"Instalado · v{v}") : L.T("Instalado"),
        InstallState.DetectedInStartMenu => L.T("Instalado (Menu Iniciar)"),
        InstallState.NotInstalled => L.T("Não instalado"),
        InstallState.NotVerified => L.T("Não verificado (sem WinGet)"),
        _ => L.T("Verificando…"),
    };

    public string SourceText => App.WinGetSource switch
    {
        "winget" => L.T($"WinGet · {App.WinGetId}"),
        "msstore" => L.T($"Microsoft Store (WinGet) · {App.WinGetId}"),
        _ => L.T("Sem pacote no WinGet · download pelo site oficial"),
    };

    public string SizeText => App.ApproxSize ?? L.T("Não informado");

    public string VerifiedVersionText => App.VerifiedVersion is null
        ? "—"
        : L.T($"{App.VerifiedVersion} (verificada no catálogo)");

    public bool IsCheckingVersion
    {
        get => _isCheckingVersion;
        set => SetProperty(ref _isCheckingVersion, value);
    }

    public string? LiveVersion
    {
        get => _liveVersion;
        set => SetProperty(ref _liveVersion, value);
    }
}
