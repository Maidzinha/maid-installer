using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MaidInstaller.App.Mvvm;
using MaidInstaller.App.Services;
using MaidInstaller.Core.Installation;

namespace MaidInstaller.App.ViewModels;

/// <summary>Tela de acompanhamento das instalações (fila do <see cref="InstallationService"/>).</summary>
public sealed class InstallsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private string _summary = L.T("Nenhuma instalação ainda.");
    private string _sidebarText = "";
    private bool _rebootRequired;

    public InstallsViewModel(AppServices services)
    {
        _services = services;
        var installer = services.Installer;

        installer.JobAdded += job => OnUi(() =>
        {
            Jobs.Add(new InstallJobViewModel(job, services.AppIcons.GetValueOrDefault(job.App.Id) ?? BundledIcons.Get(job.App.Id)));
            UpdateSummary();
        });
        installer.JobStarted += _ => OnUi(UpdateSummary);
        installer.JobFinished += _ => OnUi(UpdateSummary);
        installer.QueueDrained += () => OnUi(UpdateSummary);

        CancelCommand = new RelayCommand(p => { if (p is InstallJobViewModel j) installer.Cancel(j.Job); }, p => p is InstallJobViewModel { Job.CanCancel: true });
        RetryCommand = new RelayCommand(p => { if (p is InstallJobViewModel j) { installer.Retry(j.Job); UpdateSummary(); } }, p => p is InstallJobViewModel { Job.CanRetry: true });
        OpenLogCommand = new RelayCommand(p => OpenLog(p as InstallJobViewModel), p => p is InstallJobViewModel { Job.LogFile: not null });
        CancelAllCommand = new RelayCommand(() => installer.CancelAll(), () => installer.IsBusy);
        ClearFinishedCommand = new RelayCommand(ClearFinished, () => Jobs.Any(j => j.Job.IsFinished));
    }

    public ObservableCollection<InstallJobViewModel> Jobs { get; } = [];
    public ICommand CancelCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand OpenLogCommand { get; }
    public ICommand CancelAllCommand { get; }
    public ICommand ClearFinishedCommand { get; }

    public bool IsBusy => _services.Installer.IsBusy;
    public bool HasJobs => Jobs.Count > 0;

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string SidebarText { get => _sidebarText; private set => SetProperty(ref _sidebarText, value); }
    public bool RebootRequired { get => _rebootRequired; private set => SetProperty(ref _rebootRequired, value); }

    /// <summary>Resumo real para a Maid Assistant responder "como está a instalação?".</summary>
    public string? DescribeForAssistant()
    {
        var jobs = _services.Installer.Jobs;
        if (jobs.Count == 0) return null;
        var active = jobs.FirstOrDefault(j => j.IsActive);
        var waiting = jobs.Count(j => j.State == InstallJobState.Waiting);
        var done = jobs.Count(j => j.State is InstallJobState.Completed or InstallJobState.AlreadyInstalled);
        var failed = jobs.Where(j => j.State == InstallJobState.Failed).Select(j => j.App.Name).ToList();
        var text = active is null ? L.T("No momento não estou instalando nada.") : L.T($"Agora estou com o {active.App.Name}: {active.Message}");
        text += L.T($" Prontos: {done}. Na fila: {waiting}.");
        if (failed.Count > 0) text += L.T($" Com falha: {string.Join(", ", failed)}.");
        return text;
    }

    private void UpdateSummary()
    {
        var jobs = _services.Installer.Jobs;
        var finished = jobs.Count(j => j.IsFinished);
        var ok = jobs.Count(j => j.State == InstallJobState.Completed);
        var already = jobs.Count(j => j.State == InstallJobState.AlreadyInstalled);
        var failed = jobs.Count(j => j.State == InstallJobState.Failed);
        var cancelled = jobs.Count(j => j.State == InstallJobState.Cancelled);

        var parts = new List<string> { L.T($"{finished} de {jobs.Count} finalizados") };
        if (ok > 0) parts.Add(L.T($"{ok} concluídos"));
        if (already > 0) parts.Add(L.T($"{already} já instalados"));
        if (failed > 0) parts.Add(L.T($"{failed} com falha"));
        if (cancelled > 0) parts.Add($"{cancelled} cancelados");
        Summary = jobs.Count == 0 ? L.T("Nenhuma instalação ainda.") : string.Join(" · ", parts);
        SidebarText = IsBusy ? L.T($"Instalando {finished + 1} de {jobs.Count}…") : jobs.Count > 0 ? L.T("Instalações ✓") : "";
        RebootRequired = jobs.Any(j => j.RebootRequired);
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(HasJobs));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearFinished()
    {
        var removed = _services.Installer.ClearFinished();
        foreach (var vm in Jobs.Where(j => removed.Contains(j.Job)).ToList()) Jobs.Remove(vm);
        UpdateSummary();
    }

    private void OpenLog(InstallJobViewModel? job)
    {
        if (job?.Job.LogFile is not { } file || !File.Exists(file)) return;
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true });
    }

    private static void OnUi(Action action) => Application.Current?.Dispatcher.InvokeAsync(action);
}

/// <summary>Um item da tela de acompanhamento. Reencaminha as mudanças do job (que vêm de outra thread).</summary>
public sealed class InstallJobViewModel : ObservableObject
{
    public InstallJobViewModel(InstallJob job, ImageSource? icon)
    {
        Job = job;
        Icon = icon;
        job.PropertyChanged += OnJobChanged;
    }

    public InstallJob Job { get; }
    public ImageSource? Icon { get; }
    public string Name => Job.App.Name;
    public string Monogram => Name[..1].ToUpperInvariant();
    public string OperationText => Job.Operation == InstallOperation.Upgrade ? L.T("Atualização") : L.T("Instalação");
    public string SourceText => Job.App.WinGetSource == "msstore" ? "Microsoft Store" : L.T($"WinGet · {Job.App.WinGetId}");

    public string StatusText => Job.State switch
    {
        InstallJobState.Waiting => L.T("Aguardando"),
        InstallJobState.Downloading => L.T("Baixando"),
        InstallJobState.Installing => L.T("Instalando"),
        InstallJobState.Completed => Job.RebootRequired ? L.T("Concluído · reiniciar") : L.T("Concluído"),
        InstallJobState.AlreadyInstalled => L.T("Já instalado"),
        InstallJobState.Failed => L.T("Falhou"),
        InstallJobState.Cancelled => L.T("Cancelado"),
        _ => "",
    };

    public string ProgressText => Job.Progress is { } p ? $"{p:0}%" : Job.IsActive ? L.T("em andamento…") : "";
    public double ProgressValue => Job.Progress ?? (Job.IsFinished && Job.State == InstallJobState.Completed ? 100 : 0);

    private void OnJobChanged(object? sender, PropertyChangedEventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(Job));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(ProgressValue));
        });
    }
}
