using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using MaidInstaller.App.Mvvm;
using MaidInstaller.App.Services;
using MaidInstaller.Core.Assistant;
using MaidInstaller.Core.Collections;

namespace MaidInstaller.App.ViewModels;

/// <summary>
/// Tela de Perfis de Instalação ou de Minhas Listas (mesmo comportamento, textos diferentes).
/// Toda alteração é salva na hora em JSON; instalar passa pela mesma confirmação da Biblioteca.
/// </summary>
public sealed class CollectionsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly MainViewModel _main;
    private readonly CollectionStore _store;
    private CollectionItemViewModel? _selected;
    private string _editName = "", _editDescription = "";
    private string _savedText = "";
    private bool _isPicking;
    private string _pickerSearch = "";

    public CollectionsViewModel(AppServices services, MainViewModel main, CollectionKind kind)
    {
        _services = services;
        _main = main;
        Kind = kind;
        _store = kind == CollectionKind.Profile ? services.Profiles : services.Lists;

        PickerView = CollectionViewSource.GetDefaultView(PickerItems);
        PickerView.Filter = o => o is PickItem p && MatchesPicker(p);

        NewCommand = new RelayCommand(CreateNew);
        DuplicateCommand = new RelayCommand(Duplicate, () => Selected is not null);
        DeleteCommand = new RelayCommand(() => _ = DeleteAsync(), () => Selected is not null);
        ExportCommand = new RelayCommand(() => _ = ExportAsync(), () => Selected is not null);
        ImportCommand = new RelayCommand(() => _ = ImportAsync());
        RestoreDefaultsCommand = new RelayCommand(() => _ = RestoreDefaultsAsync());
        InstallAllCommand = new RelayCommand(() => _ = InstallAllAsync(), () => Apps.Count > 0);
        RemoveAppCommand = new RelayCommand(p => { if (p is AppItemViewModel a) RemoveApp(a); });
        OpenPickerCommand = new RelayCommand(OpenPicker, () => Selected is not null);
        ConfirmPickerCommand = new RelayCommand(ConfirmPicker, () => PickerItems.Any(p => p.IsChecked));
        CancelPickerCommand = new RelayCommand(() => IsPicking = false);

        Reload(selectId: null);
    }

    public CollectionKind Kind { get; }
    public bool IsProfiles => Kind == CollectionKind.Profile;
    public string Title => IsProfiles ? "Perfis de Instalação" : "Minhas Listas";
    public string PageGlyph => IsProfiles ? "" : "";
    public string Subtitle => IsProfiles
        ? "Conjuntos prontos para instalar de uma vez. Edite à vontade ou crie o seu."
        : "Suas listas pessoais de programas, salvas neste PC (ou no pendrive, no modo portátil).";
    public string NewText => IsProfiles ? "Novo perfil" : "Nova lista";
    public string EmptyText => IsProfiles
        ? "Nenhum perfil por aqui. Crie um novo, importe um arquivo ou restaure os perfis prontos."
        : "Nenhuma lista ainda. Crie uma nova, importe um arquivo, ou selecione apps na Biblioteca e use \"Salvar como lista\".";
    public string FolderText => $"Arquivos em: {_store.Directory}";

    public ObservableCollection<CollectionItemViewModel> Items { get; } = [];
    public ObservableCollection<AppItemViewModel> Apps { get; } = [];
    public ObservableCollection<PickItem> PickerItems { get; } = [];
    public ICollectionView PickerView { get; }
    public bool HasItems => Items.Count > 0;
    public bool HasApps => Apps.Count > 0;

    public ICommand NewCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand RestoreDefaultsCommand { get; }
    public ICommand InstallAllCommand { get; }
    public ICommand RemoveAppCommand { get; }
    public ICommand OpenPickerCommand { get; }
    public ICommand ConfirmPickerCommand { get; }
    public ICommand CancelPickerCommand { get; }

    public CollectionItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            IsPicking = false;
            _editName = value?.Model.Name ?? "";
            _editDescription = value?.Model.Description ?? "";
            OnPropertyChanged(nameof(EditName));
            OnPropertyChanged(nameof(EditDescription));
            OnPropertyChanged(nameof(HasSelection));
            SavedText = value is null ? "" : $"Atualizado em {value.Model.UpdatedAt:dd/MM/yyyy HH:mm}";
            RebuildApps();
        }
    }

    public bool HasSelection => Selected is not null;

    /// <summary>Renomear: salva quando o campo perde o foco (ou Enter).</summary>
    public string EditName
    {
        get => _editName;
        set
        {
            if (Selected is null) return;
            var clean = AppCollection.CleanName(value);
            if (clean is null || clean == Selected.Model.Name)
            {
                _editName = Selected.Model.Name; // nome vazio não é aceito: volta ao anterior
                OnPropertyChanged();
                return;
            }
            clean = CollectionStore.UniqueName(clean, Items.Where(i => i != Selected).Select(i => i.Model.Name));
            _editName = clean;
            OnPropertyChanged();
            Selected.Model.Name = clean;
            Persist("Nome salvo");
            Selected.Refresh();
        }
    }

    public string EditDescription
    {
        get => _editDescription;
        set
        {
            if (Selected is null) return;
            var clean = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (clean is { Length: > 300 }) clean = clean[..300];
            if (clean == Selected.Model.Description) return;
            _editDescription = clean ?? "";
            OnPropertyChanged();
            Selected.Model.Description = clean;
            Persist("Descrição salva");
            Selected.Refresh();
        }
    }

    public string SavedText
    {
        get => _savedText;
        private set => SetProperty(ref _savedText, value);
    }

    public bool IsPicking
    {
        get => _isPicking;
        private set => SetProperty(ref _isPicking, value);
    }

    public string PickerSearch
    {
        get => _pickerSearch;
        set { if (SetProperty(ref _pickerSearch, value ?? "")) PickerView.Refresh(); }
    }

    // ---------- Carregar / salvar ----------

    private void Reload(string? selectId)
    {
        Items.Clear();
        foreach (var c in _store.LoadAll(_services.Catalog))
            Items.Add(new CollectionItemViewModel(c));
        OnPropertyChanged(nameof(HasItems));
        Selected = Items.FirstOrDefault(i => i.Model.Id == selectId) ?? Items.FirstOrDefault();
    }

    private bool Persist(string what)
    {
        if (Selected is null) return false;
        try
        {
            _store.Save(Selected.Model);
            SavedText = $"{what} automaticamente às {DateTime.Now:HH:mm} ✓";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Logger.Error($"Falha ao salvar {Selected.Model.Name}.", ex);
            SavedText = "Não consegui salvar (pasta protegida ou pendrive cheio). Detalhes nos logs.";
            return false;
        }
    }

    private void RebuildApps()
    {
        Apps.Clear();
        if (Selected is not null)
        {
            var library = _main.Library;
            foreach (var id in Selected.Model.AppIds)
                if (library.Find(id) is { } item) Apps.Add(item);
        }
        OnPropertyChanged(nameof(HasApps));
        CommandManager.InvalidateRequerySuggested();
    }

    // ---------- Ações ----------

    private void CreateNew()
    {
        var name = CollectionStore.UniqueName(NewText, Items.Select(i => i.Model.Name));
        var c = new AppCollection { Name = name, Glyph = PageGlyph };
        try { _store.Save(c); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Logger.Error("Falha ao criar.", ex);
            SavedText = "Não consegui criar o arquivo. Detalhes nos logs.";
            return;
        }
        Reload(c.Id);
        OpenPicker();
    }

    /// <summary>Usado pela Biblioteca: "Salvar como lista" com os apps selecionados.</summary>
    public void CreateFrom(IEnumerable<string> appIds)
    {
        var c = new AppCollection
        {
            Name = CollectionStore.UniqueName($"Lista de {DateTime.Now:dd/MM}", Items.Select(i => i.Model.Name)),
            Glyph = PageGlyph,
            AppIds = [.. appIds],
        };
        _store.Save(c);
        Reload(c.Id);
        SavedText = $"Lista criada com {c.AppIds.Count} apps. Dê um nome a ela ♡";
    }

    private void Duplicate()
    {
        if (Selected is null) return;
        try
        {
            var copy = _store.Duplicate(Selected.Model, Items.Select(i => i.Model.Name));
            Reload(copy.Id);
            SavedText = "Cópia criada ✓";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Logger.Error("Falha ao duplicar.", ex);
            SavedText = "Não consegui duplicar. Detalhes nos logs.";
        }
    }

    private async Task DeleteAsync()
    {
        if (Selected is not { } item) return;
        var notes = new List<string> { "Isso apaga só o arquivo da lista. Nenhum programa é desinstalado." };
        if (item.Model.IsBuiltIn) notes.Add("É um perfil pronto: você pode recriá-lo depois em \"Restaurar perfis prontos\".");
        else notes.Add("Se quiser guardar uma cópia, use \"Exportar\" antes.");
        var ok = await _main.ShowDialogAsync(new DialogViewModel($"Excluir \"{item.Model.Name}\"?",
            $"{item.Model.AppIds.Count} aplicativo(s) nesta {(IsProfiles ? "coleção" : "lista")}.", [], notes, "Excluir"));
        if (!ok) return;
        try
        {
            _store.Delete(item.Model);
            Reload(selectId: null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Logger.Error("Falha ao excluir.", ex);
            SavedText = "Não consegui excluir o arquivo. Detalhes nos logs.";
        }
    }

    /// <summary>No modo portátil, as janelas de arquivo abrem no próprio pendrive.</summary>
    private string DialogFolder => _services.Paths.IsPortable
        ? _services.Paths.DataRoot
        : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private async Task ExportAsync()
    {
        if (Selected is not { } item) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar " + (IsProfiles ? "perfil" : "lista"),
            FileName = SafeFileName(item.Model.Name) + CollectionTransfer.FileExtension,
            Filter = "Lista do Maid Installer (*.maidlist.json)|*.maidlist.json|JSON (*.json)|*.json",
            InitialDirectory = DialogFolder,
            AddToRecent = false, // não deixa rastro na lista de "recentes" do Windows
            AddExtension = false,
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            CollectionTransfer.Export(item.Model, Kind, _services.Catalog, dialog.FileName);
            _services.Logger.Info($"Exportado: {item.Model.Name} → {dialog.FileName}");
            SavedText = $"Exportado para {Path.GetFileName(dialog.FileName)} ✓";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Logger.Error("Falha ao exportar.", ex);
            await _main.ShowDialogAsync(new DialogViewModel("Não foi possível exportar", ex.Message, [], [], "Entendi", cancelText: null));
        }
    }

    private async Task ImportAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importar " + (IsProfiles ? "perfil" : "lista"),
            Filter = "Lista do Maid Installer (*.maidlist.json;*.json)|*.maidlist.json;*.json",
            InitialDirectory = DialogFolder,
            AddToRecent = false, // não deixa rastro na lista de "recentes" do Windows
        };
        if (dialog.ShowDialog() != true) return;

        ImportResult result;
        try
        {
            result = CollectionTransfer.Import(dialog.FileName, Kind, _services.Catalog);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _services.Logger.Warn($"Importação recusada ({dialog.FileName}): {ex.Message}");
            await _main.ShowDialogAsync(new DialogViewModel("Não foi possível importar", ex.Message, [], [], "Entendi", cancelText: null));
            return;
        }

        var c = result.Collection;
        c.Name = CollectionStore.UniqueName(c.Name, Items.Select(i => i.Model.Name));
        try { _store.Save(c); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Logger.Error("Falha ao salvar importação.", ex);
            SavedText = "Não consegui salvar o arquivo importado. Detalhes nos logs.";
            return;
        }
        _services.Logger.Info($"Importado: {c.Name} ({c.AppIds.Count} apps, {result.Unknown.Count} desconhecidos) de {dialog.FileName}");
        Reload(c.Id);
        SavedText = $"Importado com {c.AppIds.Count} apps ✓";

        if (result.Unknown.Count > 0)
            await _main.ShowDialogAsync(new DialogViewModel("Importado, com avisos",
                $"\"{c.Name}\" foi importado com {c.AppIds.Count} aplicativo(s).",
                [], [$"Estes itens não estão no catálogo verificado e foram deixados de fora (por segurança, o Maid Installer só instala apps do catálogo): {string.Join(", ", result.Unknown)}."],
                "Entendi", cancelText: null));
    }

    private async Task RestoreDefaultsAsync()
    {
        var existing = Items.Select(i => i.Model).ToList();
        var missing = _services.DefaultProfiles.Where(d => existing.All(e => e.BuiltInId != d.BuiltInId)).ToList();
        if (missing.Count == 0)
        {
            await _main.ShowDialogAsync(new DialogViewModel("Perfis prontos", "Os perfis prontos já estão todos aqui. ♡", [], [], "Entendi", cancelText: null));
            return;
        }
        try
        {
            DefaultProfiles.Seed(_store, _services.DefaultProfiles, existing, _services.Settings.SeededProfiles, force: true);
            _services.SaveSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Logger.Error("Falha ao restaurar perfis prontos.", ex);
        }
        Reload(Selected?.Model.Id);
        SavedText = $"Restaurado(s): {string.Join(", ", missing.Select(m => m.Name))} ✓";
    }

    private async Task InstallAllAsync()
    {
        if (Apps.Count == 0) return;
        var installed = Apps.Where(a => a.IsInstalled).Select(a => a.App.Id).ToHashSet();
        await _main.RequestInstallAsync(Apps.Select(a => a.App), a => installed.Contains(a.Id));
    }

    private void RemoveApp(AppItemViewModel app)
    {
        if (Selected is null) return;
        Selected.Model.AppIds.Remove(app.App.Id);
        Apps.Remove(app);
        OnPropertyChanged(nameof(HasApps));
        Persist($"{app.App.Name} removido; salvo");
        Selected.Refresh();
    }

    // ---------- Escolher apps ----------

    private void OpenPicker()
    {
        if (Selected is null) return;
        PickerItems.Clear();
        foreach (var item in _main.Library.Apps.Where(a => !Selected.Model.AppIds.Contains(a.App.Id)))
            PickerItems.Add(new PickItem(item));
        PickerSearch = "";
        PickerView.Refresh();
        IsPicking = true;
    }

    private void ConfirmPicker()
    {
        if (Selected is null) return;
        var picked = PickerItems.Where(p => p.IsChecked).ToList();
        Selected.Model.AppIds.AddRange(picked.Select(p => p.Item.App.Id));
        IsPicking = false;
        RebuildApps();
        Persist($"{picked.Count} app(s) adicionado(s); salvo");
        Selected.Refresh();
    }

    private bool MatchesPicker(PickItem p)
    {
        var q = OfflineResponder.Normalize(PickerSearch);
        if (q.Length == 0) return true;
        var app = p.Item.App;
        var hay = OfflineResponder.Normalize(string.Join(' ', [app.Name, app.Developer, app.Description, p.Item.CategoryName, .. app.Aliases]));
        return q.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(t => hay.Contains(t.Length >= 6 ? t[..^2] : t));
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string([.. name.Select(c => invalid.Contains(c) ? '-' : c)]).Trim();
        return clean.Length == 0 ? "lista" : clean;
    }
}

public sealed class CollectionItemViewModel(AppCollection model) : ObservableObject
{
    public AppCollection Model { get; } = model;
    public string Name => Model.Name;
    public string Glyph => Model.Glyph;
    public string? Description => Model.Description;
    public bool IsBuiltIn => Model.IsBuiltIn;
    public string CountText => Model.AppIds.Count == 1 ? "1 aplicativo" : $"{Model.AppIds.Count} aplicativos";
    public override string ToString() => $"{Name} — {CountText}";

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(CountText));
    }
}

public sealed class PickItem(AppItemViewModel item) : ObservableObject
{
    private bool _isChecked;
    public AppItemViewModel Item { get; } = item;
    public override string ToString() => Item.App.Name;

    public bool IsChecked
    {
        get => _isChecked;
        set { if (SetProperty(ref _isChecked, value)) CommandManager.InvalidateRequerySuggested(); }
    }
}
