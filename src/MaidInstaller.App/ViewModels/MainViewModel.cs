using System.Windows;
using System.Windows.Input;
using MaidInstaller.App.Mvvm;
using MaidInstaller.App.Services;
using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Installation;

namespace MaidInstaller.App.ViewModels;

public enum PageKey { Home, Library, Profiles, Lists, PostFormat, Updates, Settings, About }

public sealed record NavItem(PageKey Key, string Title, string Glyph)
{
    public override string ToString() => Title; // nome lido por leitores de tela
}

/// <summary>Janela principal: navegação lateral, página atual, diálogos e coordenação das instalações.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Dictionary<PageKey, object> _pages = [];
    private NavItem? _selectedNav;
    private object _currentPage = null!;
    private DialogViewModel? _dialog;

    public MainViewModel(AppServices services)
    {
        _services = services;
        Assistant = new AssistantViewModel(services);
        SystemStatus = new SystemStatusViewModel(services.SystemMonitor, services.Logger);
        QuickAccess = new QuickAccessViewModel(services, Assistant, () => Navigate(PageKey.Library));
        Installs = new InstallsViewModel(services);
        Assistant.InstallStatusProvider = Installs.DescribeForAssistant;
        _ = QuickAccess.LoadAsync();
        HookAssistantToInstaller();

        NavItems =
        [
            new(PageKey.Home, L.T("Início"), ""),
            new(PageKey.Library, L.T("Biblioteca de Apps"), ""),
            new(PageKey.Profiles, L.T("Perfis de Instalação"), ""),
            new(PageKey.Lists, L.T("Minhas Listas"), ""),
            new(PageKey.PostFormat, L.T("Kit Pós-Formatação"), ""),
            new(PageKey.Updates, L.T("Atualizações"), ""),
            new(PageKey.Settings, L.T("Configurações"), ""),
            new(PageKey.About, L.T("Sobre"), ""),
        ];
        _selectedNav = NavItems[0];
        CurrentPage = GetPage(PageKey.Home);
        SystemStatus.Start();
        ShowInstallsCommand = new RelayCommand(ShowInstalls);
        if (!services.Settings.TourSeen) StartTour();
    }

    private TourViewModel? _tour;

    /// <summary>Tour de boas-vindas aberto (ou null).</summary>
    public TourViewModel? Tour
    {
        get => _tour;
        private set => SetProperty(ref _tour, value);
    }

    /// <summary>Abre o tour de boas-vindas (na primeira abertura ou por Configurações).</summary>
    public void StartTour() => Tour = new TourViewModel(Navigate, () =>
    {
        Tour = null;
        if (_services.Settings.TourSeen) return;
        _services.Settings.TourSeen = true;
        _services.SaveSettings();
    });

    public IReadOnlyList<NavItem> NavItems { get; }
    public AssistantViewModel Assistant { get; }
    public SystemStatusViewModel SystemStatus { get; }
    public QuickAccessViewModel QuickAccess { get; }
    public InstallsViewModel Installs { get; }
    public ICommand ShowInstallsCommand { get; }

    public string VersionText => $"v{_services.Version}";
    public bool IsPortable => _services.Paths.IsPortable;
    public string ModeText => IsPortable ? L.T("Modo Portátil") : L.T("Modo Instalado");
    public string ModeTooltip => L.T($"Dados em: {_services.Paths.DataRoot}");

    public NavItem? SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (value is null || !SetProperty(ref _selectedNav, value)) return;
            CurrentPage = GetPage(value.Key);
            if (CurrentPage is PostFormatViewModel kit) kit.OnShown();
            if (CurrentPage is UpdatesViewModel updates) updates.OnShown();
            // O painel de hardware só fica visível no Início: não consome recursos nas outras páginas.
            if (value.Key == PageKey.Home) SystemStatus.Start(); else SystemStatus.Stop();
        }
    }

    public object CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    /// <summary>Diálogo de confirmação aberto (null = nenhum).</summary>
    public DialogViewModel? Dialog
    {
        get => _dialog;
        private set => SetProperty(ref _dialog, value);
    }

    public void Navigate(PageKey key) => SelectedNav = NavItems.First(n => n.Key == key);

    /// <summary>A Biblioteca guarda o status real de cada app; Perfis e Listas reaproveitam os mesmos itens.</summary>
    public LibraryViewModel Library => (LibraryViewModel)GetPage(PageKey.Library);

    /// <summary>"Salvar como lista" na Biblioteca: cria a lista e abre Minhas Listas.</summary>
    public void SaveAsList(IEnumerable<string> appIds)
    {
        var lists = (CollectionsViewModel)GetPage(PageKey.Lists);
        lists.CreateFrom(appIds);
        Navigate(PageKey.Lists);
    }

    /// <summary>Abre a tela de acompanhamento (fora da barra lateral).</summary>
    public void ShowInstalls()
    {
        _selectedNav = null;
        OnPropertyChanged(nameof(SelectedNav));
        SystemStatus.Stop();
        CurrentPage = Installs;
    }

    public async Task<bool> ShowDialogAsync(DialogViewModel dialog)
    {
        Dialog = dialog;
        try { return await dialog.Result; }
        finally { Dialog = null; }
    }

    /// <summary>
    /// Pedido de instalação vindo da Biblioteca (ou, depois, de Perfis e Listas).
    /// Separa o que dá para instalar, mostra tudo numa confirmação e só então coloca na fila.
    /// </summary>
    public async Task<bool> RequestInstallAsync(IEnumerable<CatalogApp> apps, Func<CatalogApp, bool> isInstalled)
    {
        var plan = InstallRequest.Plan(apps, isInstalled, _services.Installer.Jobs);
        var (toInstall, already, inQueue, noPackage) = (plan.ToInstall, plan.AlreadyInstalled, plan.InQueue, plan.NoPackage);
        var name = _services.Settings.UserName;

        var notes = new List<string>();
        if (inQueue.Count > 0) notes.Add(L.T($"Já estão na fila: {string.Join(", ", inQueue.Select(a => a.Name))}."));
        if (already.Count > 0) notes.Add(L.T($"Já instalados (serão pulados): {string.Join(", ", already.Select(a => a.Name))}."));
        if (noPackage.Count > 0) notes.Add(L.T($"Sem pacote no WinGet (baixe pelo site oficial na Biblioteca): {string.Join(", ", noPackage.Select(a => a.Name))}."));

        if (toInstall.Count == 0)
        {
            await ShowDialogAsync(new DialogViewModel(L.T("Nada para instalar"), L.T($"{L.Name(name)}, nenhum dos aplicativos escolhidos precisa ser instalado agora."),
                [], notes, L.T("Entendi"), cancelText: null));
            return false;
        }

        notes.InsertRange(0,
        [
            L.T("Os programas são baixados das fontes oficiais pelo WinGet e instalados em modo silencioso, um de cada vez."),
            L.T("Alguns instaladores pedem permissão de administrador: o Windows mostra o aviso (UAC) e você decide. Se recusar, só aquele app é cancelado."),
            L.T("Ao confirmar, você aceita as licenças desses programas."),
            L.T("Nada já instalado será atualizado, e o PC não será reiniciado automaticamente."),
        ]);
        foreach (var app in toInstall.Where(a => !a.IsFree))
            notes.Add($"{app.Name}: {app.Price}.");

        var confirmed = await ShowDialogAsync(new DialogViewModel(
            toInstall.Count == 1 ? L.T($"Instalar {toInstall[0].Name}?") : L.T($"Instalar {toInstall.Count} aplicativos?"),
            L.T("Confira o que será instalado neste PC:"),
            [.. toInstall.Select(a => new DialogItem(a.Name, a.WinGetSource == "msstore" ? "Microsoft Store" : L.T($"WinGet · {a.WinGetId}")))],
            notes,
            toInstall.Count == 1 ? L.T("Instalar") : L.T($"Instalar {toInstall.Count}")));

        if (!confirmed)
        {
            _services.Logger.Info("Instalação cancelada na confirmação.");
            return false;
        }

        _services.Logger.Info($"Instalação autorizada: {string.Join(", ", toInstall.Select(a => a.WinGetId))}");
        foreach (var app in already) _services.Installer.AddAlreadyInstalled(app);
        foreach (var app in toInstall) _services.Installer.Enqueue(app);
        ShowInstalls();
        return true;
    }

    /// <summary>A assistente recebe as informações reais da fila. Ela só comenta; não executa nada.</summary>
    private void HookAssistantToInstaller()
    {
        var installer = _services.Installer;
        var name = _services.Settings.UserName;

        installer.JobStarted += job => OnUi(() =>
        {
            var summary = _services.Knowledge.Apps.FirstOrDefault(a => a.Id == job.App.Id)?.Summary;
            Assistant.Post((job.Operation == InstallOperation.Upgrade ? L.T($"{L.Name(name)}, estou atualizando o {job.App.Name}! ♡") : L.T($"{L.Name(name)}, estou instalando o {job.App.Name}! ♡")) + (summary is null ? "" : $" {FirstSentence(summary)}"), expand: false);
        });

        installer.JobFinished += job => OnUi(() =>
        {
            switch (job.State)
            {
                case InstallJobState.Completed:
                    Assistant.Post((job.Operation == InstallOperation.Upgrade ? L.T($"Prontinho, {job.App.Name} atualizado! ♡") : L.T($"Prontinho, {job.App.Name} instalado! ♡")) + (job.RebootRequired ? L.T(" Ele pediu para reiniciar o PC; faça isso quando puder.") : ""), expand: false);
                    break;
                case InstallJobState.Failed:
                    Assistant.Post((job.Operation == InstallOperation.Upgrade ? L.T($"Ops, {L.Name(name)}, o {job.App.Name} não foi atualizado. {job.Message} Você pode tentar de novo na tela de instalações.") : L.T($"Ops, {L.Name(name)}, o {job.App.Name} não foi instalado. {job.Message} Você pode tentar de novo na tela de instalações.")), expand: false);
                    break;
                case InstallJobState.Cancelled when job.StartedAt is not null:
                    Assistant.Post((job.Operation == InstallOperation.Upgrade ? L.T($"Tudo bem, a atualização do {job.App.Name} foi cancelada. {job.Message}") : L.T($"Tudo bem, a instalação do {job.App.Name} foi cancelada. {job.Message}")), expand: false);
                    break;
            }
        });

        installer.QueueDrained += () => OnUi(() =>
        {
            var jobs = installer.Jobs;
            var ok = jobs.Count(j => j.State == InstallJobState.Completed);
            var failed = jobs.Count(j => j.State == InstallJobState.Failed);
            Assistant.Post(failed == 0
                ? L.T($"Prontinho, {L.Name(name)}! Terminei a fila: {ok} instalação(ões) concluída(s). ♡")
                : L.T($"Terminei a fila, {L.Name(name)}: {ok} concluída(s) e {failed} com falha. Os detalhes estão na tela de instalações ♡"), expand: false);
            NotifyQueueEnded(jobs);

            // Atualiza o que está instalado (Biblioteca e Acesso Rápido).
            _services.ShellApps.Invalidate();
            _ = QuickAccess.LoadAsync();
            if (_pages.TryGetValue(PageKey.Library, out var lib) && lib is LibraryViewModel library) _ = library.RefreshStatusAsync();
            if (_pages.TryGetValue(PageKey.Updates, out var up) && up is UpdatesViewModel updatesPage && jobs.Any(j => j.Operation == InstallOperation.Upgrade)) _ = updatesPage.CheckAsync();
        });
    }

    private DateTime _lastQueueEnd = DateTime.MinValue;
    private Services.WindowsNotifier? _notifier;

    /// <summary>
    /// Aviso do Windows com o resumo da fila que acabou de terminar. Só aparece se a janela do app
    /// não estiver em primeiro plano e se o aviso estiver ligado nas Configurações.
    /// </summary>
    private void NotifyQueueEnded(IReadOnlyList<InstallJob> jobs)
    {
        var since = _lastQueueEnd;
        _lastQueueEnd = DateTime.Now;
        var batch = jobs.Where(j => j.FinishedAt is { } f && f > since).ToList();
        var ok = batch.Count(j => j.State == InstallJobState.Completed);
        var failed = batch.Count(j => j.State == InstallJobState.Failed);
        if (ok + failed == 0 || !_services.Settings.NotifyWhenQueueEnds) return;
        if (Application.Current?.MainWindow is not { } window || (window.IsActive && window.WindowState != WindowState.Minimized)) return;

        _notifier ??= new Services.WindowsNotifier(window);
        var text = failed == 0
            ? L.T($"Terminei a fila: {ok} pronto(s). ♡")
            : L.T($"Terminei a fila: {ok} pronto(s) e {failed} com falha. Clique para ver os detalhes.");
        var shown = _notifier.Show(L.T("Maid Installer: fila concluída"), text, () =>
        {
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
            ShowInstalls();
        });
        _services.Logger.Info($"Aviso do Windows de fim da fila ({ok} concluído(s), {failed} com falha): {(shown ? "mostrado" : "não foi possível mostrar")}.");
    }

    private static string FirstSentence(string text)
    {
        var i = text.IndexOf(". ", StringComparison.Ordinal);
        return i > 0 ? text[..(i + 1)] : text;
    }

    private static void OnUi(Action action) => Application.Current?.Dispatcher.InvokeAsync(action);

    private object GetPage(PageKey key)
    {
        if (_pages.TryGetValue(key, out var page)) return page;
        page = key switch
        {
            PageKey.Home => new HomeViewModel(this, _services),
            PageKey.Library => new LibraryViewModel(_services, QuickAccess, this),
            PageKey.Profiles => new CollectionsViewModel(_services, this, Core.Collections.CollectionKind.Profile),
            PageKey.Lists => new CollectionsViewModel(_services, this, Core.Collections.CollectionKind.List),
            PageKey.PostFormat => new PostFormatViewModel(_services, this),
            PageKey.Updates => new UpdatesViewModel(_services, this),
            PageKey.Settings => new SettingsViewModel(_services, this),
            PageKey.About => new AboutViewModel(_services),
            _ => PlaceholderViewModel.For(key, NavItems.First(n => n.Key == key)),
        };
        _pages[key] = page;
        return page;
    }

    public void Shutdown()
    {
        SystemStatus.Stop();
        _notifier?.Dispose();
    }
}
