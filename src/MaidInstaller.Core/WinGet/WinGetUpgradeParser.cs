namespace MaidInstaller.Core.WinGet;

/// <summary>Um programa instalado com versão mais nova disponível, segundo o WinGet.</summary>
/// <param name="IdTruncated">O WinGet cortou o ID com "…" (coluna estreita); precisa ser resolvido antes de usar.</param>
public sealed record UpgradeCandidate(string Name, string Id, string InstalledVersion, string AvailableVersion, string Source, bool IdTruncated = false);

/// <summary>
/// Lê a tabela do <c>winget upgrade</c>. O texto do cabeçalho muda com o idioma, então as colunas são encontradas
/// pela posição: a linha de traços marca o início da tabela e a linha acima dela dá onde cada coluna começa.
/// </summary>
public static class WinGetUpgradeParser
{
    public static IReadOnlyList<UpgradeCandidate> Parse(string output)
    {
        // Remove a animação de progresso: fica só o texto depois do último \r de cada linha.
        var lines = output.Replace("\r\n", "\n").Split('\n')
            .Select(l => { var i = l.LastIndexOf('\r'); return (i >= 0 ? l[(i + 1)..] : l).TrimEnd(); })
            .ToList();

        var result = new List<UpgradeCandidate>();
        var dash = lines.FindIndex(l => l.Length >= 20 && l.All(c => c == '-'));
        if (dash < 1) return result;

        var starts = ColumnStarts(lines[dash - 1]);
        if (starts.Count < 5) return result;

        for (var i = dash + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Length <= starts[4]) break; // fim da tabela (linha vazia ou rodapé "N upgrades available")
            var cols = Cut(line, starts);
            if (cols is null) break;
            var (name, id, installed, available, source) = (cols[0], cols[1], cols[2], cols[3], cols[4]);
            if (id.Length == 0 || available.Length == 0 || source.Length == 0) continue;
            var truncated = id.EndsWith('…');
            result.Add(new UpgradeCandidate(name.TrimEnd('…').Trim(), truncated ? id.TrimEnd('…') : id, installed, available, source, truncated));
        }
        return result;
    }

    /// <summary>Posições onde cada palavra do cabeçalho começa (os cabeçalhos do WinGet são uma palavra por coluna).</summary>
    private static List<int> ColumnStarts(string header)
    {
        var starts = new List<int>();
        for (var i = 0; i < header.Length; i++)
            if (header[i] != ' ' && (i == 0 || header[i - 1] == ' ')) starts.Add(i);
        return starts;
    }

    private static string[]? Cut(string line, List<int> starts)
    {
        // Só as 5 primeiras colunas: Nome, ID, Versão, Disponível, Origem.
        var cols = new string[5];
        for (var c = 0; c < 5; c++)
        {
            var from = starts[c];
            var to = c + 1 < starts.Count && c < 4 ? starts[c + 1] : line.Length;
            if (from > line.Length) return null;
            cols[c] = line[from..Math.Min(to, line.Length)].Trim();
        }
        // A coluna de origem não pode ter espaços (winget, msstore…). Se tiver, a linha não é da tabela.
        return cols[4].Contains(' ') ? null : cols;
    }
}
