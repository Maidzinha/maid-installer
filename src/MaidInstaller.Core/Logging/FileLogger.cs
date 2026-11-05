namespace MaidInstaller.Core.Logging;

public interface IAppLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

/// <summary>Log simples em arquivo diário (Logs/maid-AAAAMMDD.log). Thread-safe e nunca lança exceção.</summary>
public sealed class FileLogger(string logsDirectory) : IAppLogger
{
    private readonly Lock _lock = new();

    public string CurrentFile => Path.Combine(logsDirectory, $"maid-{DateTime.Now:yyyyMMdd}.log");

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message, Exception? exception = null) =>
        Write("ERRO", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(logsDirectory);
                File.AppendAllText(CurrentFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Falha de log nunca deve derrubar o aplicativo (ex.: pendrive removido).
        }
    }
}

public sealed class NullLogger : IAppLogger
{
    public static readonly NullLogger Instance = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}
