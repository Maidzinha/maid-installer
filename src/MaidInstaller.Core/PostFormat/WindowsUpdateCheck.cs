using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;

namespace MaidInstaller.Core.PostFormat;

public sealed record InstalledUpdate(string Id, string? Kind, DateTime? InstalledOn);
public sealed record PendingUpdate(string Title, bool IsDownloaded);

/// <summary>
/// Windows Update, somente leitura: última atualização instalada (WMI) e busca de atualizações pendentes
/// pela API oficial do Windows Update (a mesma usada pelas Configurações). Instalar fica com o próprio Windows Update.
/// </summary>
public static class WindowsUpdateCheck
{
    public static IReadOnlyList<InstalledUpdate> GetInstalled()
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\CIMV2", "SELECT HotFixID, Description, InstalledOn FROM Win32_QuickFixEngineering");
            var list = new List<InstalledUpdate>();
            foreach (var obj in s.Get())
            {
                using var mo = (ManagementObject)obj;
                list.Add(new InstalledUpdate(mo["HotFixID"]?.ToString() ?? "?", mo["Description"]?.ToString(), ParseDate(mo["InstalledOn"]?.ToString())));
            }
            return [.. list.OrderByDescending(u => u.InstalledOn ?? DateTime.MinValue)];
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>O WMI devolve a data como texto, geralmente "M/d/yyyy"; às vezes em hexadecimal (FILETIME).</summary>
    public static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();
        string[] formats = ["M/d/yyyy", "MM/dd/yyyy", "yyyyMMdd", "M/d/yyyy H:mm:ss"];
        if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        if (raw.Length == 16 && long.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var ft))
            try { return DateTime.FromFileTime(ft); } catch (ArgumentOutOfRangeException) { }
        return null;
    }

    /// <summary>Procura atualizações de software ainda não instaladas. Pode levar alguns minutos. Lança <see cref="COMException"/> se o serviço não responder.</summary>
    public static Task<IReadOnlyList<PendingUpdate>> SearchPendingAsync() => Task.Run<IReadOnlyList<PendingUpdate>>(() =>
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session") ?? throw new COMException("Windows Update indisponível.");
        dynamic session = Activator.CreateInstance(type)!;
        try
        {
            session.ClientApplicationID = "Maid Installer";
            dynamic searcher = session.CreateUpdateSearcher();
            dynamic result = searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Software'");
            var list = new List<PendingUpdate>();
            int count = result.Updates.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic u = result.Updates.Item(i);
                list.Add(new PendingUpdate((string)u.Title, (bool)u.IsDownloaded));
            }
            return list;
        }
        finally
        {
            Marshal.FinalReleaseComObject(session);
        }
    });
}
