using System.Globalization;
using System.Text.RegularExpressions;

namespace MaidInstaller.Core.Installation;

public sealed record OutputSignal(InstallJobState? Stage, double? Percent, string? Message);

/// <summary>
/// Interpreta as linhas que o WinGet escreve. Com a saída redirecionada o WinGet normalmente
/// não envia barra de progresso, só as etapas; a porcentagem só é usada quando ele realmente a informa.
/// </summary>
public static partial class WinGetOutputParser
{
    public static OutputSignal? Parse(string rawLine)
    {
        var line = Clean(rawLine);
        if (line.Length == 0) return null;

        var sizes = SizeProgressRegex().Match(line);
        if (sizes.Success)
        {
            var done = ToBytes(sizes.Groups[1].Value, sizes.Groups[2].Value);
            var total = ToBytes(sizes.Groups[3].Value, sizes.Groups[4].Value);
            if (total > 0) return new OutputSignal(InstallJobState.Downloading, Math.Clamp(done * 100 / total, 0, 100), null);
        }

        var percent = PercentRegex().Match(line);
        if (percent.Success && double.TryParse(percent.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            return new OutputSignal(null, Math.Clamp(p, 0, 100), null);

        if (StartsWithAny(line, "Downloading", "Baixando"))
            return new OutputSignal(InstallJobState.Downloading, null, "Baixando o instalador oficial…");
        if (StartsWithAny(line, "Successfully verified installer hash", "Hash do instalador verificado"))
            return new OutputSignal(InstallJobState.Installing, null, "Arquivo verificado. Preparando a instalação…");
        if (StartsWithAny(line, "Starting package install", "Iniciando a instalação"))
            return new OutputSignal(InstallJobState.Installing, null, "Instalando… (o Windows pode pedir permissão de administrador)");
        if (StartsWithAny(line, "Successfully installed", "Instalado com êxito", "Instalado com sucesso"))
            return new OutputSignal(null, null, "Instalado com sucesso.");
        if (StartsWithAny(line, "Found ", "Encontrado "))
            return new OutputSignal(null, null, "Pacote encontrado na fonte oficial.");

        return new OutputSignal(null, null, null);
    }

    /// <summary>Remove códigos de terminal, spinner e espaços.</summary>
    public static string Clean(string line)
    {
        var s = AnsiRegex().Replace(line, "");
        s = s.Trim().TrimStart('-', '\\', '|', '/').Trim();
        return s;
    }

    private static bool StartsWithAny(string line, params string[] prefixes) =>
        prefixes.Any(p => line.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static double ToBytes(string number, string unit)
    {
        var n = double.Parse(number.Replace(',', '.'), CultureInfo.InvariantCulture);
        return unit.ToUpperInvariant() switch { "KB" => n * 1024, "MB" => n * 1024 * 1024, "GB" => n * 1024 * 1024 * 1024, _ => n };
    }

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(KB|MB|GB)\s*/\s*(\d+(?:[.,]\d+)?)\s*(KB|MB|GB)", RegexOptions.IgnoreCase)]
    private static partial Regex SizeProgressRegex();

    [GeneratedRegex(@"(?:^|\s)(\d{1,3}(?:\.\d+)?)\s*%\s*$")]
    private static partial Regex PercentRegex();

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiRegex();
}
