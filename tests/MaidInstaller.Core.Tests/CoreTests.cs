using MaidInstaller.Core.Assistant;
using MaidInstaller.Core.Logging;
using MaidInstaller.Core.Settings;
using MaidInstaller.Core.Shell;
using MaidInstaller.Core.Storage;
using MaidInstaller.Core.SystemInfo;

namespace MaidInstaller.Core.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "maid-tests-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

public class AppPathsTests
{
    [Fact]
    public void Installed_mode_uses_local_app_data()
    {
        using var app = new TempDir();
        using var local = new TempDir();
        var paths = AppPaths.Detect(app.Path, local.Path);
        Assert.Equal(StorageMode.Installed, paths.Mode);
        Assert.Equal(Path.Combine(local.Path, "MaidInstaller"), paths.DataRoot);
    }

    [Fact]
    public void Portable_marker_keeps_all_data_next_to_exe()
    {
        using var app = new TempDir();
        using var local = new TempDir();
        File.WriteAllText(Path.Combine(app.Path, AppPaths.PortableMarkerFile), "");
        var paths = AppPaths.Detect(app.Path, local.Path);
        paths.EnsureCreated();

        Assert.True(paths.IsPortable);
        Assert.All(paths.UserDataDirectories, d => Assert.StartsWith(Path.GetFullPath(app.Path), d));
        Assert.All(paths.UserDataDirectories, d => Assert.True(Directory.Exists(d)));
        Assert.Empty(Directory.GetFileSystemEntries(local.Path)); // nada gravado no PC
        Assert.NotNull(paths.GetFreeSpaceBytes());
    }
}

public class SettingsTests
{
    [Fact]
    public void Roundtrip_preserves_values()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "Config", "settings.json");
        var svc = new SettingsService(file, NullLogger.Instance);
        var s = svc.Load();
        s.AssistantMinimized = true;
        s.Favorites = [new FavoriteApp { Name = "Steam", MatchNames = ["Steam"] }];
        svc.Save(s);

        var loaded = svc.Load();
        Assert.True(loaded.AssistantMinimized);
        Assert.Single(loaded.Favorites);
        Assert.Equal("Helder", loaded.UserName);
    }

    [Fact]
    public void Corrupt_file_is_backed_up_and_defaults_used()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(file, "{ isto não é json");
        var s = new SettingsService(file, NullLogger.Instance).Load();
        Assert.Equal("Helder", s.UserName);
        Assert.Single(Directory.GetFiles(dir.Path, "settings.json.corrompido-*"));
    }

    private static readonly string BundledDefaults = Path.Combine(AppContext.BaseDirectory, "Defaults", "settings.default.json");

    [Fact]
    public void Bundled_default_file_matches_code_defaults()
    {
        using var dir = new TempDir();
        var fromFile = new SettingsService(Path.Combine(dir.Path, "settings.json"), NullLogger.Instance, BundledDefaults).LoadDefaults();
        var code = new AppSettings();
        Assert.Equal(code.UserName, fromFile.UserName);
        Assert.Equal(code.AssistantAiEnabled, fromFile.AssistantAiEnabled);
        Assert.Equal(code.SchemaVersion, fromFile.SchemaVersion);
        Assert.Equal(code.Favorites.Select(f => f.Name), fromFile.Favorites.Select(f => f.Name));
        Assert.Equal(code.Favorites.SelectMany(f => f.MatchNames), fromFile.Favorites.SelectMany(f => f.MatchNames));
        Assert.Null(fromFile.ExtraFields); // nenhum campo com nome errado no arquivo
    }

    [Fact]
    public void Default_file_is_used_only_on_first_run()
    {
        using var dir = new TempDir();
        var defaults = Path.Combine(dir.Path, "settings.default.json");
        File.WriteAllText(defaults, """{ "userName": "Maria", "assistantAiEnabled": false }""");
        var file = Path.Combine(dir.Path, "Config", "settings.json");
        var svc = new SettingsService(file, NullLogger.Instance, defaults);

        var first = svc.Load();
        Assert.Equal("Maria", first.UserName);
        Assert.False(first.AssistantAiEnabled);
        first.UserName = "Helder";
        svc.Save(first);

        File.WriteAllText(defaults, """{ "userName": "Outra" }""");
        Assert.Equal("Helder", svc.Load().UserName); // o arquivo do usuário manda
    }

    [Fact]
    public void Invalid_default_file_falls_back_to_code_defaults()
    {
        using var dir = new TempDir();
        var defaults = Path.Combine(dir.Path, "settings.default.json");
        File.WriteAllText(defaults, "{ quebrado");
        var s = new SettingsService(Path.Combine(dir.Path, "settings.json"), NullLogger.Instance, defaults).Load();
        Assert.Equal("Helder", s.UserName);
        Assert.Equal(8, s.Favorites.Count);
    }

    [Fact]
    public void Settings_from_newer_version_keep_unknown_fields_and_version()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(file, """{ "schemaVersion": 7, "userName": "Helder", "recursoNovo": { "ligado": true } }""");
        var svc = new SettingsService(file, NullLogger.Instance);
        var s = svc.Load();
        s.AssistantMinimized = true;
        svc.Save(s);

        var json = File.ReadAllText(file);
        Assert.Contains("\"schemaVersion\": 7", json);
        Assert.Contains("recursoNovo", json);
        Assert.Contains("\"ligado\": true", json);
    }

    [Fact]
    public void Settings_with_missing_lists_are_repaired()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(file, """{ "schemaVersion": 0, "favorites": null, "seededProfiles": null }""");
        var s = new SettingsService(file, NullLogger.Instance).Load();
        Assert.Equal(1, s.SchemaVersion);
        Assert.Equal(8, s.Favorites.Count);
        Assert.NotNull(s.SeededProfiles);
    }
}

public class AssistantTests
{
    private static readonly KnowledgeBase Kb =
        KnowledgeBase.Load(Path.Combine(AppContext.BaseDirectory, "Catalog", "knowledge.json"));

    private static OfflineResponder Responder => new(Kb, () => "Helder");

    [Fact]
    public void Knowledge_file_loads_and_entries_are_complete()
    {
        Assert.NotEmpty(Kb.Apps);
        Assert.All(Kb.Apps, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Id));
            Assert.False(string.IsNullOrWhiteSpace(a.Name));
            Assert.False(string.IsNullOrWhiteSpace(a.Developer));
            Assert.False(string.IsNullOrWhiteSpace(a.License));
            Assert.False(string.IsNullOrWhiteSpace(a.Summary));
        });
        Assert.Equal(Kb.Apps.Count, Kb.Apps.Select(a => a.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("Para que serve o Discord?", "Discord Inc.")]
    [InlineData("o que é o zap?", "WhatsApp")]
    [InlineData("O OBS é grátis?", "GPL")]
    [InlineData("Quais apps são essenciais?", "7-Zip")]
    [InlineData("O que o Kit Pós-Formatação faz?", "Kit Pós-Formatação")]
    [InlineData("oii", "Oii, Helder")]
    public void Answers_from_knowledge_base(string question, string expected) =>
        Assert.Contains(expected, Responder.Answer(question));

    [Fact]
    public void Unknown_subject_is_not_invented()
    {
        var answer = Responder.Answer("Qual a capital da Mongólia?");
        Assert.Contains("não tenho essa informação", answer);
    }

    [Fact]
    public void Comparison_lists_both_apps()
    {
        var answer = Responder.Answer("Qual a diferença entre Chrome e Opera GX?");
        Assert.Contains("Google Chrome", answer);
        Assert.Contains("Opera GX", answer);
    }

    [Fact]
    public void Normalize_removes_accents_and_punctuation() =>
        Assert.Equal("pos formatacao e otimo", OfflineResponder.Normalize("Pós-Formatação é ÓTIMO!!"));
}

public class SystemTests
{
    [Theory]
    [InlineData("AMD Ryzen 5 5500                ", "AMD Ryzen 5 5500")]
    [InlineData("AMD Ryzen 7 5700X 8-Core Processor", "AMD Ryzen 7 5700X")]
    [InlineData("Intel(R) Core(TM) i7-9700K CPU @ 3.60GHz", "Intel Core i7-9700K @ 3.60GHz")]
    public void Cpu_name_is_cleaned(string raw, string expected) =>
        Assert.Equal(expected, WindowsSystemMonitor.CleanCpuName(raw));

    [Fact]
    public void Real_monitor_reads_this_machine()
    {
        var monitor = new WindowsSystemMonitor(NullLogger.Instance);
        var info = monitor.GetStaticInfo();
        Assert.StartsWith("Windows", info.WindowsVersion);
        Assert.True(info.TotalRamBytes > 0);

        monitor.SampleUsage();
        Thread.Sleep(300);
        var usage = monitor.SampleUsage();
        Assert.NotNull(usage.CpuPercent);
        Assert.InRange(usage.RamPercent, 0.1, 100);
    }

    [Fact]
    public void Shell_match_prefers_exact_and_skips_uninstallers()
    {
        ShellApp[] apps =
        [
            new("Uninstall Steam", "x"), new("Steam Support Center", "y"), new("Steam", "z"),
            new("OBS Studio (64bit)", "obs"),
        ];
        Assert.Equal("z", ShellAppsFolder.Match(apps, ["Steam"])!.ParsingName);
        Assert.Equal("obs", ShellAppsFolder.Match(apps, ["OBS Studio"])!.ParsingName);
        Assert.Null(ShellAppsFolder.Match(apps, ["Discord"]));
    }
}
