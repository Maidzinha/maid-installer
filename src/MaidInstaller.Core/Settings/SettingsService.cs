using System.Text.Json;
using MaidInstaller.Core.Logging;
using MaidInstaller.Core.Storage;

namespace MaidInstaller.Core.Settings;

/// <param name="settingsFile">Config/settings.json do usuário.</param>
/// <param name="defaultsFile">
/// Configuração padrão distribuída com o app (Defaults/settings.default.json), usada só na primeira
/// execução, quando o usuário ainda não tem settings.json. Pode ser editada sem recompilar.
/// </param>
public sealed class SettingsService(string settingsFile, IAppLogger logger, string? defaultsFile = null)
{
    /// <summary>Carrega as configurações. Um arquivo corrompido é preservado como backup e substituído pelos padrões.</summary>
    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = JsonStore.Read<AppSettings>(settingsFile) ?? LoadDefaults();
        }
        catch (JsonException ex)
        {
            var backup = $"{settingsFile}.corrompido-{DateTime.Now:yyyyMMddHHmmss}";
            try { File.Move(settingsFile, backup); } catch (IOException) { }
            logger.Error($"settings.json inválido; backup salvo em {backup}. Usando padrões.", ex);
            settings = LoadDefaults();
        }
        return Migrate(settings);
    }

    /// <summary>Padrões do arquivo distribuído; se ele faltar ou estiver inválido, os padrões do código.</summary>
    public AppSettings LoadDefaults()
    {
        if (defaultsFile is null || !File.Exists(defaultsFile)) return new AppSettings();
        try
        {
            var defaults = JsonStore.Read<AppSettings>(defaultsFile) ?? new AppSettings();
            logger.Info($"Configuração padrão carregada de {defaultsFile}.");
            return defaults;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.Error($"Configuração padrão inválida ({defaultsFile}); usando os padrões internos.", ex);
            return new AppSettings();
        }
    }

    /// <summary>
    /// Ajusta configurações gravadas por outras versões do app. Versões antigas são atualizadas passo a passo;
    /// as de uma versão mais nova são mantidas como estão (o número não é rebaixado e os campos desconhecidos
    /// são preservados em <see cref="AppSettings.ExtraFields"/>), para não estragar o pendrive ao usar uma versão anterior.
    /// </summary>
    public AppSettings Migrate(AppSettings settings)
    {
        if (settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
        {
            logger.Warn($"settings.json é de uma versão mais nova do Maid Installer (formato {settings.SchemaVersion}); " +
                        "campos desconhecidos serão mantidos.");
            return settings;
        }
        if (settings.SchemaVersion < 1) settings.SchemaVersion = 1;
        // Migrações futuras entram aqui, uma por versão de formato: if (settings.SchemaVersion == 1) { ...; settings.SchemaVersion = 2; }
        settings.Favorites ??= FavoriteApp.Defaults();
        settings.SeededProfiles ??= [];
        settings.UserName = settings.UserName?.Trim() ?? "";
        if (!AppSettings.Languages.Contains(settings.Language)) settings.Language = new AppSettings().Language;
        if (!AppSettings.Themes.Contains(settings.Theme)) settings.Theme = new AppSettings().Theme;
        return settings;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            JsonStore.WriteAtomic(settingsFile, settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Error("Não foi possível salvar as configurações.", ex);
        }
    }
}
