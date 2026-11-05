using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Installation;
using MaidInstaller.Core.Logging;

namespace MaidInstaller.Core.WinGet;

public sealed record WinGetRunResult(int ExitCode, string Output);

/// <summary>Pacote instalado, segundo o WinGet.</summary>
public sealed record InstalledPackage(string Id, string Source, string? Version);

/// <summary>
/// Executa o WinGet com argumentos fixos (ArgumentList, sem shell), nunca com texto vindo do catálogo
/// sem validação. Nesta etapa só faz consultas (export/show); instalação chega na Etapa 4.
/// </summary>
public sealed partial class WinGetClient(IAppLogger logger) : IWinGetProcessRunner
{
    /// <summary>
    /// Executa o WinGet entregando cada linha da saída assim que ela chega (separa por \r e \n,
    /// pois barras de progresso são redesenhadas com \r). O cancelamento encerra o processo do WinGet.
    /// </summary>
    public async Task<int> RunStreamingAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("winget")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException(L.T("Não foi possível iniciar o winget."));
        logger.Info($"winget {string.Join(' ', psi.ArgumentList)} (pid {process.Id})");

        var stdout = PumpAsync(process.StandardOutput, onLine);
        var stderr = PumpAsync(process.StandardError, onLine);
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Um instalador que já rodava como administrador não pode ser encerrado por nós.
                logger.Warn($"Não foi possível encerrar todos os processos da instalação: {ex.Message}");
            }
            throw;
        }
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        logger.Info($"winget {args.FirstOrDefault()} terminou com código {WinGetExitCodesHex(process.ExitCode)}");
        return process.ExitCode;
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> onLine)
    {
        var buffer = new char[1024];
        var line = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var c = buffer[i];
                if (c is '\r' or '\n')
                {
                    if (line.Length > 0) onLine(line.ToString());
                    line.Clear();
                }
                else
                {
                    line.Append(c);
                }
            }
        }
        if (line.Length > 0) onLine(line.ToString());
    }

    /// <summary>Explicação oficial de um código de erro, via "winget error --input".</summary>
    public async Task<string?> DescribeErrorAsync(int exitCode, CancellationToken ct)
    {
        try
        {
            var r = await RunAsync(["error", "--input", WinGetExitCodesHex(exitCode)], ct, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            var lines = r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var description = lines.Skip(1).FirstOrDefault(l => !l.Contains("unknown error", StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(description) ? null : description;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            return null;
        }
    }

    private static string WinGetExitCodesHex(int code) => $"0x{unchecked((uint)code):X8}";

    private static readonly string[] CommonArgs = ["--accept-source-agreements", "--disable-interactivity"];

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await RunAsync(["--version"], ct, TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            return r.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Versão do WinGet (ex.: "v1.12.350"), ou null se ele não estiver disponível.</summary>
    public async Task<string?> GetVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await RunAsync(["--version"], ct, TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            return r.ExitCode == 0 ? r.Output.Trim().Split('\n')[0].Trim() : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Lista o que está instalado usando "winget export" (JSON estável, sem depender da tabela de texto).</summary>
    public async Task<IReadOnlyList<InstalledPackage>> GetInstalledAsync(CancellationToken ct = default)
    {
        // O WinGet é um app empacotado (MSIX) e não enxerga pastas criadas por nós em %LocalAppData%
        // (virtualização de arquivos). Por isso exporta para %TEMP%; o arquivo é apagado logo depois.
        var file = Path.Combine(Path.GetTempPath(), $"maid-winget-{Guid.NewGuid():N}.json");
        try
        {
            var r = await RunAsync(["export", "-o", file, "--include-versions", .. CommonArgs], ct, TimeSpan.FromMinutes(3)).ConfigureAwait(false);
            if (!File.Exists(file))
                throw new InvalidOperationException($"winget export falhou (código {r.ExitCode}).");

            var list = ParseExport(await File.ReadAllTextAsync(file, ct).ConfigureAwait(false));
            logger.Info($"WinGet: {list.Count} pacotes instalados reconhecidos.");
            return list;
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    internal static List<InstalledPackage> ParseExport(string json)
    {
        var result = new List<InstalledPackage>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Sources", out var sources)) return result;
        foreach (var source in sources.EnumerateArray())
        {
            var sourceName = source.TryGetProperty("SourceDetails", out var d) && d.TryGetProperty("Name", out var n)
                ? n.GetString() ?? "" : "";
            if (!source.TryGetProperty("Packages", out var packages)) continue;
            foreach (var p in packages.EnumerateArray())
            {
                var id = p.TryGetProperty("PackageIdentifier", out var i) ? i.GetString() : null;
                var ver = p.TryGetProperty("Version", out var v) ? v.GetString() : null;
                if (!string.IsNullOrEmpty(id)) result.Add(new InstalledPackage(id, sourceName, ver));
            }
        }
        return result;
    }

    /// <summary>Versão disponível agora na fonte (consulta ao vivo com "winget show").</summary>
    public async Task<string?> GetAvailableVersionAsync(CatalogApp app, CancellationToken ct = default)
    {
        if (!IsValidPackage(app)) return null;
        var r = await RunAsync(["show", "--id", app.WinGetId!, "--exact", "--source", app.WinGetSource!, .. CommonArgs],
            ct, TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        return r.ExitCode == 0 ? ParseShowVersion(r.Output) : null;
    }

    internal static string? ParseShowVersion(string output)
    {
        var m = ShowVersionRegex().Match(output);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    /// <summary>Defesa extra: só aceita pacotes que passam no validador do catálogo.</summary>
    /// <summary>
    /// Programas instalados com atualização disponível (<c>winget upgrade</c>, só consulta — nada é atualizado aqui).
    /// IDs cortados pelo WinGet são completados com a lista do <c>winget export</c>; o que não der para confirmar é descartado.
    /// </summary>
    public async Task<IReadOnlyList<UpgradeCandidate>> GetUpgradesAsync(CancellationToken ct = default)
    {
        var r = await RunAsync(["upgrade", .. CommonArgs], ct, TimeSpan.FromMinutes(3)).ConfigureAwait(false);
        var list = WinGetUpgradeParser.Parse(r.Output).ToList();
        if (list.Count == 0 && r.ExitCode != 0 && r.ExitCode != WinGetExitCodes.UpdateNotApplicable && !r.Output.Contains("----"))
            throw new InvalidOperationException($"winget upgrade falhou (código {WinGetExitCodes.Hex(r.ExitCode)}).");

        if (list.Any(c => c.IdTruncated))
        {
            var installed = await GetInstalledAsync(ct).ConfigureAwait(false);
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (!list[i].IdTruncated) continue;
                var matches = installed.Where(p => p.Id.StartsWith(list[i].Id, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count == 1) list[i] = list[i] with { Id = matches[0].Id, IdTruncated = false };
                else
                {
                    logger.Warn($"Atualização ignorada: ID cortado pelo WinGet e não confirmado ({list[i].Name}, {list[i].Id}…).");
                    list.RemoveAt(i);
                }
            }
        }

        // Só o que dá para atualizar com segurança: fonte conhecida e ID com formato válido.
        return [.. list.Where(c => IsValidPackage(new CatalogApp { WinGetId = c.Id, WinGetSource = c.Source }))];
    }

    public static bool IsValidPackage(CatalogApp app) =>
        app.WinGetId is not null && app.WinGetSource switch
        {
            "winget" => CatalogValidator.WinGetIdRegex().IsMatch(app.WinGetId),
            "msstore" => CatalogValidator.StoreIdRegex().IsMatch(app.WinGetId),
            _ => false,
        };

    public async Task<WinGetRunResult> RunAsync(IEnumerable<string> args, CancellationToken ct, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo("winget")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException(L.T("Não foi possível iniciar o winget."));
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var stdout = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        var output = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
        logger.Info($"winget {string.Join(' ', psi.ArgumentList)} → código {process.ExitCode}");
        return new WinGetRunResult(process.ExitCode, output);
    }

    [GeneratedRegex(@"^\s*(?:Version|Versão)\s*:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex ShowVersionRegex();
}
