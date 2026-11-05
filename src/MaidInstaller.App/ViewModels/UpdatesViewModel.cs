using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MaidInstaller.App.Mvvm;
using MaidInstaller.App.Services;
using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Installation;
using MaidInstaller.Core.WinGet;

namespace MaidInstaller.App.ViewModels;

/// <summary>
/// Atualizações: consulta o WinGet (só leitura) e atualiza apenas o que o usuário escolher e confirmar.
/// Nunca usa "winget upgrade --all".
/// </summary>
public sealed class UpdatesViewModel : ObservableObject
{
    private const string AppInstallerId = "Microsoft.AppInstaller";
    private readonly AppServices _services;
    private readonly MainViewModel _main;
    private bool _isChecking, _checkedOnce;
    private string _statusLine = L.T("Clique em \"Verificar atualizações\" para consultar o WinGet.");
    private int _selectedCount;

    public UpdatesViewModel(AppServices services, MainViewModel main)
    {
        _services = services;
        _main = main;

        CheckCommand = new RelayCommand(() => _ = CheckAsync(), () => !IsChecking);
        UpdateOneCommand = new RelayCommand(p => { if (p is UpdateItemViewModel i) _ = RequestUpgradeAsync([i]); }, p => p is UpdateItemViewModel { IsQueued: false });
        UpdateSelectedCommand = new RelayCommand(() => _ = RequestUpgradeAsync([.. Items.Where(i => i.IsSelected)]), () => SelectedCount > 0);
        SelectAllCommand = new RelayCommand(() => { foreach (var i in Items.Where(i => !i.IsQueued)) i.IsSelected = true; }, () => Items.Count > 0);
        ClearSelectionCommand = new RelayCommand(() => { foreach (var i in Items) i.IsSelected = false; }, () => SelectedCount > 0);

        foreach (var e in services.History.Entries.Take(100)) History.Add(new HistoryItemViewModel(e));
        services.History.Added += e => Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            History.Insert(0, new HistoryItemViewModel(e));
            while (History.Count > 100) History.RemoveAt(History.Count - 1);
            OnPropertyChanged(nameof(HasHistory));
        });
        services.Installer.JobFinished += _ => Application.Current?.Dispatcher.InvokeAsync(UpdateQueuedFlags);
    }

    public ObservableCollection<UpdateItemViewModel> Items { get; } = [];
    public ObservableCollection<HistoryItemViewModel> History { get; } = [];
    public bool HasItems => Items.Count > 0;
    public bool HasHistory => History.Count > 0;
    public bool ShowEmpty => !HasItems && _checkedOnce && !IsChecking;

    public ICommand CheckCommand { get; }
    public ICommand UpdateOneCommand { get; }
    public ICommand UpdateSelectedCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand ClearSelectionCommand { get; }

    public bool IsChecking
    {
        get => _isChecking;
        private set { if (SetProperty(ref _isChecking, value)) { OnPropertyChanged(nameof(ShowEmpty)); CommandManager.InvalidateRequerySuggested(); } }
    }

    public string StatusLine { get => _statusLine; private set => SetProperty(ref _statusLine, value); }

    public int SelectedCount
    {
        get => _selectedCount;
        private set { if (SetProperty(ref _selectedCount, value)) OnPropertyChanged(nameof(SelectionText)); }
    }

    public string SelectionText => SelectedCount switch
    {
        0 => L.T("Nenhum programa selecionado"),
        1 => L.T("1 programa selecionado"),
        var n => L.T($"{n} programas selecionados"),
    };

    /// <summary>Ao abrir a página pela primeira vez, consulta (só leitura).</summary>
    public void OnShown()
    {
        if (!_checkedOnce && !IsChecking) _ = CheckAsync();
    }

    public async Task CheckAsync()
    {
        if (IsChecking) return;
        IsChecking = true;
        StatusLine = L.T("Perguntando ao WinGet quais programas têm versão nova… (pode levar até um minuto)");
        try
        {
            if (!await _services.WinGet.IsAvailableAsync())
            {
                StatusLine = L.T("WinGet não encontrado neste PC. Veja o cartão WinGet no Kit Pós-Formatação.");
                return;
            }
            var list = await _services.WinGet.GetUpgradesAsync();
            Items.Clear();
            foreach (var c in list.OrderBy(c => c.Id.Equals(AppInstallerId, StringComparison.OrdinalIgnoreCase)).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var app = ToApp(c);
                Items.Add(new UpdateItemViewModel(c, app, _services.AppIcons.GetValueOrDefault(app.Id), OnSelectionChanged));
            }
            UpdateQueuedFlags();
            _ = FillIconsAsync();
            StatusLine = Items.Count == 0
                ? L.T($"Verificado às {DateTime.Now:HH:mm}: tudo em dia ♡")
                : L.T($"Verificado às {DateTime.Now:HH:mm}: {Items.Count} programa(s) com versão nova. Nada é atualizado sem você pedir.");
            _services.Logger.Info($"Atualizações: {Items.Count} disponíveis ({string.Join(", ", Items.Select(i => i.Candidate.Id))}).");
        }
        catch (Exception ex)
        {
            _services.Logger.Error("Falha ao consultar atualizações.", ex);
            StatusLine = L.T("Não consegui consultar o WinGet agora. Detalhes nos logs.");
        }
        finally
        {
            _checkedOnce = true;
            IsChecking = false;
            OnSelectionChanged();
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(ShowEmpty));
        }
    }

    /// <summary>Usa o app do catálogo quando existe (nome e ícone); senão, os dados que o próprio WinGet informou.</summary>
    private CatalogApp ToApp(UpgradeCandidate c) =>
        _services.Catalog.Apps.FirstOrDefault(a => string.Equals(a.WinGetId, c.Id, StringComparison.OrdinalIgnoreCase) && a.WinGetSource == c.Source)
        ?? new CatalogApp
        {
            Id = "winget-" + new string([.. c.Id.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')]),
            Name = c.Name.Length > 0 ? c.Name : c.Id,
            WinGetId = c.Id,
            WinGetSource = c.Source,
            Price = "",
        };

    /// <summary>Os ícones reais vêm da Biblioteca (Menu Iniciar); quando ela terminar de carregar, aparecem aqui.</summary>
    private async Task FillIconsAsync()
    {
        await _main.Library.WaitForStatusAsync();
        foreach (var i in Items.Where(i => i.Icon is null))
            i.Icon = _services.AppIcons.GetValueOrDefault(i.App.Id);

        // Programas fora do catálogo: procura o atalho no Menu Iniciar pelo nome informado pelo WinGet.
        var shell = await _services.ShellApps.GetAppsAsync();
        var matches = Items.Where(i => i.Icon is null)
            .Select(i => (item: i, shell: Core.Shell.ShellAppsFolder.Match(shell, [i.Candidate.Name])))
            .Where(x => x.shell is not null).ToList();
        if (matches.Count > 0)
        {
            var icons = await _services.ShellApps.GetIconsAsync(matches.Select(m => m.shell!));
            foreach (var (item, s) in matches) item.Icon ??= icons.GetValueOrDefault(s!.ParsingName);
        }

        // Sem ícone do Windows: o ícone oficial que vem com o app (só para apps do catálogo).
        foreach (var i in Items.Where(i => i.Icon is null))
            i.Icon = BundledIcons.Get(i.App.Id);
    }

    private void OnSelectionChanged() => SelectedCount = Items.Count(i => i.IsSelected);

    private void UpdateQueuedFlags()
    {
        var active = _services.Installer.Jobs.Where(j => !j.IsFinished).Select(j => j.App.WinGetId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var i in Items) i.IsQueued = active.Contains(i.Candidate.Id);
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task RequestUpgradeAsync(IReadOnlyList<UpdateItemViewModel> items)
    {
        var list = items.Where(i => !i.IsQueued)
            .OrderBy(i => i.Candidate.Id.Equals(AppInstallerId, StringComparison.OrdinalIgnoreCase)) // o WinGet se atualiza por último
            .ToList();
        if (list.Count == 0) return;

        var notes = new List<string>
        {
            L.T("Cada programa é atualizado pelo WinGet, das fontes oficiais, em modo silencioso e um de cada vez."),
            L.T("Feche os programas da lista antes: um programa aberto pode impedir a atualização."),
            L.T("Alguns atualizadores pedem permissão de administrador (UAC). Se você recusar, só aquele é cancelado."),
            L.T("Ao confirmar, você aceita as licenças das novas versões. O PC não será reiniciado automaticamente."),
        };
        if (list.Any(i => i.Candidate.Id.Equals(AppInstallerId, StringComparison.OrdinalIgnoreCase)))
            notes.Add(L.T("O App Installer é o próprio WinGet: ele fica por último e, enquanto se atualiza, o WinGet some por alguns segundos."));
        if (list.Any(i => i.Candidate.InstalledVersion.StartsWith('<')))
            notes.Add(L.T("Versão com \"<\" significa que o Windows não informa a versão exata instalada; o WinGet só sabe que é mais antiga."));

        var ok = await _main.ShowDialogAsync(new DialogViewModel(
            list.Count == 1 ? L.T($"Atualizar {list[0].Name}?") : L.T($"Atualizar {list.Count} programas?"),
            L.T("Confira o que será atualizado:"),
            [.. list.Select(i => new DialogItem(i.Name, $"{i.Candidate.InstalledVersion} → {i.Candidate.AvailableVersion} · {i.SourceText}"))],
            notes,
            list.Count == 1 ? L.T("Atualizar") : L.T($"Atualizar {list.Count}")));
        if (!ok)
        {
            _services.Logger.Info("Atualização cancelada na confirmação.");
            return;
        }

        _services.Logger.Info($"Atualização autorizada: {string.Join(", ", list.Select(i => $"{i.Candidate.Id} {i.Candidate.InstalledVersion}→{i.Candidate.AvailableVersion}"))}");
        foreach (var i in list)
        {
            var job = _services.Installer.Enqueue(i.App, InstallOperation.Upgrade);
            job.FromVersion = i.Candidate.InstalledVersion;
            job.ToVersion = i.Candidate.AvailableVersion;
            i.IsSelected = false;
        }
        UpdateQueuedFlags();
        _main.ShowInstalls();
    }
}

public sealed class UpdateItemViewModel(UpgradeCandidate candidate, CatalogApp app, ImageSource? icon, Action selectionChanged) : ObservableObject
{
    private bool _isSelected, _isQueued;
    private ImageSource? _icon = icon;

    public UpgradeCandidate Candidate { get; } = candidate;
    public CatalogApp App { get; } = app;
    public ImageSource? Icon { get => _icon; set => SetProperty(ref _icon, value); }
    public string Name => App.Name;
    public string Monogram => Name[..1].ToUpperInvariant();
    public string UpdateButtonName => L.T($"Atualizar {Name}");
    public string SourceText => Candidate.Source == "msstore" ? "Microsoft Store" : L.T($"WinGet · {Candidate.Id}");
    public string VersionText => $"{Candidate.InstalledVersion}  →  {Candidate.AvailableVersion}";
    public override string ToString() => $"{Name} {VersionText}";

    public bool IsSelected
    {
        get => _isSelected;
        set { if (SetProperty(ref _isSelected, value && !IsQueued)) selectionChanged(); }
    }

    public bool IsQueued
    {
        get => _isQueued;
        set
        {
            if (!SetProperty(ref _isQueued, value)) return;
            if (value) IsSelected = false;
            OnPropertyChanged(nameof(ButtonText));
        }
    }

    public string ButtonText => IsQueued ? L.T("Na fila") : L.T("Atualizar");
}

public sealed class HistoryItemViewModel(HistoryEntry entry)
{
    public HistoryEntry Entry { get; } = entry;
    public string DateText => Entry.Date.ToString(L.T("dd/MM HH:mm"));
    public string Title => $"{(Entry.Operation == "upgrade" ? L.T("Atualização") : L.T("Instalação"))} · {Entry.Name}";
    public string VersionText => Entry.FromVersion is not null || Entry.ToVersion is not null
        ? $"{Entry.FromVersion ?? "?"} → {Entry.ToVersion ?? "?"}"
        : Entry.WinGetId ?? "";
    public string ResultText => Entry.Result switch
    {
        "completed" => Entry.RebootRequired ? L.T("Concluído · reiniciar") : L.T("Concluído"),
        "already" => Entry.Operation == "upgrade" ? L.T("Já estava atualizado") : L.T("Já instalado"),
        "cancelled" => L.T("Cancelado"),
        _ => L.T("Falhou"),
    };
    public bool IsOk => Entry.Result is "completed" or "already";
    public string? Message => Entry.Message;
}
