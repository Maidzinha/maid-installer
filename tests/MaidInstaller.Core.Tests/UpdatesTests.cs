using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Installation;
using MaidInstaller.Core.Logging;
using MaidInstaller.Core.WinGet;

namespace MaidInstaller.Core.Tests;

public sealed class UpdatesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "maid-up-" + Guid.NewGuid().ToString("N"));
    public UpdatesTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    // Trecho real do "winget upgrade" deste PC (2026-11-04), com a animação de progresso no começo.
    private const string English = "   - \r   \\ \r\n" + """
        Name                                        Id                        Version             Available           Source
        --------------------------------------------------------------------------------------------------------------------
        App Installer                               Microsoft.AppInstaller    1.29.379.0          1.29.380            winget
        Git                                         Git.Git                   2.56.0.3            2.56.0.4            winget
        Microsoft Teams                             Microsoft.Teams           26246.1604.5133.838 26276.513.5241.9675 winget
        Python Launcher                             Python.Launcher           < 3.14.7            3.14.7              winget
        WinRAR 7.13 (64-bit)                        RARLab.WinRAR             7.13.0              7.23.0              winget
        17 upgrades available.
        4 package(s) have version numbers that cannot be determined. Use --include-unknown to see all results.
        """;

    [Fact]
    public void Parses_real_english_table()
    {
        var list = WinGetUpgradeParser.Parse(English);
        Assert.Equal(5, list.Count);
        Assert.Equal(new UpgradeCandidate("Git", "Git.Git", "2.56.0.3", "2.56.0.4", "winget"), list[1]);
        Assert.Equal("26276.513.5241.9675", list[2].AvailableVersion);
        Assert.Equal("< 3.14.7", list[3].InstalledVersion);
        Assert.Equal("WinRAR 7.13 (64-bit)", list[4].Name);
    }

    [Fact]
    public void Parses_portuguese_headers_and_truncated_ids()
    {
        const string pt = """
            Nome                             ID                             Versão       Disponível   Origem
            ----------------------------------------------------------------------------------------------
            Discord                          Discord.Discord                1.0.9260     1.0.9267     winget
            Um programa com nome muito gran… Fornecedor.ProgramaComNomeGra… 1.0          2.0          winget
            WhatsApp                         9NKSQGP7F2NH                   2.2540.1.0   2.2541.2.0   msstore
            3 atualizações disponíveis.
            """;
        var list = WinGetUpgradeParser.Parse(pt);
        Assert.Equal(3, list.Count);
        Assert.False(list[0].IdTruncated);
        Assert.True(list[1].IdTruncated);
        Assert.Equal("Fornecedor.ProgramaComNomeGra", list[1].Id);
        Assert.Equal("Um programa com nome muito gran", list[1].Name);
        Assert.Equal("msstore", list[2].Source);
    }

    [Theory]
    [InlineData("No installed package found matching input criteria.")]
    [InlineData("Nenhum pacote instalado encontrado que corresponda aos critérios de entrada.")]
    [InlineData("")]
    public void No_table_means_no_upgrades(string output) => Assert.Empty(WinGetUpgradeParser.Parse(output));

    [Fact]
    public void Upgrade_arguments_never_use_no_upgrade_and_are_exact()
    {
        var app = new CatalogApp { Id = "git", Name = "Git", WinGetId = "Git.Git", WinGetSource = "winget" };
        var args = InstallationService.BuildArguments(app, InstallOperation.Upgrade);
        Assert.Equal("upgrade", args[0]);
        Assert.Contains("--exact", args);
        Assert.DoesNotContain("--no-upgrade", args);
        Assert.DoesNotContain("--all", args); // nunca "atualizar tudo" sem lista explícita
    }

    [Fact]
    public void History_records_only_started_jobs_newest_first()
    {
        var history = new InstallHistory(Path.Combine(_dir, "historico.json"), NullLogger.Instance);
        var app = new CatalogApp { Id = "git", Name = "Git", WinGetId = "Git.Git", WinGetSource = "winget" };

        var neverStarted = new InstallJob(app, InstallOperation.Upgrade) { State = InstallJobState.Cancelled };
        history.Record(neverStarted);
        Assert.Empty(history.Entries);

        var ok = new InstallJob(app, InstallOperation.Upgrade)
        {
            FromVersion = "2.56.0.3", ToVersion = "2.56.0.4",
            StartedAt = DateTime.Now.AddMinutes(-1), FinishedAt = DateTime.Now, State = InstallJobState.Completed,
        };
        history.Record(ok);
        var failed = new InstallJob(app, InstallOperation.Install) { StartedAt = DateTime.Now, FinishedAt = DateTime.Now.AddSeconds(5), State = InstallJobState.Failed };
        history.Record(failed);

        var reloaded = new InstallHistory(Path.Combine(_dir, "historico.json"), NullLogger.Instance).Entries;
        Assert.Equal(2, reloaded.Count);
        Assert.Equal("failed", reloaded[0].Result);
        Assert.Equal("install", reloaded[0].Operation);
        Assert.Equal("upgrade", reloaded[1].Operation);
        Assert.Equal("2.56.0.3", reloaded[1].FromVersion);
        Assert.Equal("2.56.0.4", reloaded[1].ToVersion);
    }

    [Fact]
    public void History_is_capped()
    {
        var history = new InstallHistory(Path.Combine(_dir, "h.json"), NullLogger.Instance);
        for (var i = 0; i < InstallHistory.MaxEntries + 20; i++) history.Add(new HistoryEntry { Name = $"App {i}", Result = "completed" });
        Assert.Equal(InstallHistory.MaxEntries, history.Entries.Count);
        Assert.Equal($"App {InstallHistory.MaxEntries + 19}", history.Entries[0].Name);
    }

    [Fact]
    public void Corrupt_history_is_backed_up_and_restarted()
    {
        var path = Path.Combine(_dir, "ruim.json");
        File.WriteAllText(path, "{ não é uma lista");
        var history = new InstallHistory(path, NullLogger.Instance);
        Assert.Empty(history.Entries);
        Assert.Single(Directory.GetFiles(_dir, "ruim.json.corrompido-*"));
    }
}
