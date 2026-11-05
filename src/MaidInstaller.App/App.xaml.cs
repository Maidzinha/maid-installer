using System.IO;
using System.Windows;
using System.Windows.Threading;
using MaidInstaller.App.Services;
using MaidInstaller.App.ViewModels;
using MaidInstaller.Core.Assistant;
using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Collections;
using MaidInstaller.Core.WinGet;
using MaidInstaller.Core.Logging;
using MaidInstaller.Core.Settings;
using MaidInstaller.Core.Storage;
using MaidInstaller.Core.SystemInfo;

namespace MaidInstaller.App;

public partial class App : Application
{
    /// <summary>Espaço mínimo livre recomendado na unidade de dados (modo portátil).</summary>
    private const long LowSpaceBytes = 100L * 1024 * 1024;

    private IAppLogger _logger = NullLogger.Instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var paths = AppPaths.Detect(AppContext.BaseDirectory);
        try
        {
            paths.EnsureCreated();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                $"Não foi possível criar as pastas de dados em:\n{paths.DataRoot}\n\n{ex.Message}\n\n" +
                "Se estiver usando um pendrive, verifique se ele não está protegido contra gravação.",
                "Maid Installer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _logger = new FileLogger(paths.LogsDir);
        _logger.Info($"Iniciando Maid Installer. Modo: {paths.Mode}. Dados: {paths.DataRoot}");

        if (paths.IsPortable && paths.GetFreeSpaceBytes() is { } free && free < LowSpaceBytes)
        {
            MessageBox.Show(
                $"O pendrive está com pouco espaço livre ({free / 1024 / 1024} MB).\n" +
                "Listas, perfis e logs podem não ser salvos corretamente.",
                "Maid Installer — espaço insuficiente", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        var settingsService = new SettingsService(paths.SettingsFile, _logger, paths.DefaultSettingsFile);
        var settings = settingsService.Load();
        settingsService.Save(settings); // grava o arquivo padrão na primeira execução

        var knowledgePath = Path.Combine(paths.BundledCatalogDir, "knowledge.json");
        KnowledgeBase knowledge;
        try
        {
            knowledge = KnowledgeBase.Load(knowledgePath);
        }
        catch (Exception ex)
        {
            _logger.Error($"Base de conhecimento inválida: {knowledgePath}", ex);
            knowledge = new KnowledgeBase();
        }

        var catalogPath = Path.Combine(paths.BundledCatalogDir, "catalog.json");
        AppCatalog catalog;
        try
        {
            catalog = CatalogLoader.Load(catalogPath, _logger);
        }
        catch (Exception ex)
        {
            _logger.Error($"Catálogo inválido: {catalogPath}", ex);
            catalog = new AppCatalog();
        }
        knowledge.AddFromCatalog(catalog);

        var profiles = new CollectionStore(paths.ProfilesDir, CollectionKind.Profile, _logger);
        var lists = new CollectionStore(paths.ListsDir, CollectionKind.List, _logger);
        IReadOnlyList<AppCollection> defaultProfiles = [];
        try
        {
            defaultProfiles = DefaultProfiles.Load(Path.Combine(paths.BundledCatalogDir, "profiles.json"), catalog, _logger);
            var created = DefaultProfiles.Seed(profiles, defaultProfiles, profiles.LoadAll(catalog), settings.SeededProfiles);
            if (created > 0) settingsService.Save(settings);
        }
        catch (Exception ex)
        {
            _logger.Error("Não foi possível preparar os perfis prontos.", ex);
        }

        var winget = new WinGetClient(_logger);
        var installer = new Core.Installation.InstallationService(winget, _logger, paths.LogsDir);
        var history = new Core.Installation.InstallHistory(Path.Combine(paths.DataDir, "historico.json"), _logger);
        installer.JobFinished += history.Record;
        var responder = new HybridResponder(knowledge, () => settings.UserName, _logger);
        var ai = new LocalAiService(paths, settings, () => settingsService.Save(settings), responder, _logger);
        var services = new AppServices
        {
            Ai = ai,
            History = history,
            Catalog = catalog,
            WinGet = winget,
            Installer = installer,
            ShellApps = new ShellAppsService(_logger),
            Profiles = profiles,
            Lists = lists,
            DefaultProfiles = defaultProfiles,
            Paths = paths,
            Logger = _logger,
            SettingsService = settingsService,
            Settings = settings,
            SystemMonitor = new WindowsSystemMonitor(_logger),
            Knowledge = knowledge,
            Responder = responder,
        };

        var vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = vm };
        window.Closing += (_, args) =>
        {
            if (ai.IsDownloading && MessageBox.Show(
                    "O modelo da IA local ainda está sendo baixado.\n\nFechar agora cancela o download (o arquivo incompleto é apagado).\n\nFechar mesmo assim?",
                    "Maid Installer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                args.Cancel = true;
                return;
            }
            if (!services.Installer.IsBusy) return;
            var answer = MessageBox.Show(
                "Ainda há instalações em andamento ou na fila.\n\nFechar agora cancela as que estão aguardando. " +
                "Um instalador que já começou pode continuar sozinho.\n\nFechar mesmo assim?",
                "Maid Installer", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) args.Cancel = true;
            else services.Installer.CancelAll();
        };
        window.Closed += (_, _) =>
        {
            vm.Shutdown();
            ai.Dispose();
            _logger.Info("Maid Installer encerrado.");
        };
        MainWindow = window;
        window.Show();
        _ = ai.InitializeAsync(); // carrega a IA local em segundo plano, se o modelo existir
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger.Error("Erro inesperado na interface.", e.Exception);
        MessageBox.Show($"Ops! Ocorreu um erro inesperado:\n{e.Exception.Message}\n\nO detalhe foi salvo nos logs.",
            "Maid Installer", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
