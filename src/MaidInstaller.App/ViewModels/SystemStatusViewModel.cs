using System.Globalization;
using System.Windows.Threading;
using MaidInstaller.App.Mvvm;
using MaidInstaller.Core.Logging;
using MaidInstaller.Core.SystemInfo;

namespace MaidInstaller.App.ViewModels;

/// <summary>Painel "Status do Sistema" com leituras reais, atualizado a cada 1,5 s fora da thread de UI.</summary>
public sealed class SystemStatusViewModel : ObservableObject
{
    private readonly ISystemMonitor _monitor;
    private readonly IAppLogger _logger;
    private readonly DispatcherTimer _timer;
    private bool _sampling;
    private bool _staticLoaded;

    private string _windowsVersion = L.T("Carregando…"), _windowsBuild = "", _cpuName = "…", _gpuName = "…", _ramName = "…";
    private double _cpuValue, _gpuValue, _ramValue;
    private string _cpuText = "—", _gpuText = "—", _ramText = "—", _temperatureText = "…", _ramTooltip = "";
    private bool _gpuAvailable = true;

    public SystemStatusViewModel(ISystemMonitor monitor, IAppLogger logger)
    {
        _monitor = monitor;
        _logger = logger;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1.5) };
        _timer.Tick += async (_, _) => await SampleAsync();
    }

    public string WindowsVersion { get => _windowsVersion; private set => SetProperty(ref _windowsVersion, value); }
    public string WindowsBuild { get => _windowsBuild; private set => SetProperty(ref _windowsBuild, value); }
    public string CpuName { get => _cpuName; private set => SetProperty(ref _cpuName, value); }
    public string GpuName { get => _gpuName; private set => SetProperty(ref _gpuName, value); }
    public string RamName { get => _ramName; private set => SetProperty(ref _ramName, value); }
    public double CpuValue { get => _cpuValue; private set => SetProperty(ref _cpuValue, value); }
    public double GpuValue { get => _gpuValue; private set => SetProperty(ref _gpuValue, value); }
    public double RamValue { get => _ramValue; private set => SetProperty(ref _ramValue, value); }
    public string CpuText { get => _cpuText; private set => SetProperty(ref _cpuText, value); }
    public string GpuText { get => _gpuText; private set => SetProperty(ref _gpuText, value); }
    public string RamText { get => _ramText; private set => SetProperty(ref _ramText, value); }
    public string RamTooltip { get => _ramTooltip; private set => SetProperty(ref _ramTooltip, value); }
    public bool GpuAvailable { get => _gpuAvailable; private set => SetProperty(ref _gpuAvailable, value); }
    public string TemperatureText { get => _temperatureText; private set => SetProperty(ref _temperatureText, value); }

    public async void Start()
    {
        if (_timer.IsEnabled) return;
        _timer.Start();
        if (!_staticLoaded) await LoadStaticAsync();
        await SampleAsync();
    }

    public void Stop() => _timer.Stop();

    private async Task LoadStaticAsync()
    {
        _staticLoaded = true;
        try
        {
            var info = await Task.Run(_monitor.GetStaticInfo);
            WindowsVersion = info.WindowsVersion;
            WindowsBuild = L.T($"Build {info.WindowsBuild}");
            CpuName = info.CpuName;
            GpuName = info.GpuName is null
                ? L.T("Placa de vídeo não identificada")
                : info.GpuMemoryBytes is { } mem ? $"{info.GpuName} · {FormatGb(mem)}" : info.GpuName;
            RamName = $"RAM {FormatGb(info.TotalRamBytes)}";
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao ler informações do sistema.", ex);
            WindowsVersion = L.T("Não foi possível ler");
        }
    }

    private async Task SampleAsync()
    {
        if (_sampling) return;
        _sampling = true;
        try
        {
            var usage = await Task.Run(_monitor.SampleUsage);

            CpuValue = usage.CpuPercent ?? 0;
            CpuText = usage.CpuPercent is { } c ? Percent(c) : "—";

            GpuAvailable = usage.GpuPercent is not null;
            GpuValue = usage.GpuPercent ?? 0;
            GpuText = usage.GpuPercent is { } g ? Percent(g) : "n/d";

            RamValue = usage.RamPercent;
            RamText = Percent(usage.RamPercent);
            RamTooltip = L.T($"{FormatGb(usage.RamUsedBytes)} em uso");

            var parts = new List<string>();
            if (usage.CpuTemperatureC is { } ct) parts.Add(L.T($"ACPI {ct:0}°C"));
            if (usage.GpuTemperatureC is { } gt) parts.Add(L.T($"GPU {gt:0}°C"));
            TemperatureText = parts.Count > 0 ? string.Join("  ·  ", parts) : L.T("Não suportada neste PC");
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao ler uso do sistema.", ex);
        }
        finally
        {
            _sampling = false;
        }
    }

    private static string Percent(double v) => $"{Math.Round(v).ToString(CultureInfo.InvariantCulture)}%";

    private static string FormatGb(ulong bytes)
    {
        var gb = bytes / 1024d / 1024d / 1024d;
        return gb >= 10 ? $"{Math.Round(gb):0} GB" : $"{gb:0.#} GB";
    }
}
