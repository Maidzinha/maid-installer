using System.Security.Cryptography;
using MaidInstaller.Core.Logging;

namespace MaidInstaller.Core.Assistant;

/// <summary>Modelo de IA que o Maid Installer sabe baixar (endereço, tamanho e SHA-256 conferidos).</summary>
public sealed record ModelInfo(
    string DisplayName,
    string FileName,
    Uri Url,
    long SizeBytes,
    string Sha256,
    string License,
    string Publisher);

public static class KnownModels
{
    /// <summary>
    /// Qwen2.5 0.5B Instruct, quantização Q4_K_M, do repositório oficial da Qwen no Hugging Face.
    /// Tamanho e SHA-256 conferidos na API do Hugging Face (LFS oid) — Apache-2.0.
    /// </summary>
    public static readonly ModelInfo Qwen25_05B = new(
        DisplayName: "Qwen2.5 0.5B Instruct (Q4_K_M)",
        FileName: "qwen2.5-0.5b-instruct-q4_k_m.gguf",
        Url: new Uri("https://huggingface.co/Qwen/Qwen2.5-0.5B-Instruct-GGUF/resolve/main/qwen2.5-0.5b-instruct-q4_k_m.gguf"),
        SizeBytes: 491_400_032,
        Sha256: "74a4da8c9fdbcd15bd1f6d01d621410d31c6fc00986f5eb687824e7b93d7a9db",
        License: "Apache-2.0",
        Publisher: "Qwen (Alibaba Cloud)");

    public static ModelInfo Default => Qwen25_05B;
}

/// <summary>Encontra o arquivo do modelo na pasta Models/.</summary>
public static class ModelLocator
{
    /// <summary>
    /// O modelo oficial, se existir; senão, o primeiro *.gguf colocado pelo usuário
    /// (ordem alfabética). Arquivos .part (download incompleto) são ignorados.
    /// </summary>
    public static string? Find(string modelsDir, ModelInfo known)
    {
        if (!Directory.Exists(modelsDir)) return null;
        var official = Path.Combine(modelsDir, known.FileName);
        if (File.Exists(official)) return official;
        return Directory.EnumerateFiles(modelsDir, "*.gguf", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}

public sealed record DownloadProgress(long BytesReceived, long TotalBytes, bool Verifying = false)
{
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)BytesReceived / TotalBytes, 0, 1);
}

public sealed class ModelDownloadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Baixa o modelo SOMENTE do endereço fixo em <see cref="KnownModels"/>, gravando em um
/// arquivo .part. O arquivo só vira o modelo depois de conferir tamanho e SHA-256.
/// </summary>
public sealed class ModelDownloader(HttpClient http, IAppLogger logger, Func<string, long?>? freeSpaceProvider = null)
{
    /// <summary>Folga de espaço além do tamanho do modelo.</summary>
    public const long SpaceMargin = 64L * 1024 * 1024;

    public static string PartPath(string modelsDir, ModelInfo model) => Path.Combine(modelsDir, model.FileName + ".part");

    public async Task<string> DownloadAsync(ModelInfo model, string modelsDir, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        if (model.Url.Scheme != Uri.UriSchemeHttps || !model.Url.Host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase))
            throw new ModelDownloadException("Endereço do modelo não é o oficial; download recusado.");

        Directory.CreateDirectory(modelsDir);
        var free = (freeSpaceProvider ?? DefaultFreeSpace)(modelsDir);
        if (free is { } f && f < model.SizeBytes + SpaceMargin)
            throw new ModelDownloadException(
                $"Espaço insuficiente: o modelo precisa de {Format(model.SizeBytes + SpaceMargin)} livres e há {Format(f)}.");

        var final = Path.Combine(modelsDir, model.FileName);
        var part = PartPath(modelsDir, model);
        logger.Info($"Baixando modelo de IA: {model.DisplayName} ({model.Url})");
        try
        {
            using var response = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new ModelDownloadException($"O servidor respondeu {(int)response.StatusCode} ({response.ReasonPhrase}).");
            if (response.Content.Headers.ContentLength is { } length && length != model.SizeBytes)
                throw new ModelDownloadException($"O arquivo no servidor tem {length} bytes; o esperado era {model.SizeBytes}. Download recusado.");

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                var lastReport = 0L;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    received += read;
                    if (received > model.SizeBytes)
                        throw new ModelDownloadException("O servidor enviou mais dados que o esperado. Download recusado.");
                    sha.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    if (received - lastReport >= 1 << 20 || received == model.SizeBytes)
                    {
                        lastReport = received;
                        progress?.Report(new DownloadProgress(received, model.SizeBytes));
                    }
                }
            }

            if (received != model.SizeBytes)
                throw new ModelDownloadException($"O download terminou incompleto ({Format(received)} de {Format(model.SizeBytes)}). Tente de novo.");

            progress?.Report(new DownloadProgress(received, model.SizeBytes, Verifying: true));
            var hash = Convert.ToHexStringLower(sha.GetHashAndReset());
            if (!hash.Equals(model.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ModelDownloadException("O arquivo baixado não confere com o SHA-256 oficial. Por segurança, ele foi apagado.");

            File.Move(part, final, overwrite: true);
            logger.Info($"Modelo de IA baixado e conferido (SHA-256 {hash}): {final}");
            return final;
        }
        catch (OperationCanceledException)
        {
            TryDelete(part);
            logger.Info("Download do modelo de IA cancelado.");
            throw;
        }
        catch (ModelDownloadException ex)
        {
            TryDelete(part);
            logger.Warn($"Download do modelo recusado: {ex.Message}");
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            TryDelete(part);
            logger.Error("Falha no download do modelo de IA.", ex);
            throw new ModelDownloadException(ex is IOException
                ? "Não foi possível gravar o arquivo do modelo na pasta Models."
                : "Não foi possível baixar o modelo. Verifique a conexão com a internet.", ex);
        }
    }

    private static long? DefaultFreeSpace(string dir)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(dir));
            return root is null ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static string Format(long bytes) => PostFormat.TempCleaner.FormatBytes(bytes);
}
