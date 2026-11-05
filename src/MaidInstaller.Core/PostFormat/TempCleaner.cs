namespace MaidInstaller.Core.PostFormat;

public sealed record TempScan(int TotalFiles, long TotalBytes, int EligibleFiles, long EligibleBytes);
public sealed record TempCleanResult(int DeletedFiles, long FreedBytes, int SkippedFiles, int DeletedFolders);

/// <summary>
/// Limpa a pasta temporária do usuário (%TEMP%). Regras de segurança:
/// só arquivos com mais de <c>minAge</c> sem uso, nunca segue atalhos de pasta (junções/links),
/// pula arquivos em uso e não toca em C:\Windows\Temp nem em outras pastas do sistema.
/// </summary>
public static class TempCleaner
{
    public static readonly TimeSpan DefaultMinAge = TimeSpan.FromHours(24);

    public static string UserTempDirectory => Path.GetFullPath(Path.GetTempPath());

    public static TempScan Scan(string root, TimeSpan minAge, DateTime? now = null)
    {
        var limit = (now ?? DateTime.Now) - minAge;
        int total = 0, eligible = 0;
        long totalBytes = 0, eligibleBytes = 0;
        foreach (var file in EnumerateFiles(new DirectoryInfo(root)))
        {
            long len;
            try { len = file.Length; } catch (IOException) { continue; }
            total++;
            totalBytes += len;
            if (IsOld(file, limit)) { eligible++; eligibleBytes += len; }
        }
        return new TempScan(total, totalBytes, eligible, eligibleBytes);
    }

    public static TempCleanResult Clean(string root, TimeSpan minAge, DateTime? now = null, CancellationToken ct = default)
    {
        var rootInfo = new DirectoryInfo(root);
        var limit = (now ?? DateTime.Now) - minAge;
        int deleted = 0, skipped = 0, folders = 0;
        long freed = 0;

        foreach (var file in EnumerateFiles(rootInfo))
        {
            ct.ThrowIfCancellationRequested();
            if (!IsOld(file, limit)) continue;
            try
            {
                var len = file.Length;
                if (file.IsReadOnly) { skipped++; continue; } // somente leitura: alguém quis proteger
                file.Delete();
                deleted++;
                freed += len;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++; // em uso ou sem permissão: fica onde está
            }
        }

        // Pastas vazias e antigas (de baixo para cima). A pasta raiz nunca é apagada.
        foreach (var dir in EnumerateDirectories(rootInfo).OrderByDescending(d => d.FullName.Length))
        {
            try
            {
                if (dir.LastWriteTime < limit && !dir.EnumerateFileSystemInfos().Any())
                {
                    dir.Delete();
                    folders++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return new TempCleanResult(deleted, freed, skipped, folders);
    }

    private static bool IsOld(FileInfo f, DateTime limit)
    {
        try { return f.LastWriteTime < limit && f.LastAccessTime < limit && f.CreationTime < limit; }
        catch (IOException) { return false; }
    }

    private static IEnumerable<FileInfo> EnumerateFiles(DirectoryInfo root)
    {
        foreach (var dir in EnumerateDirectories(root).Prepend(root))
        {
            FileInfo[] files;
            try { files = dir.GetFiles(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var f in files)
                if ((f.Attributes & FileAttributes.ReparsePoint) == 0) yield return f;
        }
    }

    /// <summary>Todas as subpastas reais (sem entrar em junções/links simbólicos).</summary>
    private static IEnumerable<DirectoryInfo> EnumerateDirectories(DirectoryInfo root)
    {
        var stack = new Stack<DirectoryInfo>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            DirectoryInfo[] subs;
            try { subs = current.GetDirectories(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var sub in subs)
            {
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                yield return sub;
                stack.Push(sub);
            }
        }
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} bytes",
    };
}
