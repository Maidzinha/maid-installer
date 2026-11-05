using System.Management;
using System.Runtime.InteropServices;

namespace MaidInstaller.Core.PostFormat;

public sealed record ProblemDevice(string Name, int Code, string Explanation, bool IsDisabledByUser);
public sealed record VideoDriver(string Name, string? Version, DateTime? Date);
public sealed record DriverReport(IReadOnlyList<ProblemDevice> Problems, IReadOnlyList<VideoDriver> Video, bool Supported);

/// <summary>Verifica (somente leitura, via WMI) dispositivos com problema e a versão do driver de vídeo.</summary>
public static class DriverCheck
{
    public static DriverReport Run()
    {
        try
        {
            var problems = new List<ProblemDevice>();
            using (var s = new ManagementObjectSearcher(@"root\CIMV2",
                       "SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
            {
                foreach (var obj in s.Get())
                {
                    using var mo = (ManagementObject)obj;
                    var code = Convert.ToInt32(mo["ConfigManagerErrorCode"] ?? 0);
                    var name = mo["Name"]?.ToString() is { Length: > 0 } n ? n : L.T("Dispositivo desconhecido");
                    problems.Add(new ProblemDevice(name, code, Explain(code), code == 22));
                }
            }

            var video = new List<VideoDriver>();
            using (var s = new ManagementObjectSearcher(@"root\CIMV2", "SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController"))
            {
                foreach (var obj in s.Get())
                {
                    using var mo = (ManagementObject)obj;
                    DateTime? date = null;
                    if (mo["DriverDate"]?.ToString() is { Length: > 0 } raw)
                        try { date = ManagementDateTimeConverter.ToDateTime(raw); } catch (ArgumentOutOfRangeException) { }
                    video.Add(new VideoDriver(mo["Name"]?.ToString() ?? L.T("Vídeo"), mo["DriverVersion"]?.ToString(), date));
                }
            }
            return new DriverReport(problems, video, true);
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            return new DriverReport([], [], false);
        }
    }

    /// <summary>Códigos do Gerenciador de Dispositivos mais comuns, em português.</summary>
    public static string Explain(int code) => code switch
    {
        1 => L.T("configurado incorretamente"),
        3 => L.T("driver corrompido ou com pouca memória"),
        10 => L.T("o dispositivo não pode ser iniciado"),
        12 => L.T("conflito de recursos"),
        14 => L.T("precisa reiniciar o PC"),
        18 => L.T("reinstale o driver"),
        19 => L.T("registro do driver danificado"),
        21 => L.T("o Windows está removendo o dispositivo"),
        22 => L.T("desativado por você"),
        24 => L.T("dispositivo ausente ou não conectado"),
        28 => L.T("nenhum driver instalado"),
        29 => L.T("desativado pelo firmware (BIOS)"),
        31 => L.T("o driver não funcionou"),
        32 => L.T("serviço do driver desativado"),
        39 => L.T("driver ausente ou corrompido"),
        43 => L.T("o dispositivo relatou um erro"),
        45 => L.T("não está conectado agora"),
        48 => L.T("driver bloqueado por incompatibilidade"),
        52 => L.T("assinatura digital do driver não verificada"),
        _ => L.T($"código {code} do Gerenciador de Dispositivos"),
    };
}
