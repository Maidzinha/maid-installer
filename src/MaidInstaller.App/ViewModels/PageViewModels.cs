using System.Diagnostics;
using System.Windows.Input;
using MaidInstaller.App.Mvvm;
using MaidInstaller.App.Services;

namespace MaidInstaller.App.ViewModels;

/// <summary>
/// Página ainda não implementada. Mostra claramente que está em desenvolvimento e em qual etapa
/// será entregue — nunca finge ser uma função pronta.
/// </summary>
public sealed record PlaceholderViewModel(string Title, string Glyph, string Stage, IReadOnlyList<string> Planned)
{
    public static PlaceholderViewModel For(PageKey key, NavItem nav) => key switch
    {
        PageKey.Library => new(nav.Title, nav.Glyph, L.T("Etapas 3 e 4"),
            [L.T("Catálogo por categorias com identificadores WinGet verificados"), L.T("Pesquisa e filtros"),
             L.T("Seleção múltipla e instalação em lote"), L.T("Acompanhamento real de cada instalação")]),
        PageKey.Profiles => new(nav.Title, nav.Glyph, L.T("Etapa 5"),
            [L.T("Perfis PC Gamer, PC Trabalho e PC Completo"), L.T("Perfis personalizados editáveis"),
             L.T("Importar e exportar perfis")]),
        PageKey.Lists => new(nav.Title, nav.Glyph, L.T("Etapa 5"),
            [L.T("Criar, editar, duplicar e excluir listas"), L.T("Armazenamento em JSON na pasta Lists"),
             L.T("Importar e exportar listas")]),
        PageKey.PostFormat => new(nav.Title, nav.Glyph, L.T("Etapa 6"),
            [L.T("Verificar Windows Update e WinGet"), L.T("Runtimes e drivers"), L.T("Limpeza de temporários e plano de energia"),
             L.T("Cada ação separada, explicada e com confirmação")]),
        PageKey.Updates => new(nav.Title, nav.Glyph, L.T("Etapa 7"),
            [L.T("Listar programas desatualizados pelo WinGet"), L.T("Atualizar individualmente ou em lote"),
             L.T("Histórico de atualizações")]),
        PageKey.Settings => new(nav.Title, nav.Glyph, L.T("Etapas 8, 9 e 11"),
            [L.T("Tema, idioma e inicialização"), L.T("Modo portátil e local de armazenamento"),
             L.T("Configuração e download do modelo de IA"), L.T("Logs e limpeza de cache")]),
        _ => new(nav.Title, nav.Glyph, "", []),
    };
}

/// <summary>Página Sobre, com informações reais do ambiente.</summary>
public sealed class AboutViewModel
{
    public AboutViewModel(AppServices services)
    {
        Version = services.Version;
        Mode = services.Paths.IsPortable ? L.T("Portátil (dados no pendrive)") : L.T("Instalado (dados do usuário)");
        DataRoot = services.Paths.DataRoot;
        AppDirectory = services.Paths.AppDirectory;
        var info = services.SystemMonitor.GetStaticInfo();
        Windows = L.T($"{info.WindowsVersion} (build {info.WindowsBuild})");
        Runtime = $".NET {Environment.Version}";
        OpenDataFolderCommand = new RelayCommand(() => OpenFolder(DataRoot));
        OpenLogsFolderCommand = new RelayCommand(() => OpenFolder(services.Paths.LogsDir));
    }

    public string Version { get; }
    public string Mode { get; }
    public string DataRoot { get; }
    public string AppDirectory { get; }
    public string Windows { get; }
    public string Runtime { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }

    private static void OpenFolder(string path)
    {
        System.IO.Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
