using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaidInstaller.App.Mvvm;
using MaidInstaller.App.Services;

namespace MaidInstaller.App.ViewModels;

public sealed record ShortcutItem(string Title, string Subtitle, string Glyph, ICommand Command);

public sealed class HomeViewModel
{
    public HomeViewModel(MainViewModel main, AppServices services)
    {
        Main = main;
        UserName = services.Settings.UserName;
        MascotImage = LoadLocalImage(Path.Combine(services.Paths.AssetsDir, "mascot.png"), services);

        ICommand Go(PageKey key) => new RelayCommand(() => main.Navigate(key));
        Shortcuts =
        [
            new(L.T("Instalar Aplicativos"), L.T("Escolha e instale programas."), "", Go(PageKey.Library)),
            new(L.T("Usar um Perfil"), L.T("Instale um conjunto pronto."), "", Go(PageKey.Profiles)),
            new(L.T("Kit Pós-Formatação"), L.T("Ajustes e drivers essenciais."), "", Go(PageKey.PostFormat)),
            new(L.T("Atualizar Programas"), L.T("Verifique e atualize apps."), "", Go(PageKey.Updates)),
            new(L.T("Minhas Listas"), L.T("Suas seleções salvas."), "", Go(PageKey.Lists)),
            new(L.T("Configurações"), L.T("Personalize o aplicativo."), "", Go(PageKey.Settings)),
        ];
    }

    public MainViewModel Main { get; }
    public string UserName { get; }

    /// <summary>"Bem-vindo, " antes do nome; sem nome, só "Bem-vindo".</summary>
    public string WelcomePrefix => string.IsNullOrWhiteSpace(UserName) ? L.T("Bem-vindo") : L.T("Bem-vindo, ");
    public ImageSource? MascotImage { get; }
    public IReadOnlyList<ShortcutItem> Shortcuts { get; }
    public QuickAccessViewModel QuickAccess => Main.QuickAccess;
    public SystemStatusViewModel SystemStatus => Main.SystemStatus;
    public AssistantViewModel Assistant => Main.Assistant;

    /// <summary>A mascote é um arquivo local (Assets\mascot.png): pode ser trocada sem recompilar.</summary>
    private static ImageSource? LoadLocalImage(string path, AppServices services)
    {
        if (!File.Exists(path))
        {
            services.Logger.Warn($"Imagem da mascote não encontrada: {path}");
            return null;
        }
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // não mantém o arquivo aberto (pendrive)
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            services.Logger.Error("Falha ao carregar a mascote.", ex);
            return null;
        }
    }
}
