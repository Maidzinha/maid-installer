using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using MaidInstaller.App.Mvvm;
using MaidInstaller.App.Services;
using MaidInstaller.Core.Assistant;
using MaidInstaller.Core.Settings;
using MaidInstaller.Core.Storage;

namespace MaidInstaller.App.ViewModels;

/// <summary>Uma opção de escolha única (idioma, tema) mostrada como botão de seleção.</summary>
public sealed class OptionItem(string value, string label, Action changed) : ObservableObject
{
    private bool _isSelected;
    public string Value { get; } = value;
    public string Label { get; } = label;

    public bool IsSelected
    {
        get => _isSelected;
        set { if (SetProperty(ref _isSelected, value)) changed(); }
    }
}

/// <summary>
/// Configurações: nome do usuário, idioma e tema, modo portátil/armazenamento (com a cópia para pendrive) e a IA local
/// da Maid Assistant (baixar, ligar/desligar, remover). Cada ação que baixa, copia ou apaga algo pede confirmação.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    public const int MaxNameLength = 40;

    private readonly AppServices _services;
    private readonly MainViewModel _main;
    private string _userName;
    private string? _message;
    private bool _includeModel;
    private bool _isCopying;
    private CopyProgress? _copyProgress;
    private CancellationTokenSource? _copyCts;

    public SettingsViewModel(AppServices services, MainViewModel main)
    {
        _services = services;
        _main = main;
        _userName = services.Settings.UserName;

        DownloadCommand = new RelayCommand(() => _ = DownloadAsync(), () => !Ai.IsDownloading);
        CancelDownloadCommand = new RelayCommand(Ai.CancelDownload, () => Ai.IsDownloading);
        DeleteModelCommand = new RelayCommand(() => _ = DeleteAsync(), () => Ai.HasModelFile);
        OpenModelsFolderCommand = new RelayCommand(OpenModelsFolder);
        RescanCommand = new RelayCommand(() => _ = Ai.RescanAsync(), () => !Ai.IsDownloading);

        CreatePortableCommand = new RelayCommand(() => _ = CreatePortableAsync(), () => !IsCopying);
        CancelCopyCommand = new RelayCommand(() => _copyCts?.Cancel(), () => IsCopying);
        OpenDataFolderCommand = new RelayCommand(() => OpenFolder(_services.Paths.DataRoot));

        Ai.PropertyChanged += OnAiChanged;

        LanguageOptions =
        [
            new(L.English, "English", OnAppearanceChanged),
            new(L.Portuguese, "Português (Brasil)", OnAppearanceChanged),
            new(L.Auto, L.T("Automático (idioma do Windows)"), OnAppearanceChanged),
        ];
        ThemeOptions =
        [
            new(AppSettings.ThemeDark, L.T("Escuro"), OnAppearanceChanged),
            new(AppSettings.ThemeLight, L.T("Claro"), OnAppearanceChanged),
            new(AppSettings.ThemeBlackPink, L.T("Preto e rosa"), OnAppearanceChanged),
        ];
        foreach (var o in LanguageOptions) o.IsSelected = o.Value == services.Settings.Language;
        foreach (var o in ThemeOptions) o.IsSelected = o.Value == services.Settings.Theme;
        ApplyAppearanceCommand = new RelayCommand(() => _ = ApplyAppearanceAsync(), () => HasPendingAppearance);
    }

    // ---------- Idioma e tema (aplicados ao reabrir o app) ----------

    public IReadOnlyList<OptionItem> LanguageOptions { get; }
    public IReadOnlyList<OptionItem> ThemeOptions { get; }
    public ICommand ApplyAppearanceCommand { get; }

    private string? ChosenLanguage => LanguageOptions.FirstOrDefault(o => o.IsSelected)?.Value;
    private string? ChosenTheme => ThemeOptions.FirstOrDefault(o => o.IsSelected)?.Value;

    public bool HasPendingAppearance =>
        (ChosenLanguage is { } l && l != _services.Settings.Language) || (ChosenTheme is { } t && t != _services.Settings.Theme);

    private void OnAppearanceChanged()
    {
        OnPropertyChanged(nameof(HasPendingAppearance));
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task ApplyAppearanceAsync()
    {
        if (_services.Installer.IsBusy || Ai.IsDownloading || IsCopying)
        {
            Message = L.T("Espere terminar as instalações, o download ou a cópia em andamento antes de reabrir o app.");
            return;
        }
        var ok = await _main.ShowDialogAsync(new DialogViewModel(L.T("Reabrir o Maid Installer?"),
            L.T("O app vai fechar e abrir de novo já com o idioma e o tema escolhidos."),
            [], [L.T("Só o Maid Installer reabre: o PC não é reiniciado.")], L.T("Reabrir agora")));
        if (!ok) return;

        _services.Settings.Language = ChosenLanguage ?? _services.Settings.Language;
        _services.Settings.Theme = ChosenTheme ?? _services.Settings.Theme;
        _services.SaveSettings();
        _services.Logger.Info($"Idioma/tema alterados para {_services.Settings.Language} / {_services.Settings.Theme}; reabrindo o app.");
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _services.Logger.Error("Não foi possível reabrir o app.", ex);
            Message = L.T("Salvo! Feche e abra o Maid Installer para ver a mudança.");
        }
    }

    // ---------- Modo portátil e armazenamento ----------

    public ICommand CreatePortableCommand { get; }
    public ICommand CancelCopyCommand { get; }
    public ICommand OpenDataFolderCommand { get; }

    public bool IsPortable => _services.Paths.IsPortable;
    public string ModeTitle => IsPortable ? L.T("Modo portátil (pendrive)") : L.T("Modo instalado");
    public string ModeDescription => IsPortable
        ? L.T("Configurações, perfis, listas, logs e o modelo de IA ficam nesta pasta, junto do MaidInstaller.exe. ") +
          L.T("No PC em uso só ficam os arquivos temporários do WinGet (apagados em seguida) e o que você mandar instalar ou alterar.")
        : L.T("Configurações, perfis, listas e logs ficam na pasta do seu usuário neste PC. ") +
          L.T("Para levar tudo num pendrive (inclusive com Ventoy), crie uma cópia portátil.");
    public string DataRoot => _services.Paths.DataRoot;
    public string FreeSpaceText => _services.Paths.GetFreeSpaceBytes() is { } f ? L.T($"{ModelDownloader.Format(f)} livres") : L.T("espaço livre desconhecido");

    /// <summary>Copiar o modelo de IA junto (desmarcado por padrão: ele tem centenas de MB).</summary>
    public bool IncludeModel
    {
        get => _includeModel;
        set => SetProperty(ref _includeModel, value);
    }

    public bool IsCopying
    {
        get => _isCopying;
        private set { if (SetProperty(ref _isCopying, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public double CopyPercent => (_copyProgress?.Fraction ?? 0) * 100;
    public string CopyText => _copyProgress is { } p
        ? L.T($"{ModelDownloader.Format(p.BytesCopied)} de {ModelDownloader.Format(p.TotalBytes)} ({p.Fraction:P0}) · {p.CurrentFile}")
        : "";

    private async Task CreatePortableAsync()
    {
        var picker = new Microsoft.Win32.OpenFolderDialog
        {
            Title = L.T("Escolha o pendrive (ou a pasta) para a cópia portátil"),
            AddToRecent = false,
        };
        if (picker.ShowDialog() != true) return;
        await CreatePortableAtAsync(picker.FolderName);
    }

    /// <summary>Monta o plano, confirma com o usuário e copia (com progresso real e cancelamento).</summary>
    public async Task CreatePortableAtAsync(string chosenFolder)
    {
        PortableCopyPlan plan;
        try
        {
            plan = await Task.Run(() => PortableCopy.Plan(_services.Paths, chosenFolder, IncludeModel && Ai.HasModelFile, Ai.ModelPath));
        }
        catch (Exception ex) when (ex is PortableCopyException or IOException or UnauthorizedAccessException)
        {
            Message = ex.Message;
            return;
        }

        var required = PortableCopy.RequiredBytes(plan);
        var free = FreeSpace(plan.Destination);
        if (free is { } f && f < required)
        {
            await _main.ShowDialogAsync(new DialogViewModel(L.T("Espaço insuficiente"),
                L.T($"A cópia precisa de {ModelDownloader.Format(required)} e o destino tem {ModelDownloader.Format(f)} livres."),
                [], [plan.IncludesModel ? L.T("Tente sem o modelo de IA, ou libere espaço no pendrive.") : L.T("Libere espaço no pendrive e tente de novo.")],
                L.T("Entendi"), cancelText: null));
            return;
        }

        var notes = new List<string>
        {
            plan.UpdatesExisting
                ? L.T("Já existe uma cópia portátil aqui: os arquivos do aplicativo serão atualizados e os dados dela (configurações, perfis e listas) serão mantidos.")
                : L.T("Vão junto: suas configurações, perfis e listas."),
            L.T("Não vão: logs, cache e histórico de instalações deste PC."),
            plan.IncludesModel ? L.T("O modelo de IA será copiado.") : L.T("O modelo de IA não será copiado (pode ser baixado depois, pela própria cópia portátil)."),
            L.T("Será criado o arquivo MaidInstaller.portable, que ativa o modo portátil."),
        };
        if (!plan.SourceIsSelfContained)
            notes.Add(L.T("Atenção: esta versão do app precisa do .NET 10 instalado no PC. Para abrir em PCs recém-formatados, use a versão portátil gerada por publish-portable.ps1 (ela já leva o .NET junto)."));
        if (free is { } ff) notes.Add(L.T($"Espaço livre no destino: {ModelDownloader.Format(ff)}."));

        var ok = await _main.ShowDialogAsync(new DialogViewModel(
            plan.UpdatesExisting ? L.T("Atualizar a cópia portátil?") : L.T("Criar a cópia portátil?"),
            L.T($"Destino: {plan.Destination}"),
            [new DialogItem(L.T($"{plan.Files.Count} arquivos"), L.T($"{ModelDownloader.Format(plan.TotalBytes)} · {plan.UserDataFiles} com dados seus"))],
            notes, plan.UpdatesExisting ? L.T("Atualizar") : L.T("Criar cópia")));
        if (!ok) return;

        _services.Logger.Info($"Criando cópia portátil em {plan.Destination} ({plan.Files.Count} arquivos, {plan.TotalBytes} bytes, modelo: {plan.IncludesModel}).");
        _copyCts = new CancellationTokenSource();
        IsCopying = true;
        Message = null;
        var progress = new Progress<CopyProgress>(p =>
        {
            _copyProgress = p;
            OnPropertyChanged(nameof(CopyPercent));
            OnPropertyChanged(nameof(CopyText));
        });
        try
        {
            var token = _copyCts.Token;
            await Task.Run(() => PortableCopy.ExecuteAsync(plan, progress, token));
            _services.Logger.Info($"Cópia portátil pronta: {plan.Destination}");
            Message = L.T($"Cópia portátil pronta em {plan.Destination} ♡ É só abrir o MaidInstaller.exe de lá.");
        }
        catch (OperationCanceledException)
        {
            _services.Logger.Info("Cópia portátil cancelada pelo usuário.");
            Message = L.T("Cópia cancelada. Para terminar depois, é só criar a cópia de novo na mesma pasta.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _services.Logger.Error($"Falha na cópia portátil para {plan.Destination}.", ex);
            Message = L.T("Não foi possível terminar a cópia: ") + ex.Message;
        }
        finally
        {
            _copyCts.Dispose();
            _copyCts = null;
            _copyProgress = null;
            IsCopying = false;
            OnPropertyChanged(nameof(CopyPercent));
            OnPropertyChanged(nameof(CopyText));
        }
    }

    private void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _services.Logger.Error($"Não foi possível abrir a pasta {dir}.", ex);
        }
    }

    public LocalAiService Ai => _services.Ai;
    public ModelInfo Model => Ai.Model;

    public ICommand DownloadCommand { get; }
    public ICommand CancelDownloadCommand { get; }
    public ICommand DeleteModelCommand { get; }
    public ICommand OpenModelsFolderCommand { get; }
    public ICommand RescanCommand { get; }

    public string ModelSizeText => ModelDownloader.Format(Model.SizeBytes);
    public string DownloadButtonText => L.T($"Baixar IA local ({ModelSizeText})");
    public string ModelSourceText => L.T($"{Model.Publisher} · licença {Model.License} · {Model.Url.Host}");
    public string ModelsDir => Ai.ModelsDir;

    public double ProgressPercent => (Ai.Progress?.Fraction ?? 0) * 100;
    public bool IsVerifying => Ai.Progress?.Verifying == true;
    public bool ShowDownloadButton => !Ai.IsDownloading && !Ai.HasModelFile;

    /// <summary>Liga/desliga o uso da IA local (o modelo continua na pasta).</summary>
    public bool IsAiEnabled
    {
        get => Ai.IsEnabled;
        set
        {
            if (value == Ai.IsEnabled) return;
            _ = Ai.SetEnabledAsync(value);
            OnPropertyChanged();
        }
    }

    public string UserName
    {
        get => _userName;
        set
        {
            var clean = (value ?? "").Trim();
            if (clean.Length > MaxNameLength) clean = clean[..MaxNameLength];
            if (clean == _services.Settings.UserName)
            {
                SetProperty(ref _userName, clean);
                return;
            }
            _userName = clean;
            OnPropertyChanged();
            _services.Settings.UserName = clean;
            _services.SaveSettings();
            _services.Logger.Info("Nome do usuário alterado nas configurações.");
            Message = clean.Length == 0 ? L.T("Prontinho! Vou falar com você sem usar nome ♡") : L.T($"Prontinho! Agora vou te chamar de {clean} ♡");
        }
    }

    /// <summary>Resultado da última ação (erro de download, nome salvo…).</summary>
    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    private void OnAiChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LocalAiService.Progress):
                OnPropertyChanged(nameof(ProgressPercent));
                OnPropertyChanged(nameof(IsVerifying));
                break;
            case nameof(LocalAiService.State):
            case nameof(LocalAiService.HasModelFile):
            case nameof(LocalAiService.IsEnabled):
                OnPropertyChanged(nameof(ShowDownloadButton));
                OnPropertyChanged(nameof(IsAiEnabled));
                CommandManager.InvalidateRequerySuggested();
                break;
        }
    }

    private async Task DownloadAsync()
    {
        var free = FreeSpace(ModelsDir);
        var notes = new List<string>
        {
            L.T($"Origem: repositório oficial da Qwen no Hugging Face ({Model.Url.Host}). Licença {Model.License}."),
            L.T($"Destino: {Path.Combine(ModelsDir, Model.FileName)}"),
            L.T("O arquivo é conferido pelo SHA-256 oficial; se não bater, ele é apagado e não é usado."),
            L.T("A IA roda só neste PC (na CPU), sem internet, e responde apenas com a base de conhecimento do Maid Installer."),
            L.T("Ela não instala, remove nem executa nada."),
        };
        if (free is { } f) notes.Add(L.T($"Espaço livre nesse disco: {ModelDownloader.Format(f)}."));
        var ok = await _main.ShowDialogAsync(new DialogViewModel(L.T("Baixar a IA local da Maid?"),
            L.T($"{L.Name(_services.Settings.UserName)}, vou baixar o modelo abaixo. Você pode cancelar a qualquer momento."),
            [new DialogItem(Model.DisplayName, $"{ModelSizeText} · {Model.Publisher}")], notes, L.T("Baixar")));
        if (!ok) return;

        Message = null;
        _services.Logger.Info("Usuário confirmou o download do modelo de IA.");
        var error = await Ai.DownloadAsync();
        Message = error ?? (Ai.IsReady ? L.T("IA local instalada e ativa! ♡ Pode testar no chat da tela Início.") : Ai.StatusText);
    }

    private async Task DeleteAsync()
    {
        var file = Ai.ModelFileName;
        if (file is null) return;
        var size = Ai.ModelPath is { } p && File.Exists(p) ? ModelDownloader.Format(new FileInfo(p).Length) : "";
        var ok = await _main.ShowDialogAsync(new DialogViewModel(L.T("Remover a IA local?"),
            L.T("O arquivo do modelo será apagado da pasta Models. A Maid continua respondendo com a base de conhecimento."),
            [new DialogItem(file, size)], [L.T("Você pode baixar de novo quando quiser.")], L.T("Remover")));
        if (!ok) return;
        Message = Ai.DeleteModel() ?? L.T("IA local removida. A Maid segue respondendo pela base local ♡");
    }

    private void OpenModelsFolder() => OpenFolder(ModelsDir);

    private static long? FreeSpace(string dir)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(dir));
            return root is null ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
