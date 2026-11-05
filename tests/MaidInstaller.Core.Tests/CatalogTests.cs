using MaidInstaller.Core.Assistant;
using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Logging;
using MaidInstaller.Core.Shell;
using MaidInstaller.Core.WinGet;

namespace MaidInstaller.Core.Tests;

public class CatalogTests
{
    private static readonly string CatalogPath = Path.Combine(AppContext.BaseDirectory, "Catalog", "catalog.json");

    private static AppCatalog Raw => Storage.JsonStore.Read<AppCatalog>(CatalogPath)!;

    [Fact]
    public void Shipped_catalog_is_fully_valid()
    {
        var problems = CatalogValidator.Validate(Raw, out var clean);
        Assert.Empty(problems);
        Assert.Equal(39, clean.Apps.Count);
        Assert.Equal(8, clean.Categories.Count);
    }

    [Fact]
    public void Every_category_has_apps_and_required_fields()
    {
        var c = Raw;
        Assert.All(c.Categories, cat => Assert.Contains(c.Apps, a => a.Category == cat.Id));
        Assert.All(c.Apps, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Description));
            Assert.False(string.IsNullOrWhiteSpace(a.Developer));
            Assert.False(string.IsNullOrWhiteSpace(a.Price));
            Assert.StartsWith("https://", a.OfficialUrl);
            if (a.WinGetId is not null) Assert.NotNull(a.WinGetSource);
        });
    }

    [Fact]
    public void Apps_without_winget_package_are_explained()
    {
        foreach (var a in Raw.Apps.Where(a => a.WinGetId is null))
            Assert.False(string.IsNullOrWhiteSpace(a.Notes), $"{a.Id} precisa explicar por que não tem WinGet");
    }

    [Theory]
    [InlineData("Google.Chrome & calc.exe")]
    [InlineData("Google.Chrome;rm")]
    [InlineData("SemPonto")]
    [InlineData("Foo Bar.Baz")]
    [InlineData("--source=evil")]
    public void Dangerous_or_malformed_ids_are_rejected(string id)
    {
        var catalog = new AppCatalog
        {
            Categories = [new() { Id = "x", Name = "X" }],
            Apps = [new() { Id = "bad", Name = "Bad", Category = "x", OfficialUrl = "https://a.com", WinGetId = id, WinGetSource = "winget" }],
        };
        Assert.Single(CatalogValidator.Validate(catalog, out var clean));
        Assert.Empty(clean.Apps);
    }

    [Theory]
    [InlineData("http://inseguro.com", "winget")]
    [InlineData("https://ok.com", "site-aleatorio")]
    public void Insecure_url_or_unknown_source_is_rejected(string url, string source)
    {
        var catalog = new AppCatalog
        {
            Categories = [new() { Id = "x", Name = "X" }],
            Apps = [new() { Id = "a", Name = "A", Category = "x", OfficialUrl = url, WinGetId = "A.B", WinGetSource = source }],
        };
        Assert.Single(CatalogValidator.Validate(catalog, out _));
    }

    [Fact]
    public void Export_json_is_parsed_with_sources_and_versions()
    {
        const string json = """
            {"Sources":[
              {"Packages":[{"PackageIdentifier":"Git.Git","Version":"2.56.0.3"}],"SourceDetails":{"Name":"winget"}},
              {"Packages":[{"PackageIdentifier":"9NKSQGP7F2NH"}],"SourceDetails":{"Name":"msstore"}}]}
            """;
        var list = WinGetClient.ParseExport(json);
        Assert.Equal(2, list.Count);
        Assert.Equal(new InstalledPackage("Git.Git", "winget", "2.56.0.3"), list[0]);
        Assert.Null(list[1].Version);
    }

    [Theory]
    [InlineData("Found Discord [Discord.Discord]\nVersion: 1.0.9267\nPublisher: Discord Inc.", "1.0.9267")]
    [InlineData("Encontrado X [A.B]\nVersão: 2.0\n", "2.0")]
    [InlineData("No package found matching input criteria.", null)]
    public void Show_version_is_parsed(string output, string? expected) =>
        Assert.Equal(expected, WinGetClient.ParseShowVersion(output));

    [Fact]
    public void Status_prefers_winget_then_start_menu()
    {
        var git = new CatalogApp { Id = "git", Name = "Git", WinGetId = "Git.Git", WinGetSource = "winget", StartMenuNames = ["Git Bash"] };
        var wa = new CatalogApp { Id = "wa", Name = "WhatsApp", WinGetId = "9NKSQGP7F2NH", WinGetSource = "msstore", StartMenuNames = ["WhatsApp"] };
        var vlc = new CatalogApp { Id = "vlc", Name = "VLC", WinGetId = "VideoLAN.VLC", WinGetSource = "winget", StartMenuNames = ["VLC media player"] };
        InstalledPackage[] winget = [new("git.git", "winget", "2.56")];
        ShellApp[] shell = [new("WhatsApp", "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App")];

        Assert.Equal(new AppInstallStatus(InstallState.Installed, "2.56"), InstallStatusResolver.Resolve(git, winget, shell));
        Assert.Equal(InstallState.DetectedInStartMenu, InstallStatusResolver.Resolve(wa, winget, shell).State);
        Assert.Equal(InstallState.NotInstalled, InstallStatusResolver.Resolve(vlc, winget, shell).State);
        Assert.Equal(InstallState.Unknown, InstallStatusResolver.Resolve(vlc, null, null).State);
    }

    [Fact]
    public void Assistant_knows_catalog_apps_without_own_entry()
    {
        var kb = KnowledgeBase.Load(Path.Combine(AppContext.BaseDirectory, "Catalog", "knowledge.json"));
        kb.AddFromCatalog(CatalogLoader.Load(CatalogPath, NullLogger.Instance));
        var answer = new OfflineResponder(kb, () => "Helder").Answer("Para que serve o Everything?");
        Assert.Contains("voidtools", answer);
        Assert.Equal(kb.Apps.Count, kb.Apps.Select(a => a.Id).Distinct().Count());
    }
}
