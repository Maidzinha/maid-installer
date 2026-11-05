namespace MaidInstaller.Core.PostFormat;

/// <param name="Target">Programa do Windows ou página "ms-settings:". Lista fixa, definida aqui no código.</param>
public sealed record AdminTool(string Name, string Description, string Glyph, string Target, string? Arguments = null, bool AsksAdmin = false);

/// <summary>Ferramentas do próprio Windows que o Kit pode abrir. Abrir não altera nada.</summary>
public static class AdminTools
{
    public static readonly IReadOnlyList<AdminTool> All =
    [
        new("Gerenciador de Dispositivos", "Drivers e dispositivos do PC", "", "devmgmt.msc"),
        new("Gerenciamento de Disco", "Partições, letras e formatação de discos", "", "diskmgmt.msc", AsksAdmin: true),
        new("Gerenciador de Tarefas", "Programas abertos, desempenho e inicialização", "", "taskmgr.exe"),
        new("Serviços", "Serviços do Windows e de programas", "", "services.msc"),
        new("Visualizador de Eventos", "Registros de erros e avisos do sistema", "", "eventvwr.msc"),
        new("Informações do Sistema", "Detalhes completos de hardware e software", "", "msinfo32.exe"),
        new("Limpeza de Disco", "Ferramenta do Windows para arquivos do sistema", "", "cleanmgr.exe"),
        new("Painel de Controle", "Configurações clássicas do Windows", "", "control.exe"),
    ];

    public static class Settings
    {
        public const string WindowsUpdate = "ms-settings:windowsupdate";
        public const string OptionalUpdates = "ms-settings:windowsupdate-optionalupdates";
        public const string StartupApps = "ms-settings:startupapps";
        public const string DefaultApps = "ms-settings:defaultapps";
        public const string PowerSleep = "ms-settings:powersleep";
        public const string StorageSense = "ms-settings:storagesense";
        /// <summary>Página da Loja do "Instalador de Aplicativo", que contém o WinGet.</summary>
        public const string AppInstallerStore = "ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1";
    }
}
