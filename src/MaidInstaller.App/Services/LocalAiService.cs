using System.Net.Http;
using System.IO;
using MaidInstaller.AI;
using MaidInstaller.App.Mvvm;
using MaidInstaller.Core.Assistant;
using MaidInstaller.Core.Logging;
using MaidInstaller.Core.Settings;
using MaidInstaller.Core.Storage;

namespace MaidInstaller.App.Services;

public enum AiState { NotInstalled, Disabled, Loading, Ready, Downloading, Error }

/// <summary>
/// Cuida da IA local opcional: encontra, carrega, baixa (com confirmação feita pela tela) e remove o modelo.
/// Sem modelo, a Maid Assistant continua respondendo pela base local.
/// </summary>
public sealed class LocalAiService : ObservableObject, IDisposable
{
    private static readonly HttpClient Http = CreateHttp();

    private readonly AppPaths _paths;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly HybridResponder _responder;
    private readonly IAppLogger _logger;
    private LlamaChatModel? _model;
    private CancellationTokenSource? _downloadCts;
    private AiState _state = AiState.NotInstalled;
    private string? _error;
    private string? _modelPath;
    private DownloadProgress? _progress;

    public LocalAiService(AppPaths paths, AppSettings settings, Action saveSettings, HybridResponder responder, IAppLogger logger)
    {
        _paths = paths;
        _settings = settings;
        _saveSettings = saveSettings;
        _responder = responder;
        _logger = logger;
        _modelPath = ModelLocator.Find(paths.ModelsDir, Model);
        _state = _modelPath is null ? AiState.NotInstalled : settings.AssistantAiEnabled ? AiState.Loading : AiState.Disabled;
    }

    public ModelInfo Model => KnownModels.Default;
    public string ModelsDir => _paths.ModelsDir;

    public AiState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value)) return;
            OnPropertyChanged(nameof(IsReady));
            OnPropertyChanged(nameof(IsDownloading));
            OnPropertyChanged(nameof(HasModelFile));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(AssistantStatus));
        }
    }

    public bool IsReady => State == AiState.Ready;
    public bool IsDownloading => State == AiState.Downloading;
    public bool HasModelFile => _modelPath is not null && State != AiState.Downloading;
    public bool IsEnabled => _settings.AssistantAiEnabled;
    public string? ModelPath => _modelPath;
    public string? ModelFileName => _modelPath is null ? null : Path.GetFileName(_modelPath);
    public bool IsOfficialModel => _modelPath is not null && Path.GetFileName(_modelPath).Equals(Model.FileName, StringComparison.OrdinalIgnoreCase);

    public string? Error
    {
        get => _error;
        private set { if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(StatusText)); }
    }

    public DownloadProgress? Progress
    {
        get => _progress;
        private set { if (SetProperty(ref _progress, value)) OnPropertyChanged(nameof(ProgressText)); }
    }

    public string ProgressText => Progress switch
    {
        null => "",
        { Verifying: true } => L.T("Conferindo o SHA-256 do arquivo…"),
        var p => L.T($"{ModelDownloader.Format(p.BytesReceived)} de {ModelDownloader.Format(p.TotalBytes)} ({p.Fraction:P0})"),
    };

    /// <summary>Texto curto ao lado do nome da assistente (sempre verdadeiro).</summary>
    public string AssistantStatus => State switch
    {
        AiState.Ready => L.T("Online · IA local (") + (_model?.DisplayName ?? "modelo") + ")",
        AiState.Loading => L.T("Online · carregando a IA local…"),
        _ => L.T("Online · base local"),
    };

    public string StatusText => State switch
    {
        AiState.NotInstalled => L.T("A IA local não está instalada. A Maid responde com a base de conhecimento."),
        AiState.Disabled => L.T("A IA local está desligada. A Maid responde com a base de conhecimento."),
        AiState.Loading => L.T("Carregando a IA local…"),
        AiState.Ready => L.T($"IA local ativa: {ModelFileName} (roda só neste PC, sem internet)."),
        AiState.Downloading => L.T("Baixando o modelo…"),
        AiState.Error => L.T("A IA local não pôde ser usada: ") + (Error ?? L.T("erro desconhecido")) + L.T(" A Maid responde com a base de conhecimento."),
        _ => "",
    };

    /// <summary>Carrega o modelo na inicialização, se ele existir e estiver ligado.</summary>
    public Task InitializeAsync() => _modelPath is not null && _settings.AssistantAiEnabled ? LoadAsync() : Task.CompletedTask;

    private async Task LoadAsync()
    {
        if (_modelPath is null) return;
        Unload();
        State = AiState.Loading;
        Error = null;
        try
        {
            var path = _modelPath;
            var model = await Task.Run(() => LlamaChatModel.LoadAsync(path, _logger));
            _model = model;
            _responder.Model = model;
            State = AiState.Ready;
        }
        catch (Exception ex)
        {
            _logger.Error($"Não foi possível carregar a IA local ({_modelPath}).", ex);
            Error = L.T("o arquivo do modelo não abriu (pode estar corrompido ou ser de um formato incompatível).");
            State = AiState.Error;
        }
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        if (_settings.AssistantAiEnabled == enabled) return;
        _settings.AssistantAiEnabled = enabled;
        _saveSettings();
        _logger.Info(enabled ? "IA local ligada pelo usuário." : "IA local desligada pelo usuário.");
        OnPropertyChanged(nameof(IsEnabled));
        if (enabled)
        {
            if (_modelPath is not null) await LoadAsync();
        }
        else
        {
            Unload();
            State = _modelPath is null ? AiState.NotInstalled : AiState.Disabled;
        }
    }

    /// <summary>Baixa o modelo oficial (a confirmação é pedida antes, pela tela de Configurações).</summary>
    public async Task<string?> DownloadAsync()
    {
        if (IsDownloading) return null;
        _downloadCts = new CancellationTokenSource();
        var previous = State;
        State = AiState.Downloading;
        Error = null;
        Progress = new DownloadProgress(0, Model.SizeBytes);
        var progress = new Progress<DownloadProgress>(p => Progress = p); // criado na thread da tela
        try
        {
            var downloader = new ModelDownloader(Http, _logger);
            var path = await Task.Run(() => downloader.DownloadAsync(Model, _paths.ModelsDir, progress, _downloadCts.Token));
            _modelPath = path;
            OnPropertyChanged(nameof(ModelPath));
            OnPropertyChanged(nameof(ModelFileName));
            OnPropertyChanged(nameof(IsOfficialModel));
            if (!_settings.AssistantAiEnabled)
            {
                _settings.AssistantAiEnabled = true;
                _saveSettings();
                OnPropertyChanged(nameof(IsEnabled));
            }
            await LoadAsync();
            return null;
        }
        catch (OperationCanceledException)
        {
            State = previous == AiState.Downloading ? AiState.NotInstalled : previous;
            return L.T("Download cancelado. Nada foi instalado.");
        }
        catch (ModelDownloadException ex)
        {
            State = previous == AiState.Downloading ? AiState.NotInstalled : previous;
            return ex.Message;
        }
        finally
        {
            Progress = null;
            _downloadCts.Dispose();
            _downloadCts = null;
            OnPropertyChanged(nameof(HasModelFile));
        }
    }

    public void CancelDownload() => _downloadCts?.Cancel();

    /// <summary>Descarrega e apaga o arquivo do modelo (a confirmação é pedida antes, pela tela).</summary>
    public string? DeleteModel()
    {
        if (_modelPath is null) return null;
        Unload();
        var path = _modelPath;
        try
        {
            File.Delete(path);
            _logger.Info($"Modelo de IA removido pelo usuário: {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error($"Não foi possível apagar o modelo: {path}", ex);
            if (_settings.AssistantAiEnabled) _ = LoadAsync();
            return L.T("Não consegui apagar o arquivo do modelo. Ele pode estar em uso ou protegido.");
        }
        _modelPath = ModelLocator.Find(_paths.ModelsDir, Model);
        OnPropertyChanged(nameof(ModelPath));
        OnPropertyChanged(nameof(ModelFileName));
        OnPropertyChanged(nameof(IsOfficialModel));
        Error = null;
        State = _modelPath is null ? AiState.NotInstalled : AiState.Disabled;
        if (_modelPath is not null && _settings.AssistantAiEnabled) _ = LoadAsync();
        return null;
    }

    /// <summary>Procura de novo na pasta Models (ex.: o usuário colocou um .gguf manualmente).</summary>
    public async Task RescanAsync()
    {
        if (IsDownloading) return;
        var found = ModelLocator.Find(_paths.ModelsDir, Model);
        if (found == _modelPath && State is AiState.Ready or AiState.Loading) return;
        Unload();
        _modelPath = found;
        OnPropertyChanged(nameof(ModelPath));
        OnPropertyChanged(nameof(ModelFileName));
        OnPropertyChanged(nameof(IsOfficialModel));
        if (found is null) State = AiState.NotInstalled;
        else if (_settings.AssistantAiEnabled) await LoadAsync();
        else State = AiState.Disabled;
        OnPropertyChanged(nameof(HasModelFile));
    }

    private void Unload()
    {
        _responder.Model = null;
        var model = _model;
        _model = null;
        model?.Dispose();
    }

    public void Dispose()
    {
        _downloadCts?.Cancel();
        Unload();
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MaidInstaller/1.0");
        return http;
    }
}
