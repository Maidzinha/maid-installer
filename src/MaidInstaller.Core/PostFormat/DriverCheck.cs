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
                    var name = mo["Name"]?.ToString() is { Length: > 0 } n ? n : "Dispositivo desconhecido";
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
                    video.Add(new VideoDriver(mo["Name"]?.ToString() ?? "Vídeo", mo["DriverVersion"]?.ToString(), date));
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
        1 => "configurado incorretamente",
        3 => "driver corrompido ou com pouca memória",
        10 => "o dispositivo não pode ser iniciado",
        12 => "conflito de recursos",
        14 => "precisa reiniciar o PC",
        18 => "reinstale o driver",
        19 => "registro do driver danificado",
        21 => "o Windows está removendo o dispositivo",
        22 => "desativado por você",
        24 => "dispositivo ausente ou não conectado",
        28 => "nenhum driver instalado",
        29 => "desativado pelo firmware (BIOS)",
        31 => "o driver não funcionou",
        32 => "serviço do driver desativado",
        39 => "driver ausente ou corrompido",
        43 => "o dispositivo relatou um erro",
        45 => "não está conectado agora",
        48 => "driver bloqueado por incompatibilidade",
        52 => "assinatura digital do driver não verificada",
        _ => $"código {code} do Gerenciador de Dispositivos",
    };
}
