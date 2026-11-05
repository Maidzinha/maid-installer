using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace MaidInstaller.Core.PostFormat;

/// <summary>Executa ferramentas fixas do Windows (ex.: powercfg). Nunca recebe comandos vindos de arquivos ou do catálogo.</summary>
public static class SystemTool
{
    static SystemTool() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public sealed record Result(int ExitCode, string Output);

    public static async Task<Result> RunAsync(string exe, IEnumerable<string> args, TimeSpan timeout, CancellationToken ct = default)
    {
        var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, exe))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = oem,
            StandardErrorEncoding = oem,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Não foi possível iniciar {exe}.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = process.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        return new Result(process.ExitCode, (await stdout) + (await stderr));
    }

    /// <summary>Abre uma ferramenta ou página de Configurações do Windows (só abre; quem altera algo é o usuário).</summary>
    public static void Open(string target, string? arguments = null)
    {
        var psi = new ProcessStartInfo(target) { UseShellExecute = true };
        if (arguments is not null) psi.Arguments = arguments;
        Process.Start(psi)?.Dispose();
    }
}
