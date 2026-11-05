using System.ComponentModel;
using System.Runtime.CompilerServices;
using MaidInstaller.Core.Catalog;

namespace MaidInstaller.Core.Installation;

/// <summary>Os status exibidos na tela de acompanhamento.</summary>
public enum InstallJobState
{
    Waiting,
    Downloading,
    Installing,
    Completed,
    /// <summary>Já estava instalado: nada foi feito.</summary>
    AlreadyInstalled,
    Failed,
    Cancelled,
}

public enum InstallOperation { Install, Upgrade }

/// <summary>
/// Uma instalação (ou atualização) na fila. Alterado só pelo <see cref="InstallationService"/>.
/// Implementa INotifyPropertyChanged para a interface acompanhar em tempo real.
/// </summary>
public sealed class InstallJob(CatalogApp app, InstallOperation operation) : INotifyPropertyChanged
{
    private InstallJobState _state = InstallJobState.Waiting;
    private double? _progress;
    private string _message = L.T("Aguardando a vez na fila.");
    private int? _exitCode;
    private bool _rebootRequired;
    private string? _logFile;
    private DateTime? _startedAt, _finishedAt;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; } = Guid.NewGuid();
    public CatalogApp App { get; } = app;
    public InstallOperation Operation { get; } = operation;

    /// <summary>Versões informadas pelo WinGet quando a atualização foi pedida (para o histórico).</summary>
    public string? FromVersion { get; set; }
    public string? ToVersion { get; set; }

    internal CancellationTokenSource? Cancellation { get; set; }
    internal bool CancelRequested { get; set; }

    public InstallJobState State
    {
        get => _state;
        internal set
        {
            if (!Set(ref _state, value)) return;
            OnChanged(nameof(IsActive));
            OnChanged(nameof(IsFinished));
            OnChanged(nameof(CanRetry));
            OnChanged(nameof(CanCancel));
            OnChanged(nameof(ShowActivity));
        }
    }

    /// <summary>Porcentagem real informada pelo WinGet, ou null quando não há progresso real.</summary>
    public double? Progress
    {
        get => _progress;
        internal set { if (Set(ref _progress, value)) OnChanged(nameof(ShowActivity)); }
    }

    public string Message { get => _message; internal set => Set(ref _message, value); }
    public int? ExitCode { get => _exitCode; internal set => Set(ref _exitCode, value); }
    public bool RebootRequired { get => _rebootRequired; internal set => Set(ref _rebootRequired, value); }
    public string? LogFile { get => _logFile; internal set => Set(ref _logFile, value); }
    public DateTime? StartedAt { get => _startedAt; internal set => Set(ref _startedAt, value); }
    public DateTime? FinishedAt { get => _finishedAt; internal set => Set(ref _finishedAt, value); }

    public bool IsActive => State is InstallJobState.Downloading or InstallJobState.Installing;
    public bool IsFinished => State is InstallJobState.Completed or InstallJobState.AlreadyInstalled
        or InstallJobState.Failed or InstallJobState.Cancelled;
    public bool CanRetry => State is InstallJobState.Failed or InstallJobState.Cancelled;
    public bool CanCancel => State is InstallJobState.Waiting || IsActive;

    /// <summary>Ativo e sem porcentagem real: a interface mostra um indicador de atividade.</summary>
    public bool ShowActivity => IsActive && Progress is null;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnChanged(name);
        return true;
    }

    private void OnChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
