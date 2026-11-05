using System.Text;
using System.Threading.Channels;
using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Logging;
using MaidInstaller.Core.WinGet;

namespace MaidInstaller.Core.Installation;

/// <summary>Abstração do processo do WinGet (permite testar a fila sem instalar nada de verdade).</summary>
public interface IWinGetProcessRunner
{
    Task<int> RunStreamingAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken ct);
    Task<string?> DescribeErrorAsync(int exitCode, CancellationToken ct);
}

/// <summary>
/// Mecanismo de instalação: fila executada uma por vez (o Windows não instala vários MSI ao mesmo tempo),
/// com log por instalação, cancelamento e nova tentativa. É o ÚNICO lugar que instala algo —
/// a Maid Assistant só recebe os eventos.
/// </summary>
public sealed class InstallationService
{
    private readonly IWinGetProcessRunner _runner;
    private readonly IAppLogger _logger;
    private readonly string _logsDir;
    private readonly Channel<InstallJob> _queue = Channel.CreateUnbounded<InstallJob>(new UnboundedChannelOptions { SingleReader = true });
    private readonly List<InstallJob> _jobs = [];
    private readonly Lock _lock = new();

    public InstallationService(IWinGetProcessRunner runner, IAppLogger logger, string logsDir)
    {
        _runner = runner;
        _logger = logger;
        _logsDir = logsDir;
        _ = Task.Run(WorkerAsync);
    }

    /// <summary>Um job entrou na fila.</summary>
    public event Action<InstallJob>? JobAdded;
    /// <summary>Um job começou a rodar.</summary>
    public event Action<InstallJob>? JobStarted;
    /// <summary>Um job terminou (qualquer resultado).</summary>
    public event Action<InstallJob>? JobFinished;
    /// <summary>Não há mais nada aguardando nem rodando.</summary>
    public event Action? QueueDrained;

    public IReadOnlyList<InstallJob> Jobs
    {
        get { lock (_lock) return [.. _jobs]; }
    }

    public bool IsBusy
    {
        get { lock (_lock) return _jobs.Any(j => j.State == InstallJobState.Waiting || j.IsActive); }
    }

    /// <summary>Coloca um app na fila. Apps sem pacote WinGet válido são recusados.</summary>
    public InstallJob Enqueue(CatalogApp app, InstallOperation operation = InstallOperation.Install)
    {
        if (!WinGetClient.IsValidPackage(app))
            throw new ArgumentException($"{app.Name} não tem um pacote WinGet válido.", nameof(app));

        var job = new InstallJob(app, operation);
        lock (_lock) _jobs.Add(job);
        JobAdded?.Invoke(job);
        _queue.Writer.TryWrite(job);
        _logger.Info($"Na fila: {app.Name} ({app.WinGetId}, {operation}).");
        return job;
    }

    /// <summary>Registra um app que já estava instalado, sem executar nada.</summary>
    public InstallJob AddAlreadyInstalled(CatalogApp app)
    {
        var job = new InstallJob(app, InstallOperation.Install)
        {
            State = InstallJobState.AlreadyInstalled,
            Message = L.T("Já estava instalado. Nada foi alterado."),
            FinishedAt = DateTime.Now,
        };
        lock (_lock) _jobs.Add(job);
        JobAdded?.Invoke(job);
        return job;
    }

    public void Cancel(InstallJob job)
    {
        lock (_lock)
        {
            if (job.State == InstallJobState.Waiting)
            {
                job.CancelRequested = true;
                job.State = InstallJobState.Cancelled;
                job.Message = L.T("Cancelado antes de começar.");
                job.FinishedAt = DateTime.Now;
                _logger.Info($"Cancelado na fila: {job.App.Name}.");
            }
            else if (job.IsActive)
            {
                job.CancelRequested = true;
                job.Message = "Cancelando…";
                job.Cancellation?.Cancel();
            }
        }
        if (job.State == InstallJobState.Cancelled) JobFinished?.Invoke(job);
    }

    public void CancelAll()
    {
        foreach (var job in Jobs.Where(j => j.CanCancel)) Cancel(job);
    }

    /// <summary>Tenta de novo um job que falhou ou foi cancelado.</summary>
    public void Retry(InstallJob job)
    {
        lock (_lock)
        {
            if (!job.CanRetry) return;
            job.CancelRequested = false;
            job.ExitCode = null;
            job.Progress = null;
            job.RebootRequired = false;
            job.FinishedAt = null;
            job.State = InstallJobState.Waiting;
            job.Message = L.T("Aguardando para tentar de novo.");
        }
        _queue.Writer.TryWrite(job);
        _logger.Info($"Nova tentativa: {job.App.Name}.");
    }

    /// <summary>Remove da lista os jobs já terminados.</summary>
    public IReadOnlyList<InstallJob> ClearFinished()
    {
        lock (_lock)
        {
            var finished = _jobs.Where(j => j.IsFinished).ToList();
            _jobs.RemoveAll(j => j.IsFinished);
            return finished;
        }
    }

    public static IReadOnlyList<string> BuildArguments(CatalogApp app, InstallOperation operation)
    {
        if (!WinGetClient.IsValidPackage(app))
            throw new ArgumentException("Pacote inválido.", nameof(app));

        var args = new List<string>
        {
            operation == InstallOperation.Upgrade ? "upgrade" : "install",
            "--id", app.WinGetId!, "--exact", "--source", app.WinGetSource!,
            "--silent",
        };
        // Sem isso, "winget install" atualiza o que já existe: atualizar só com autorização (Etapa 7).
        if (operation == InstallOperation.Install) args.Add("--no-upgrade");
        args.AddRange(["--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"]);
        return args;
    }

    private async Task WorkerAsync()
    {
        await foreach (var job in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (job.State != InstallJobState.Waiting || job.CancelRequested) continue;
            try
            {
                await RunJobAsync(job).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"Erro inesperado instalando {job.App.Name}.", ex);
                Finish(job, InstallJobState.Failed, L.T("Erro inesperado. Detalhes nos logs."));
            }
            if (!IsBusy) QueueDrained?.Invoke();
        }
    }

    private async Task RunJobAsync(InstallJob job)
    {
        using var cts = new CancellationTokenSource();
        lock (_lock)
        {
            if (job.CancelRequested) return;
            job.Cancellation = cts;
            job.StartedAt = DateTime.Now;
            job.State = InstallJobState.Downloading;
            job.Message = L.T("Procurando o pacote na fonte oficial…");
        }
        JobStarted?.Invoke(job);

        var args = BuildArguments(job.App, job.Operation);
        Directory.CreateDirectory(Path.Combine(_logsDir, "Instalacoes"));
        job.LogFile = Path.Combine(_logsDir, "Instalacoes", $"{DateTime.Now:yyyyMMdd-HHmmss}-{job.App.Id}.log");

        await using var log = new StreamWriter(job.LogFile, append: false, Encoding.UTF8);
        await log.WriteLineAsync($"Maid Installer — {job.Operation} de {job.App.Name}");
        await log.WriteLineAsync($"Início: {job.StartedAt:dd/MM/yyyy HH:mm:ss}");
        await log.WriteLineAsync($"Comando: winget {string.Join(' ', args)}");
        await log.WriteLineAsync(new string('-', 60));

        var logLock = new Lock();
        void OnLine(string line)
        {
            lock (logLock) log.WriteLine(line);
            var signal = WinGetOutputParser.Parse(line);
            if (signal is null || job.CancelRequested) return;
            if (signal.Stage == InstallJobState.Installing && job.State == InstallJobState.Downloading)
            {
                job.State = InstallJobState.Installing;
                job.Progress = null; // a porcentagem do download não vale para a instalação
            }
            if (signal.Percent is { } p) job.Progress = p;
            if (signal.Message is { } m) job.Message = m;
        }

        int exitCode;
        try
        {
            exitCode = await _runner.RunStreamingAsync(args, OnLine, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (logLock) log.WriteLine("[Maid Installer] Cancelado pelo usuário.");
            Finish(job, InstallJobState.Cancelled,
                L.T("Cancelado por você. Se o instalador já tinha começado, ele pode terminar sozinho."));
            return;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Finish(job, InstallJobState.Failed, L.T("O WinGet não foi encontrado. Use o Kit Pós-Formatação para instalá-lo."));
            return;
        }

        var info = WinGetExitCodes.Describe(exitCode);
        var message = info.Outcome == InstallOutcome.Success && job.Operation == InstallOperation.Upgrade
            ? L.T("Atualização concluída.")
            : info.Message;
        if (!info.Known && await _runner.DescribeErrorAsync(exitCode, CancellationToken.None).ConfigureAwait(false) is { } detail)
            message += $" WinGet: {detail}";

        job.ExitCode = exitCode;
        job.RebootRequired = info.RebootRequired;
        lock (logLock) log.WriteLine($"[Maid Installer] Código de saída {WinGetExitCodes.Hex(exitCode)} — {message}");

        Finish(job, info.Outcome switch
        {
            InstallOutcome.Success => InstallJobState.Completed,
            InstallOutcome.AlreadyInstalled => InstallJobState.AlreadyInstalled,
            InstallOutcome.Cancelled => InstallJobState.Cancelled,
            _ => InstallJobState.Failed,
        }, message);
    }

    private void Finish(InstallJob job, InstallJobState state, string message)
    {
        lock (_lock)
        {
            job.Progress = null;
            job.Message = message;
            job.FinishedAt = DateTime.Now;
            job.State = state;
            job.Cancellation = null;
        }
        _logger.Info($"Resultado: {job.App.Name} → {state} ({message})");
        JobFinished?.Invoke(job);
    }
}
