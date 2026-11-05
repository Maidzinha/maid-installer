using System.Text.RegularExpressions;

namespace MaidInstaller.Core.PostFormat;

public sealed record PowerPlan(Guid Id, string Name, bool IsActive);

/// <summary>Planos de energia via <c>powercfg</c>. Só troca para um plano que já existe no PC (não cria nem apaga planos).</summary>
public static partial class PowerPlans
{
    /// <summary>Interpreta a saída de <c>powercfg /list</c> (funciona em qualquer idioma do Windows).</summary>
    public static IReadOnlyList<PowerPlan> Parse(string output)
    {
        var plans = new List<PowerPlan>();
        foreach (var line in output.Split('\n'))
        {
            var m = PlanLine().Match(line);
            if (!m.Success || !Guid.TryParse(m.Groups["guid"].Value, out var id)) continue;
            plans.Add(new PowerPlan(id, m.Groups["name"].Value.Trim(), m.Groups["active"].Success));
        }
        return plans;
    }

    public static async Task<IReadOnlyList<PowerPlan>> GetAsync(CancellationToken ct = default)
    {
        var r = await SystemTool.RunAsync("powercfg.exe", ["/list"], TimeSpan.FromSeconds(15), ct);
        return r.ExitCode == 0 ? Parse(r.Output) : [];
    }

    /// <summary>Ativa um plano existente. Retorna false se o plano não está na lista do PC ou o powercfg falhou.</summary>
    public static async Task<bool> SetActiveAsync(Guid id, CancellationToken ct = default)
    {
        var plans = await GetAsync(ct);
        if (plans.All(p => p.Id != id)) return false;
        var r = await SystemTool.RunAsync("powercfg.exe", ["/setactive", id.ToString("D")], TimeSpan.FromSeconds(15), ct);
        return r.ExitCode == 0;
    }

    [GeneratedRegex(@"(?<guid>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s+\((?<name>[^)]*)\)\s*(?<active>\*)?")]
    private static partial Regex PlanLine();
}
