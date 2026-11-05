using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Installation;
using MaidInstaller.Core.Logging;

namespace MaidInstaller.Core.Tests;

/// <summary>Simula o WinGet: devolve linhas e códigos combinados, sem instalar nada.</summary>
internal sealed class FakeRunner : IWinGetProcessRunner
{
    public Queue<(string[] lines, int exit, bool waitForCancel)> Script { get; } = new();
    public List<IReadOnlyList<string>> Calls { get; } = [];
    public TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<int> RunStreamingAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken ct)
    {
        Calls.Add(args);
        var (lines, exit, wait) = Script.Count > 0 ? Script.Dequeue() : ([], 0, false);
        foreach (var l in lines) onLine(l);
        Started.TrySetResult();
        if (wait) await Task.Delay(Timeout.Infinite, ct);
        return exit;
    }

    public void ResetStarted() => Started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<string?> DescribeErrorAsync(int exitCode, CancellationToken ct) => Task.FromResult<string?>("descrição oficial");
}

public class InstallationTests : IDisposable
{
    private readonly TempDir _logs = new();
    public void Dispose() => _logs.Dispose();

    private static CatalogApp App(string id = "discord", string wingetId = "Discord.Discord") =>
        new() { Id = id, Name = id, Category = "x", OfficialUrl = "https://x.com", WinGetId = wingetId, WinGetSource = "winget" };

    private (InstallationService svc, FakeRunner runner) Create()
    {
        var runner = new FakeRunner();
        return (new InstallationService(runner, NullLogger.Instance, _logs.Path), runner);
    }

    private static async Task<InstallJob> WaitFinished(InstallationService svc, InstallJob job)
    {
        for (var i = 0; i < 200 && !job.IsFinished; i++) await Task.Delay(20);
        Assert.True(job.IsFinished, $"job não terminou (estado {job.State})");
        return job;
    }

    [Fact]
    public async Task Successful_install_goes_through_real_stages_and_writes_log()
    {
        var (svc, runner) = Create();
        var states = new List<InstallJobState>();
        runner.Script.Enqueue((["Found Discord [Discord.Discord] Version 1.0", "Downloading https://x/setup.exe",
            "Successfully verified installer hash", "Starting package install...", "Successfully installed"], 0, false));

        var job = svc.Enqueue(App());
        job.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(InstallJob.State)) states.Add(job.State); };
        await WaitFinished(svc, job);

        Assert.Equal(InstallJobState.Completed, job.State);
        Assert.Contains(InstallJobState.Installing, states);
        Assert.Null(job.Progress); // nenhuma porcentagem inventada
        Assert.True(File.Exists(job.LogFile));
        Assert.Contains("Successfully installed", File.ReadAllText(job.LogFile!));
    }

    [Fact]
    public async Task Known_codes_become_friendly_results()
    {
        var (svc, runner) = Create();
        runner.Script.Enqueue(([], WinGetExitCodes.PackageAlreadyInstalled, false));
        runner.Script.Enqueue(([], WinGetExitCodes.NoNetwork, false));
        runner.Script.Enqueue(([], WinGetExitCodes.WindowsCancelled, false));
        runner.Script.Enqueue(([], WinGetExitCodes.RebootRequiredToFinish, false));

        var a = svc.Enqueue(App("a", "A.A"));
        var b = svc.Enqueue(App("b", "B.B"));
        var c = svc.Enqueue(App("c", "C.C"));
        var d = svc.Enqueue(App("d", "D.D"));
        await WaitFinished(svc, d);

        Assert.Equal(InstallJobState.AlreadyInstalled, a.State);
        Assert.Equal(InstallJobState.Failed, b.State);
        Assert.Contains("internet", b.Message);
        Assert.Equal(InstallJobState.Cancelled, c.State);
        Assert.Contains("UAC", c.Message);
        Assert.Equal(InstallJobState.Completed, d.State);
        Assert.True(d.RebootRequired);
        Assert.Equal(4, runner.Calls.Count); // executados um por vez, na ordem
    }

    [Fact]
    public async Task Unknown_code_uses_official_winget_description()
    {
        var (svc, runner) = Create();
        runner.Script.Enqueue(([], unchecked((int)0x8A150049), false));
        var job = await WaitFinished(svc, svc.Enqueue(App()));
        Assert.Equal(InstallJobState.Failed, job.State);
        Assert.Contains("0x8A150049", job.Message);
        Assert.Contains("descrição oficial", job.Message);
    }

    [Fact]
    public async Task Cancel_running_and_waiting_jobs_then_retry()
    {
        var (svc, runner) = Create();
        runner.Script.Enqueue((["Downloading https://x"], 0, true)); // fica rodando até cancelar
        var running = svc.Enqueue(App("a", "A.A"));
        var waiting = svc.Enqueue(App("b", "B.B"));
        await runner.Started.Task;

        svc.Cancel(waiting);
        Assert.Equal(InstallJobState.Cancelled, waiting.State);
        svc.Cancel(running);
        await WaitFinished(svc, running);
        Assert.Equal(InstallJobState.Cancelled, running.State);
        Assert.Single(runner.Calls); // o job cancelado na fila nunca rodou

        runner.Script.Enqueue(([], 0, false));
        svc.Retry(waiting);
        await Task.Delay(50);
        await WaitFinished(svc, waiting);
        Assert.Equal(InstallJobState.Completed, waiting.State);
    }

    [Fact]
    public async Task Queue_drained_event_fires_when_everything_is_done()
    {
        var (svc, _) = Create();
        var drained = new TaskCompletionSource();
        svc.QueueDrained += () => drained.TrySetResult();
        svc.Enqueue(App("a", "A.A"));
        svc.Enqueue(App("b", "B.B"));
        await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(svc.IsBusy);
    }

    [Fact]
    public void Install_arguments_are_safe_and_never_upgrade()
    {
        var args = InstallationService.BuildArguments(App(), InstallOperation.Install);
        Assert.Equal(["install", "--id", "Discord.Discord", "--exact", "--source", "winget", "--silent", "--no-upgrade",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"], args);
        Assert.DoesNotContain("--no-upgrade", InstallationService.BuildArguments(App(), InstallOperation.Upgrade));
    }

    [Fact]
    public void Apps_without_valid_package_are_refused()
    {
        var (svc, _) = Create();
        Assert.Throws<ArgumentException>(() => svc.Enqueue(new CatalogApp { Id = "riot", Name = "Riot", OfficialUrl = "https://x.com" }));
        Assert.Throws<ArgumentException>(() => svc.Enqueue(App("x", "Evil.App & calc")));
    }

    [Theory]
    [InlineData("  ██████▒▒▒▒  12.0 MB / 48.0 MB", 25.0)]
    [InlineData("\u001b[2K  50%", 50.0)]
    public void Real_percentages_are_parsed_when_winget_sends_them(string line, double expected) =>
        Assert.Equal(expected, WinGetOutputParser.Parse(line)!.Percent!.Value, 1);

    [Fact]
    public void Spinner_lines_carry_no_fake_progress() =>
        Assert.Null(WinGetOutputParser.Parse("   - ")?.Percent);
}
