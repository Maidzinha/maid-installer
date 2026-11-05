namespace MaidInstaller.Core.Settings;

/// <summary>Configurações persistidas em Config/settings.json.</summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>Como a Maid chama o usuário. Vazio = sem nome (a Maid fala sem vocativo).</summary>
    public string UserName { get; set; } = "";

    /// <summary>"en" (padrão), "pt-BR" ou "auto" (idioma do Windows).</summary>
    public string Language { get; set; } = "en";

    /// <summary>"Dark" (padrão), "Light" ou "BlackPink".</summary>
    public string Theme { get; set; } = ThemeDark;

    public const string ThemeDark = "Dark", ThemeLight = "Light", ThemeBlackPink = "BlackPink";
    public static readonly string[] Themes = [ThemeDark, ThemeLight, ThemeBlackPink];
    public static readonly string[] Languages = [L.English, L.Portuguese, L.Auto];
    public bool AssistantMinimized { get; set; }

    /// <summary>Usar a IA local quando o modelo estiver na pasta Models (ligada por padrão).</summary>
    public bool AssistantAiEnabled { get; set; } = true;

    /// <summary>Programas da barra de Acesso Rápido.</summary>
    public List<FavoriteApp> Favorites { get; set; } = FavoriteApp.Defaults();

    /// <summary>Perfis prontos já criados nesta pasta de dados (um perfil excluído pelo usuário não volta sozinho).</summary>
    public List<string> SeededProfiles { get; set; } = [];

    /// <summary>Campos gravados por uma versão mais nova do app; são regravados sem alteração.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtraFields { get; set; }
}

/// <summary>
/// Um favorito da barra de acesso rápido. <see cref="MatchNames"/> são os nomes como
/// aparecem no Menu Iniciar do Windows (usados para localizar o programa instalado).
/// </summary>
public sealed class FavoriteApp
{
    public string Name { get; set; } = "";
    public List<string> MatchNames { get; set; } = [];

    public static List<FavoriteApp> Defaults() =>
    [
        new() { Name = "Steam", MatchNames = ["Steam"] },
        new() { Name = "Discord", MatchNames = ["Discord"] },
        new() { Name = "Google Chrome", MatchNames = ["Google Chrome"] },
        new() { Name = "Opera GX", MatchNames = ["Opera GX Browser", "Opera GX"] },
        new() { Name = "WhatsApp", MatchNames = ["WhatsApp"] },
        new() { Name = "Spotify", MatchNames = ["Spotify"] },
        new() { Name = "OBS Studio", MatchNames = ["OBS Studio"] },
        new() { Name = "Minecraft", MatchNames = ["Minecraft Launcher", "Minecraft"] },
    ];
}
