namespace MaidInstaller.Core.Storage;

/// <summary>Onde o Maid Installer guarda seus dados.</summary>
public enum StorageMode
{
    /// <summary>Dados em %LocalAppData%\MaidInstaller.</summary>
    Installed,
    /// <summary>Dados ao lado do executável (pendrive / Ventoy).</summary>
    Portable,
}

/// <summary>
/// Resolve todas as pastas usadas pelo aplicativo.
/// O modo portátil é ativado pela presença do arquivo <see cref="PortableMarkerFile"/>
/// ao lado do executável. Nesse modo nada é gravado fora da pasta do aplicativo.
/// </summary>
public sealed class AppPaths
{
    public const string PortableMarkerFile = "MaidInstaller.portable";
    public const string AppFolderName = "MaidInstaller";

    private AppPaths(StorageMode mode, string appDirectory, string dataRoot)
    {
        Mode = mode;
        AppDirectory = appDirectory;
        DataRoot = dataRoot;
    }

    public StorageMode Mode { get; }
    public bool IsPortable => Mode == StorageMode.Portable;

    /// <summary>Pasta do executável (arquivos somente leitura que acompanham o app).</summary>
    public string AppDirectory { get; }

    /// <summary>Raiz dos dados do usuário (listas, perfis, configurações, logs...).</summary>
    public string DataRoot { get; }

    public string DataDir => Path.Combine(DataRoot, "Data");
    public string ConfigDir => Path.Combine(DataRoot, "Config");
    public string ProfilesDir => Path.Combine(DataRoot, "Profiles");
    public string ListsDir => Path.Combine(DataRoot, "Lists");
    public string ModelsDir => Path.Combine(DataRoot, "Models");
    public string LogsDir => Path.Combine(DataRoot, "Logs");
    public string CacheDir => Path.Combine(DataRoot, "Cache");

    /// <summary>Catálogo e base de conhecimento distribuídos com o aplicativo.</summary>
    public string BundledCatalogDir => Path.Combine(AppDirectory, "Catalog");
    public string AssetsDir => Path.Combine(AppDirectory, "Assets");
    public string SettingsFile => Path.Combine(ConfigDir, "settings.json");

    /// <summary>Configuração padrão distribuída com o app (usada só na primeira execução).</summary>
    public string DefaultSettingsFile => Path.Combine(AppDirectory, "Defaults", "settings.default.json");

    public IReadOnlyList<string> UserDataDirectories =>
        [DataDir, ConfigDir, ProfilesDir, ListsDir, ModelsDir, LogsDir, CacheDir];

    public static AppPaths Detect(string appDirectory, string? localAppDataOverride = null)
    {
        appDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory));
        if (appDirectory.EndsWith(':')) appDirectory += Path.DirectorySeparatorChar; // raiz do pendrive (ex.: "E:\")
        if (File.Exists(Path.Combine(appDirectory, PortableMarkerFile)))
            return new AppPaths(StorageMode.Portable, appDirectory, appDirectory);

        var localAppData = localAppDataOverride
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AppPaths(StorageMode.Installed, appDirectory, Path.Combine(localAppData, AppFolderName));
    }

    /// <summary>Cria as pastas de dados. Lança exceção se a mídia estiver somente leitura.</summary>
    public void EnsureCreated()
    {
        foreach (var dir in UserDataDirectories)
            Directory.CreateDirectory(dir);
    }

    /// <summary>Espaço livre na unidade de dados, ou null se não puder ser lido.</summary>
    public long? GetFreeSpaceBytes()
    {
        try
        {
            var root = Path.GetPathRoot(DataRoot);
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
