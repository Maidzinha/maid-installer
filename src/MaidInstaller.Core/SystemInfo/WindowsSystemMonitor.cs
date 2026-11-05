using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MaidInstaller.Core.Logging;
using Microsoft.Win32;

namespace MaidInstaller.Core.SystemInfo;

/// <summary>Informações que não mudam durante a execução.</summary>
public sealed record StaticSystemInfo(
    string WindowsVersion,
    string WindowsBuild,
    string CpuName,
    string? GpuName,
    ulong? GpuMemoryBytes,
    ulong TotalRamBytes);

/// <summary>Uma leitura de uso. Campos nulos = não disponível neste hardware/permissão.</summary>
public sealed record SystemUsage(
    double? CpuPercent,
    double RamPercent,
    ulong RamUsedBytes,
    double? GpuPercent,
    double? CpuTemperatureC,
    double? GpuTemperatureC);

public interface ISystemMonitor
{
    StaticSystemInfo GetStaticInfo();

    /// <summary>Lê o uso atual. Pode levar algumas centenas de ms (WMI): chamar fora da thread de UI.</summary>
    SystemUsage SampleUsage();
}

/// <summary>
/// Monitor real de hardware usando APIs do Windows:
/// registro (nomes), GetSystemTimes (CPU), GlobalMemoryStatusEx (RAM),
/// contadores "GPU Engine" via WMI (GPU), nvidia-smi (temperatura de GPUs NVIDIA)
/// e MSAcpi_ThermalZoneTemperature (temperatura ACPI, normalmente só como administrador).
/// </summary>
public sealed partial class WindowsSystemMonitor(IAppLogger logger) : ISystemMonitor
{
    private static readonly TimeSpan TemperatureInterval = TimeSpan.FromSeconds(5);

    private long _lastIdle, _lastKernel, _lastUser;
    private bool _hasCpuBaseline;
    private bool _gpuCountersSupported = true;
    private bool _acpiSupported = true;
    private readonly string? _nvidiaSmi = FindNvidiaSmi();
    private DateTime _lastTemperatureRead = DateTime.MinValue;
    private double? _cpuTemp, _gpuTemp;

    public StaticSystemInfo GetStaticInfo()
    {
        var (version, build) = ReadWindowsVersion();
        var (gpuName, gpuMem) = ReadPrimaryGpu();
        // Memória instalada (pentes); o Windows reserva uma parte, por isso o total utilizável é menor.
        var installed = GetPhysicallyInstalledSystemMemory(out var kb) && kb > 0 ? kb * 1024 : ReadMemory().total;
        return new StaticSystemInfo(version, build, ReadCpuName(), gpuName, gpuMem, installed);
    }

    public SystemUsage SampleUsage()
    {
        var (total, available) = ReadMemory();
        var used = total - available;
        var ramPercent = total == 0 ? 0 : used * 100.0 / total;

        if (DateTime.UtcNow - _lastTemperatureRead >= TemperatureInterval)
        {
            _lastTemperatureRead = DateTime.UtcNow;
            _gpuTemp = ReadNvidiaTemperature();
            _cpuTemp = ReadAcpiTemperature();
        }

        return new SystemUsage(ReadCpuPercent(), ramPercent, used, ReadGpuPercent(), _cpuTemp, _gpuTemp);
    }

    // ---------- Windows ----------

    internal static (string version, string build) ReadWindowsVersion()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = key?.GetValue("ProductName") as string ?? "Windows";
        var display = key?.GetValue("DisplayVersion") as string;
        var buildText = key?.GetValue("CurrentBuild") as string ?? Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);
        var ubr = key?.GetValue("UBR") is int u ? u : (int?)null;

        // O registro continua dizendo "Windows 10" no Windows 11; a build 22000+ identifica o 11.
        if (int.TryParse(buildText, out var build) && build >= 22000)
            product = product.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);

        var fullBuild = ubr is null ? buildText : $"{buildText}.{ubr}";
        return (display is null ? product : $"{product} {display}", fullBuild);
    }

    // ---------- CPU ----------

    internal static string ReadCpuName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        var raw = key?.GetValue("ProcessorNameString") as string;
        return string.IsNullOrWhiteSpace(raw) ? L.T("Processador desconhecido") : CleanCpuName(raw);
    }

    internal static string CleanCpuName(string raw)
    {
        var s = CpuNoiseRegex().Replace(raw, " ");
        return SpacesRegex().Replace(s, " ").Trim();
    }

    private double? ReadCpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        double? result = null;
        if (_hasCpuBaseline)
        {
            var idleDelta = idle - _lastIdle;
            var totalDelta = (kernel - _lastKernel) + (user - _lastUser); // kernel inclui idle
            if (totalDelta > 0)
                result = Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100);
        }
        _lastIdle = idle; _lastKernel = kernel; _lastUser = user; _hasCpuBaseline = true;
        return result;
    }

    // ---------- Memória ----------

    private static (ulong total, ulong available) ReadMemory()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (status.ullTotalPhys, status.ullAvailPhys) : (0UL, 0UL);
    }

    // ---------- GPU ----------

    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    /// <summary>Escolhe a placa com mais memória dedicada (normalmente a dedicada, não a integrada).</summary>
    private (string? name, ulong? memory) ReadPrimaryGpu()
    {
        string? bestName = null;
        ulong? bestMem = null;
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (classKey is null) return (null, null);
            foreach (var sub in classKey.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                try
                {
                    using var dev = classKey.OpenSubKey(sub);
                    var name = dev?.GetValue("DriverDesc") as string;
                    if (string.IsNullOrWhiteSpace(name) || IsVirtualAdapter(name)) continue;
                    var mem = ReadRegistrySize(dev!.GetValue("HardwareInformation.qwMemorySize"))
                              ?? ReadRegistrySize(dev.GetValue("HardwareInformation.MemorySize"));
                    if (bestName is null || (mem ?? 0) > (bestMem ?? 0))
                    {
                        bestName = name.Trim();
                        bestMem = mem;
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"Não foi possível ler a placa de vídeo: {ex.Message}");
        }
        return (bestName, bestMem);
    }

    private static bool IsVirtualAdapter(string name) =>
        name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase);

    private static ulong? ReadRegistrySize(object? value) => value switch
    {
        long l when l > 0 => (ulong)l,
        int i when i != 0 => (uint)i,
        byte[] { Length: >= 8 } b => BitConverter.ToUInt64(b, 0),
        byte[] { Length: >= 4 } b => BitConverter.ToUInt32(b, 0),
        _ => null,
    };

    /// <summary>
    /// Mesmo critério do Gerenciador de Tarefas: soma o uso de todos os processos por motor
    /// (luid + phys + eng) e devolve o motor mais ocupado.
    /// </summary>
    private double? ReadGpuPercent()
    {
        if (!_gpuCountersSupported) return null;
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\CIMV2",
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            var perEngine = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var obj in searcher.Get())
            {
                using var mo = (ManagementObject)obj;
                if (mo["Name"] is not string name) continue;
                var match = GpuEngineRegex().Match(name);
                if (!match.Success) continue;
                var util = Convert.ToDouble(mo["UtilizationPercentage"] ?? 0, CultureInfo.InvariantCulture);
                perEngine[match.Value] = perEngine.GetValueOrDefault(match.Value) + util;
            }
            return perEngine.Count == 0 ? null : Math.Clamp(perEngine.Values.Max(), 0, 100);
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            _gpuCountersSupported = false;
            logger.Warn($"Contadores de GPU indisponíveis: {ex.Message}");
            return null;
        }
    }

    // ---------- Temperaturas ----------

    private static string? FindNvidiaSmi()
    {
        var path = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
        return File.Exists(path) ? path : null;
    }

    private double? ReadNvidiaTemperature()
    {
        if (_nvidiaSmi is null) return null;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(_nvidiaSmi, "--query-gpu=temperature.gpu --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } return null; }
            var first = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) ? t : null;
        }
        catch (Exception ex)
        {
            logger.Warn($"nvidia-smi falhou: {ex.Message}");
            return null;
        }
    }

    /// <summary>Zona térmica ACPI. A maioria das placas-mãe só expõe isso para administradores (ou não expõe).</summary>
    private double? ReadAcpiTemperature()
    {
        if (!_acpiSupported) return null;
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            double? max = null;
            foreach (var obj in searcher.Get())
            {
                using var mo = (ManagementObject)obj;
                var tenthsKelvin = Convert.ToDouble(mo["CurrentTemperature"] ?? 0, CultureInfo.InvariantCulture);
                if (tenthsKelvin <= 0) continue;
                var c = tenthsKelvin / 10.0 - 273.15;
                if (c is > 0 and < 150) max = Math.Max(max ?? c, c);
            }
            if (max is null) _acpiSupported = false;
            return max;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            _acpiSupported = false; // não tenta de novo a cada leitura
            return null;
        }
    }

    // ---------- Interop ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    [GeneratedRegex(@"\((R|TM)\)|\bCPU\b|\b\d+-Core Processor\b|\bProcessor\b|\bwith Radeon Graphics\b", RegexOptions.IgnoreCase)]
    private static partial Regex CpuNoiseRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();

    [GeneratedRegex(@"luid_0x[0-9a-f]+_0x[0-9a-f]+_phys_\d+_eng_\d+", RegexOptions.IgnoreCase)]
    private static partial Regex GpuEngineRegex();
}
