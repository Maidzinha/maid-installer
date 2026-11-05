using MaidInstaller.Core.Logging;
using MaidInstaller.Core.Storage;

namespace MaidInstaller.Core.Installation;

public sealed class HistoryEntry
{
    public DateTime Date { get; set; }
    public string Operation { get; set; } = "install"; // "install" | "upgrade"
    public string Name { get; set; } = "";
    public string? WinGetId { get; set; }
    public string? FromVersion { get; set; }
    public string? ToVersion { get; set; }
    public string Result { get; set; } = ""; // "completed" | "already" | "failed" | "cancelled"
    public string? Message { get; set; }
    public bool RebootRequired { get; set; }
}

/// <summary>
/// Histórico de instalações e atualizações (Data/historico.json). Guarda só o que realmente foi executado,
/// do mais novo para o mais antigo, limitado a <see cref="MaxEntries"/> itens.
/// </summary>
public sealed class InstallHistory(string path, IAppLogger logger)
{
    public const int MaxEntries = 500;
    private readonly object _lock = new();
    private List<HistoryEntry>? _entries;

    public event Action<HistoryEntry>? Added;

    public IReadOnlyList<HistoryEntry> Entries
    {
        get { lock (_lock) return [.. Load()]; }
    }

    private List<HistoryEntry> Load()
    {
        if (_entries is not null) return _entries;
        try { _entries = JsonStore.Read<List<HistoryEntry>>(path) ?? []; }
        catch (Exception ex)
        {
            logger.Error($"Histórico ilegível, começando um novo: {path}", ex);
            try { if (File.Exists(path)) File.Move(path, path + $".corrompido-{DateTime.Now:yyyyMMddHHmmss}"); } catch (IOException) { }
            _entries = [];
        }
        return _entries;
    }

    public void Add(HistoryEntry entry)
    {
        lock (_lock)
        {
            var list = Load();
            list.Insert(0, entry);
            if (list.Count > MaxEntries) list.RemoveRange(MaxEntries, list.Count - MaxEntries);
            try { JsonStore.WriteAtomic(path, list); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logger.Error("Não foi possível salvar o histórico.", ex); }
        }
        Added?.Invoke(entry);
    }

    /// <summary>Registra um job que terminou depois de ter começado de verdade (pedidos cancelados na fila não entram).</summary>
    public void Record(InstallJob job)
    {
        if (job.StartedAt is null || !job.IsFinished) return;
        Add(new HistoryEntry
        {
            Date = job.FinishedAt ?? DateTime.Now,
            Operation = job.Operation == InstallOperation.Upgrade ? "upgrade" : "install",
            Name = job.App.Name,
            WinGetId = job.App.WinGetId,
            FromVersion = job.FromVersion,
            ToVersion = job.ToVersion,
            Result = job.State switch
            {
                InstallJobState.Completed => "completed",
                InstallJobState.AlreadyInstalled => "already",
                InstallJobState.Cancelled => "cancelled",
                _ => "failed",
            },
            Message = job.Message,
            RebootRequired = job.RebootRequired,
        });
    }
}
