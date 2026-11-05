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
        PageKey.Library => new(nav.Title, nav.Glyph, "Etapas 3 e 4",
            ["Catálogo por categorias com identificadores WinGet verificados", "Pesquisa e filtros",
             "Seleção múltipla e instalação em lote", "Acompanhamento real de cada instalação"]),
        PageKey.Profiles => new(nav.Title, nav.Glyph, "Etapa 5",
            ["Perfis PC Gamer, PC Trabalho e PC Completo", "Perfis personalizados editáveis",
             "Importar e exportar perfis"]),
        PageKey.Lists => new(nav.Title, nav.Glyph, "Etapa 5",
            ["Criar, editar, duplicar e excluir listas", "Armazenamento em JSON na pasta Lists",
             "Importar e exportar listas"]),
        PageKey.PostFormat => new(nav.Title, nav.Glyph, "Etapa 6",
            ["Verificar Windows Update e WinGet", "Runtimes e drivers", "Limpeza de temporários e plano de energia",
             "Cada ação separada, explicada e com confirmação"]),
        PageKey.Updates => new(nav.Title, nav.Glyph, "Etapa 7",
            ["Listar programas desatualizados pelo WinGet", "Atualizar individualmente ou em lote",
             "Histórico de atualizações"]),
        PageKey.Settings => new(nav.Title, nav.Glyph, "Etapas 8, 9 e 11",
            ["Tema, idioma e inicialização", "Modo portátil e local de armazenamento",
             "Configuração e download do modelo de IA", "Logs e limpeza de cache"]),
        _ => new(nav.Title, nav.Glyph, "", []),
    };
}

/// <summary>Página Sobre, com informações reais do ambiente.</summary>
public sealed class AboutViewModel
{
    public AboutViewModel(AppServices services)
    {
        Version = services.Version;
        Mode = services.Paths.IsPortable ? "Portátil (dados no pendrive)" : "Instalado (dados do usuário)";
        DataRoot = services.Paths.DataRoot;
        AppDirectory = services.Paths.AppDirectory;
        var info = services.SystemMonitor.GetStaticInfo();
        Windows = $"{info.WindowsVersion} (build {info.WindowsBuild})";
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
