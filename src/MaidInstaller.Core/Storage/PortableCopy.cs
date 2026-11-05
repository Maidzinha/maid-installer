namespace MaidInstaller.Core.Storage;

/// <summary>Um arquivo a copiar para a cópia portátil.</summary>
public sealed record CopyItem(string Source, string RelativePath, long Bytes, bool IsUserData);

/// <summary>O que será copiado (mostrado na confirmação antes de qualquer gravação).</summary>
public sealed record PortableCopyPlan(
    string Destination,
    bool UpdatesExisting,
    IReadOnlyList<CopyItem> Files,
    bool IncludesModel,
    bool SourceIsSelfContained)
{
    public long TotalBytes => Files.Sum(f => f.Bytes);
    public int UserDataFiles => Files.Count(f => f.IsUserData);
}

public sealed record CopyProgress(long BytesCopied, long TotalBytes, string CurrentFile)
{
    public double Fraction => TotalBytes <= 0 ? 1 : Math.Clamp((double)BytesCopied / TotalBytes, 0, 1);
}

public sealed class PortableCopyException(string message) : Exception(message);

/// <summary>
/// Monta uma cópia portátil do Maid Installer numa pasta escolhida (pendrive / Ventoy):
/// os arquivos do aplicativo, o arquivo <see cref="AppPaths.PortableMarkerFile"/> e os dados que
/// fazem sentido levar (configurações, perfis e listas). Logs, cache e histórico deste PC não vão.
/// O modelo de IA só é copiado se o usuário pedir.
/// </summary>
public static class PortableCopy
{
    public const string FolderName = AppPaths.AppFolderName;

    /// <summary>Folga de espaço além do tamanho da cópia.</summary>
    public const long SpaceMargin = 32L * 1024 * 1024;

    /// <summary>Pastas de dados que nunca são copiadas como "arquivos do aplicativo".</summary>
    private static readonly string[] DataFolders = ["Data", "Config", "Profiles", "Lists", "Models", "Logs", "Cache"];

    /// <summary>
    /// Uma publicação "self-contained" leva o .NET junto e abre em qualquer Windows 10/11, mesmo recém-formatado.
    /// </summary>
    public static bool IsSelfContained(string appDirectory) =>
        File.Exists(Path.Combine(appDirectory, "hostfxr.dll")) && File.Exists(Path.Combine(appDirectory, "coreclr.dll"));

    /// <summary>Pasta final: a escolhida, se já for uma cópia portátil; senão, uma subpasta "MaidInstaller" dentro dela.</summary>
    public static string ResolveDestination(string chosenFolder)
    {
        var full = Path.GetFullPath(chosenFolder);
        return File.Exists(Path.Combine(full, AppPaths.PortableMarkerFile)) ? full : Path.Combine(full, FolderName);
    }

    public static PortableCopyPlan Plan(AppPaths paths, string chosenFolder, bool includeModel, string? modelFile = null)
    {
        var dest = ResolveDestination(chosenFolder);
        Validate(paths, dest);
        var updates = File.Exists(Path.Combine(dest, AppPaths.PortableMarkerFile));

        var files = new List<CopyItem>();
        var appDir = paths.AppDirectory;
        foreach (var file in Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(appDir, file);
            var top = rel.Split(Path.DirectorySeparatorChar, 2)[0];
            if (rel.Equals(AppPaths.PortableMarkerFile, StringComparison.OrdinalIgnoreCase)) continue;
            if (rel.Contains(Path.DirectorySeparatorChar) && DataFolders.Contains(top, StringComparer.OrdinalIgnoreCase)) continue;
            if (rel.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue;
            files.Add(new CopyItem(file, rel, new FileInfo(file).Length, IsUserData: false));
        }

        // Dados do usuário: só os que levam algo útil para outro PC. Numa atualização, os da cópia são mantidos.
        void AddData(string sourceDir, string folder, string pattern)
        {
            if (!Directory.Exists(sourceDir)) return;
            foreach (var file in Directory.EnumerateFiles(sourceDir, pattern, SearchOption.TopDirectoryOnly))
            {
                var rel = Path.Combine(folder, Path.GetFileName(file));
                if (updates && File.Exists(Path.Combine(dest, rel))) continue;
                files.Add(new CopyItem(file, rel, new FileInfo(file).Length, IsUserData: true));
            }
        }
        AddData(paths.ConfigDir, "Config", "settings.json");
        AddData(paths.ProfilesDir, "Profiles", "*.json");
        AddData(paths.ListsDir, "Lists", "*.json");

        var withModel = false;
        if (includeModel && modelFile is not null && File.Exists(modelFile))
        {
            var rel = Path.Combine("Models", Path.GetFileName(modelFile));
            if (!File.Exists(Path.Combine(dest, rel)))
                files.Add(new CopyItem(modelFile, rel, new FileInfo(modelFile).Length, IsUserData: true));
            withModel = true;
        }

        return new PortableCopyPlan(dest, updates, files, withModel, IsSelfContained(appDir));
    }

    /// <summary>Recusa destinos que iriam copiar o app para dentro dele mesmo ou misturar com outra pasta.</summary>
    public static void Validate(AppPaths paths, string dest)
    {
        if (IsSameOrInside(dest, paths.AppDirectory) || IsSameOrInside(paths.AppDirectory, dest))
            throw new PortableCopyException("Escolha uma pasta fora da pasta do próprio Maid Installer.");
        if (IsSameOrInside(dest, paths.DataRoot))
            throw new PortableCopyException("Escolha uma pasta fora da pasta de dados do Maid Installer.");
        if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any()
            && !File.Exists(Path.Combine(dest, AppPaths.PortableMarkerFile)))
            throw new PortableCopyException($"A pasta {dest} já existe e não é uma cópia portátil do Maid Installer. Escolha outra pasta.");
    }

    /// <summary>Bytes que realmente faltam gravar (arquivos substituídos liberam o espaço deles).</summary>
    public static long RequiredBytes(PortableCopyPlan plan) =>
        plan.Files.Sum(f =>
        {
            var target = Path.Combine(plan.Destination, f.RelativePath);
            return File.Exists(target) ? Math.Max(0, f.Bytes - new FileInfo(target).Length) : f.Bytes;
        }) + SpaceMargin;

    public static async Task ExecuteAsync(PortableCopyPlan plan, IProgress<CopyProgress>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(plan.Destination);
        var total = plan.TotalBytes;
        long copied = 0;
        var buffer = new byte[1 << 20];
        foreach (var item in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(plan.Destination, item.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var tmp = target + ".tmp";
            await using (var source = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true))
            await using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    copied += read;
                    progress?.Report(new CopyProgress(copied, total, item.RelativePath));
                }
                output.Flush(flushToDisk: true);
            }
            File.Move(tmp, target, overwrite: true);
        }

        foreach (var dir in new[] { "Data", "Config", "Profiles", "Lists", "Models", "Logs", "Cache" })
            Directory.CreateDirectory(Path.Combine(plan.Destination, dir));
        await File.WriteAllTextAsync(Path.Combine(plan.Destination, AppPaths.PortableMarkerFile),
            "Este arquivo ativa o modo portátil do Maid Installer: os dados ficam nesta pasta.\r\n", cancellationToken).ConfigureAwait(false);
        progress?.Report(new CopyProgress(total, total, ""));
    }

    private static bool IsSameOrInside(string path, string folder)
    {
        var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
        var f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }
}
