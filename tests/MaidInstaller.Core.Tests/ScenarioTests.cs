using MaidInstaller.Core.Assistant;
using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Collections;
using MaidInstaller.Core.Installation;
using MaidInstaller.Core.Logging;

namespace MaidInstaller.Core.Tests;

/// <summary>
/// WinGet simulado por função: cada chamada decide o resultado (código, exceção, demora).
/// Também mede quantas instalações rodaram ao mesmo tempo.
/// </summary>
internal sealed class ScriptedRunner(Func<IReadOnlyList<string>, CancellationToken, Task<int>> behavior) : IWinGetProcessRunner
{
    private int _running;
    public int MaxConcurrent { get; private set; }
    public List<string> Ids { get; } = [];

    public async Task<int> RunStreamingAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken ct)
    {
        var now = Interlocked.Increment(ref _running);
        lock (Ids)
        {
            MaxConcurrent = Math.Max(MaxConcurrent, now);
            Ids.Add(args[args.ToList().IndexOf("--id") + 1]);
        }
        try { return await behavior(args, ct); }
        finally { Interlocked.Decrement(ref _running); }
    }

    public Task<string?> DescribeErrorAsync(int exitCode, CancellationToken ct) => Task.FromResult<string?>(null);
}

/// <summary>Cenários da Etapa 10, na ordem da especificação. Nada é instalado de verdade.</summary>
public class ScenarioTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    private static readonly string CatalogDir = Path.Combine(AppContext.BaseDirectory, "Catalog");
    private static readonly AppCatalog Catalog = CatalogLoader.Load(Path.Combine(CatalogDir, "catalog.json"), NullLogger.Instance);

    private static CatalogApp App(string id) =>
        new() { Id = id, Name = id, Category = "x", OfficialUrl = "https://x.com", WinGetId = $"Maid.{id}", WinGetSource = "winget" };

    private InstallationService Service(ScriptedRunner runner) => new(runner, NullLogger.Instance, _dir.Path);

    private static async Task WaitAll(IEnumerable<InstallJob> jobs)
    {
        for (var i = 0; i < 300 && !jobs.All(j => j.IsFinished); i++) await Task.Delay(20);
        Assert.All(jobs, j => Assert.True(j.IsFinished, $"{j.App.Id} ficou em {j.State}"));
    }

    // ---------- Instalação individual e em lote ----------

    [Fact]
    public async Task Batch_runs_one_at_a_time_in_order_and_a_failure_does_not_stop_the_rest()
    {
        var runner = new ScriptedRunner(async (args, ct) =>
        {
            await Task.Delay(30, ct);
            return args.Contains("Maid.c") ? WinGetExitCodes.NoNetwork : 0;
        });
        var svc = Service(runner);
        var jobs = new[] { "a", "b", "c", "d", "e" }.Select(id => svc.Enqueue(App(id))).ToList();
        await WaitAll(jobs);

        Assert.Equal(1, runner.MaxConcurrent);
        Assert.Equal(["Maid.a", "Maid.b", "Maid.c", "Maid.d", "Maid.e"], runner.Ids);
        Assert.Equal(InstallJobState.Failed, jobs[2].State);
        Assert.All(jobs.Where((_, i) => i != 2), j => Assert.Equal(InstallJobState.Completed, j.State));
    }

    [Fact]
    public void Request_plan_puts_each_app_in_exactly_one_group()
    {
        var a = App("a"); var b = App("b"); var c = App("c");
        var noPkg = new CatalogApp { Id = "riot", Name = "Riot", OfficialUrl = "https://x.com" };
        var badId = new CatalogApp { Id = "bad", Name = "Bad", WinGetId = "x & del *", WinGetSource = "winget" };
        var queued = new InstallJob(c, InstallOperation.Install); // Waiting
        var finished = new InstallJob(b, InstallOperation.Install) { State = InstallJobState.Failed };

        var plan = InstallRequest.Plan([a, b, c, noPkg, badId, a], app => app.Id == "b", [queued, finished]);

        Assert.Equal(["a"], plan.ToInstall.Select(x => x.Id));
        Assert.Equal(["b"], plan.AlreadyInstalled.Select(x => x.Id));
        Assert.Equal(["c"], plan.InQueue.Select(x => x.Id));
        Assert.Equal(["riot", "bad"], plan.NoPackage.Select(x => x.Id)); // ID perigoso nunca chega à fila
    }

    [Fact]
    public void Installed_app_in_queue_is_reported_once()
    {
        var a = App("a");
        var plan = InstallRequest.Plan([a], _ => true, [new InstallJob(a, InstallOperation.Install)]);
        Assert.Single(plan.InQueue);
        Assert.Empty(plan.AlreadyInstalled);
    }

    // ---------- Falhas de internet ----------

    [Theory]
    [InlineData(unchecked((int)0x8A150107))] // NoNetwork
    [InlineData(unchecked((int)0x8A150008))] // DownloadFailed
    [InlineData(unchecked((int)0x8A150086))] // ZeroByteInstaller
    public async Task Internet_failures_are_explained_and_can_be_retried(int code)
    {
        var calls = 0;
        var runner = new ScriptedRunner((_, _) => Task.FromResult(Interlocked.Increment(ref calls) == 1 ? code : 0));
        var svc = Service(runner);
        var job = svc.Enqueue(App("a"));
        await WaitAll([job]);
        Assert.Equal(InstallJobState.Failed, job.State);
        Assert.Contains("internet", job.Message);
        Assert.True(job.CanRetry);

        svc.Retry(job); // a conexão voltou
        await Task.Delay(50);
        await WaitAll([job]);
        Assert.Equal(InstallJobState.Completed, job.State);
    }

    [Fact]
    public async Task Hash_mismatch_is_never_installed_silently()
    {
        var svc = Service(new ScriptedRunner((_, _) => Task.FromResult(WinGetExitCodes.HashMismatch)));
        var job = svc.Enqueue(App("a"));
        await WaitAll([job]);
        Assert.Equal(InstallJobState.Failed, job.State);
        Assert.Contains("hash", job.Message);
    }

    [Fact]
    public async Task Missing_winget_fails_clearly_and_the_queue_keeps_going()
    {
        var runner = new ScriptedRunner((args, _) => args.Contains("Maid.a")
            ? throw new System.ComponentModel.Win32Exception(2, "O sistema não pode encontrar o arquivo especificado")
            : Task.FromResult(0));
        var svc = Service(runner);
        var a = svc.Enqueue(App("a"));
        var b = svc.Enqueue(App("b"));
        await WaitAll([a, b]);
        Assert.Equal(InstallJobState.Failed, a.State);
        Assert.Contains("WinGet não foi encontrado", a.Message);
        Assert.Equal(InstallJobState.Completed, b.State);
    }

    [Fact]
    public async Task Unexpected_error_does_not_kill_the_queue()
    {
        var runner = new ScriptedRunner((args, _) => args.Contains("Maid.a") ? throw new InvalidOperationException("boom") : Task.FromResult(0));
        var svc = Service(runner);
        var a = svc.Enqueue(App("a"));
        var b = svc.Enqueue(App("b"));
        await WaitAll([a, b]);
        Assert.Equal(InstallJobState.Failed, a.State);
        Assert.Equal(InstallJobState.Completed, b.State);
    }

    // ---------- Cancelamento ----------

    [Fact]
    public async Task Cancel_all_stops_running_and_waiting_without_running_the_rest()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new ScriptedRunner(async (_, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        });
        var svc = Service(runner);
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.QueueDrained += () => drained.TrySetResult();
        var jobs = new[] { "a", "b", "c" }.Select(id => svc.Enqueue(App(id))).ToList();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        svc.CancelAll();
        await WaitAll(jobs);
        await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(jobs, j => Assert.Equal(InstallJobState.Cancelled, j.State));
        Assert.Single(runner.Ids);
        Assert.False(svc.IsBusy);
    }

    [Fact]
    public async Task Uac_refusal_cancels_only_that_app()
    {
        var runner = new ScriptedRunner((args, _) => Task.FromResult(args.Contains("Maid.a") ? WinGetExitCodes.WindowsCancelled : 0));
        var svc = Service(runner);
        var a = svc.Enqueue(App("a"));
        var b = svc.Enqueue(App("b"));
        await WaitAll([a, b]);
        Assert.Equal(InstallJobState.Cancelled, a.State);
        Assert.Contains("UAC", a.Message);
        Assert.Equal(InstallJobState.Completed, b.State);
    }

    // ---------- Aplicativos já instalados ----------

    [Fact]
    public async Task Already_installed_apps_never_call_winget_and_are_not_in_history()
    {
        var runner = new ScriptedRunner((_, _) => Task.FromResult(0));
        var svc = Service(runner);
        var history = new InstallHistory(Path.Combine(_dir.Path, "historico.json"), NullLogger.Instance);
        svc.JobFinished += history.Record;

        var job = svc.AddAlreadyInstalled(App("a"));
        await Task.Delay(100);
        Assert.Equal(InstallJobState.AlreadyInstalled, job.State);
        Assert.Empty(runner.Ids);
        Assert.Empty(history.Entries);
    }

    [Theory]
    [InlineData(unchecked((int)0x8A150061), "Já estava instalado")]
    [InlineData(unchecked((int)0x8A15010D), "Outra versão")]
    [InlineData(unchecked((int)0x8A15010E), "mais nova")]
    public async Task Winget_already_installed_codes_change_nothing(int code, string expected)
    {
        var svc = Service(new ScriptedRunner((_, _) => Task.FromResult(code)));
        var job = svc.Enqueue(App("a"));
        await WaitAll([job]);
        Assert.Equal(InstallJobState.AlreadyInstalled, job.State);
        Assert.Contains(expected, job.Message);
    }

    // ---------- Atualizações ----------

    [Fact]
    public async Task Upgrade_that_is_no_longer_needed_is_not_a_failure()
    {
        var svc = Service(new ScriptedRunner((args, _) =>
        {
            Assert.Equal("upgrade", args[0]);
            return Task.FromResult(WinGetExitCodes.UpdateNotApplicable);
        }));
        var job = svc.Enqueue(App("a"), InstallOperation.Upgrade);
        await WaitAll([job]);
        Assert.Equal(InstallJobState.AlreadyInstalled, job.State);
        Assert.Contains("mais recente", job.Message);
    }

    [Fact]
    public async Task Successful_upgrade_says_update_not_install()
    {
        var svc = Service(new ScriptedRunner((_, _) => Task.FromResult(0)));
        var upgrade = svc.Enqueue(App("a"), InstallOperation.Upgrade);
        var install = svc.Enqueue(App("b"), InstallOperation.Install);
        await WaitAll([upgrade, install]);
        Assert.Equal("Atualização concluída.", upgrade.Message);
        Assert.Equal("Instalação concluída.", install.Message);
    }

    [Fact]
    public void Install_never_upgrades_and_upgrade_is_explicit_per_package()
    {
        var app = App("a");
        var install = InstallationService.BuildArguments(app, InstallOperation.Install);
        var upgrade = InstallationService.BuildArguments(app, InstallOperation.Upgrade);
        Assert.Contains("--no-upgrade", install);
        Assert.Equal(["upgrade", "--id", "Maid.a", "--exact"], upgrade.Take(4));
        Assert.DoesNotContain("--all", upgrade);
    }

    // ---------- Importação e exportação ----------

    [Fact]
    public void Export_import_roundtrip_with_accents_and_emoji_in_name()
    {
        var store = new CollectionStore(Path.Combine(_dir.Path, "Lists"), CollectionKind.List, NullLogger.Instance);
        var list = new AppCollection { Id = AppCollection.NewId("Lista do Hélder ♡"), Name = "Lista do Hélder ♡", AppIds = ["discord", "7zip", "vlc"] };
        store.Save(list);
        var file = Path.Combine(_dir.Path, "exportada" + CollectionTransfer.FileExtension);
        CollectionTransfer.Export(list, CollectionKind.List, Catalog, file);

        var result = CollectionTransfer.Import(file, CollectionKind.List, Catalog);
        Assert.Equal("Lista do Hélder ♡", result.Collection.Name);
        Assert.Equal(["discord", "7zip", "vlc"], result.Collection.AppIds);
        Assert.Empty(result.Unknown);
    }

    [Fact]
    public void Profile_exported_can_be_imported_as_a_list()
    {
        var profile = new AppCollection { Id = "meu-perfil-abc123", Name = "Meu perfil", AppIds = ["steam"] };
        var file = Path.Combine(_dir.Path, "perfil" + CollectionTransfer.FileExtension);
        CollectionTransfer.Export(profile, CollectionKind.Profile, Catalog, file);
        var result = CollectionTransfer.Import(file, CollectionKind.List, Catalog);
        Assert.Equal(["steam"], result.Collection.AppIds);
    }

    // ---------- Windows 10 sem WinGet ----------

    [Fact]
    public void Without_winget_missing_from_start_menu_is_not_verified_instead_of_not_installed()
    {
        var app = new CatalogApp { Id = "7zip", Name = "7-Zip", WinGetId = "7zip.7zip", WinGetSource = "winget", StartMenuNames = ["7-Zip File Manager"] };
        Assert.Equal(InstallState.NotVerified, InstallStatusResolver.Resolve(app, winget: null, shell: []).State);
        Assert.Equal(InstallState.NotInstalled, InstallStatusResolver.Resolve(app, winget: [], shell: []).State);
        Assert.Equal(InstallState.Unknown, InstallStatusResolver.Resolve(app, null, null).State);
    }

    [Fact]
    public void Components_have_detection_that_does_not_need_winget()
    {
        Assert.NotNull(PostFormat.RuntimeDetector.For("vcredist-x64"));
        Assert.NotNull(PostFormat.RuntimeDetector.For("vcredist-x86"));
        Assert.NotNull(PostFormat.RuntimeDetector.For("directx"));
        using var sys = new TempDir();
        Assert.Null(PostFormat.RuntimeDetector.DirectXLegacy(sys.Path));
        File.WriteAllText(Path.Combine(sys.Path, "d3dx9_43.dll"), "");
        Assert.NotNull(PostFormat.RuntimeDetector.DirectXLegacy(sys.Path));
    }

    // ---------- Funcionamento sem IA e offline ----------

    private static KnowledgeBase FullKb()
    {
        var kb = KnowledgeBase.Load(Path.Combine(CatalogDir, "knowledge.json"));
        kb.AddFromCatalog(Catalog);
        return kb;
    }

    [Fact]
    public void Every_catalog_app_has_an_offline_answer_with_its_developer()
    {
        var offline = new OfflineResponder(FullKb(), () => "Helder");
        Assert.All(Catalog.Apps, app =>
        {
            var answer = offline.Answer($"Para que serve o {app.Name}?");
            Assert.DoesNotContain("não tenho essa informação", answer);
            Assert.Contains(app.Developer.Split(' ')[0], answer);
        });
    }

    [Fact]
    public void Every_topic_is_reachable_by_its_first_keyword()
    {
        var kb = FullKb();
        var offline = new OfflineResponder(kb, () => "Helder");
        Assert.All(kb.Topics, t =>
        {
            var answer = offline.Answer("me fala sobre " + t.Keywords[0]);
            Assert.Equal(t.Answer.Replace("{nome}", "Helder"), answer);
        });
    }

    [Theory]
    [InlineData("Para que serve o Discord?")]
    [InlineData("Quais apps são essenciais?")]
    [InlineData("Qual navegador escolher?")]
    public async Task Suggested_questions_answer_without_model(string question)
    {
        var answer = await new HybridResponder(FullKb(), () => "Helder").AnswerAsync(question);
        Assert.NotEqual(AnswerSource.Model, answer.Source);
        Assert.DoesNotContain("não tenho essa informação", answer.Text);
    }

    [Fact]
    public void Local_information_loads_from_bundled_files_only()
    {
        // Catálogo, perfis e base de conhecimento vêm de arquivos que acompanham o app: nenhum acesso à rede.
        Assert.Equal(61, Catalog.Apps.Count);
        Assert.NotEmpty(DefaultProfiles.Load(Path.Combine(CatalogDir, "profiles.json"), Catalog, NullLogger.Instance));
        Assert.True(FullKb().Topics.Count >= 13);
    }
}
