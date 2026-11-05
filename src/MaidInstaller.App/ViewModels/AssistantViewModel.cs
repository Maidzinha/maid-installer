using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using MaidInstaller.App.Mvvm;
using MaidInstaller.App.Services;
using MaidInstaller.Core.Assistant;

namespace MaidInstaller.App.ViewModels;

/// <summary>Uma mensagem da conversa. O texto muda enquanto a IA local escreve.</summary>
public sealed class ChatMessage(string text, bool fromUser) : ObservableObject
{
    private string _text = text;
    private bool _isStreaming;
    private string? _sourceNote;

    public bool FromUser { get; } = fromUser;

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set => SetProperty(ref _isStreaming, value);
    }

    /// <summary>De onde veio a resposta (mostrado como dica ao passar o mouse).</summary>
    public string? SourceNote
    {
        get => _sourceNote;
        set => SetProperty(ref _sourceNote, value);
    }
}

/// <summary>
/// Painel da Maid Assistant. Responde pela base local e, se o modelo estiver instalado,
/// pela IA local limitada aos fatos da base. A assistente nunca executa comandos:
/// apenas conversa e recebe avisos do app via <see cref="Post"/>.
/// </summary>
public sealed class AssistantViewModel : ObservableObject
{
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(90);

    private readonly AppServices _services;
    private string _input = "";
    private bool _isMinimized;
    private bool _isTyping;

    public AssistantViewModel(AppServices services)
    {
        _services = services;
        _isMinimized = services.Settings.AssistantMinimized;
        var name = services.Settings.UserName;

        Messages.Add(new ChatMessage(
            L.T($"Oii, {L.Name(name)}! ♡\nEu sou a Maid Assistant, sua ajudante aqui no Maid Installer!\n\n") +
            L.T("Posso te explicar para que serve cada aplicativo, qual é o mais indicado, ou tirar dúvidas sobre downloads e instalações."),
            fromUser: false));

        Suggestions = [L.T("Para que serve o Discord?"), L.T("Quais apps são essenciais?"), L.T("Qual navegador escolher?")];

        SendCommand = new RelayCommand(() => _ = AskAsync(Input), () => !string.IsNullOrWhiteSpace(Input) && !IsTyping);
        AskCommand = new RelayCommand(p => { if (p is string q) _ = AskAsync(q); }, _ => !IsTyping);
        ToggleMinimizedCommand = new RelayCommand(() => IsMinimized = !IsMinimized);

        services.Ai.PropertyChanged += OnAiChanged;
    }

    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public IReadOnlyList<string> Suggestions { get; }
    public ICommand SendCommand { get; }
    public ICommand AskCommand { get; }
    public ICommand ToggleMinimizedCommand { get; }

    /// <summary>Indica honestamente o que está respondendo.</summary>
    public string StatusText => _services.Ai.AssistantStatus;

    public string StatusTooltip => _services.Ai.IsReady
        ? L.T("A IA local roda só neste PC e responde usando apenas a base de conhecimento do Maid Installer.")
        : L.T("Respondendo com a base de conhecimento local. A IA local (opcional) pode ser baixada em Configurações.");

    public string Input
    {
        get => _input;
        set => SetProperty(ref _input, value);
    }

    public bool IsTyping
    {
        get => _isTyping;
        private set => SetProperty(ref _isTyping, value);
    }

    public bool IsMinimized
    {
        get => _isMinimized;
        set
        {
            if (!SetProperty(ref _isMinimized, value)) return;
            _services.Settings.AssistantMinimized = value;
            _services.SaveSettings();
        }
    }

    private void OnAiChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LocalAiService.AssistantStatus) or nameof(LocalAiService.State))
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusTooltip));
        }
    }

    /// <summary>Mensagem iniciada pelo app (ex.: status de instalação).</summary>
    public void Post(string text, bool expand = true)
    {
        Messages.Add(new ChatMessage(text, fromUser: false));
        if (expand) IsMinimized = false;
    }

    /// <summary>Fornece o estado real da fila de instalação (null = nada na fila).</summary>
    public Func<string?>? InstallStatusProvider { get; set; }

    private static bool AsksAboutInstalls(string question)
    {
        var q = OfflineResponder.Normalize(question);
        return q.Contains("instalando") || q.Contains("fila") ||
               (q.Contains("instala") && (q.Contains("como esta") || q.Contains("como vai") || q.Contains("andamento") ||
                                          q.Contains("status") || q.Contains("progresso") || q.Contains("terminou")));
    }

    private async Task AskAsync(string question)
    {
        question = question.Trim();
        if (question.Length == 0 || IsTyping) return;
        Input = "";
        Messages.Add(new ChatMessage(question, fromUser: true));
        IsTyping = true;
        CommandManager.InvalidateRequerySuggested();

        var reply = new ChatMessage("…", fromUser: false) { IsStreaming = true };
        Messages.Add(reply);
        var finished = false;
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        try
        {
            if (AsksAboutInstalls(question) && InstallStatusProvider?.Invoke() is { } status)
            {
                await Task.Delay(300); // pequena pausa para a conversa parecer natural
                reply.Text = status;
                reply.SourceNote = L.T("Estado real da fila de instalação");
                return;
            }

            using var cts = new CancellationTokenSource(AnswerTimeout);
            var answer = await _services.Responder.AnswerAsync(question,
                partial => dispatcher.BeginInvoke(() =>
                {
                    if (!finished && partial.Length > 0) reply.Text = partial;
                }),
                cts.Token);
            if (answer.Source is AnswerSource.Rules or AnswerSource.KnowledgeBase)
                await Task.Delay(300);
            finished = true;
            reply.Text = answer.Text;
            reply.SourceNote = answer.Source switch
            {
                AnswerSource.Model => L.T("Resposta da IA local, com base nos fatos da base de conhecimento"),
                AnswerSource.ModelRejected => L.T("Resposta da base de conhecimento (a da IA local foi descartada por não bater com os fatos)"),
                _ => L.T("Resposta da base de conhecimento local"),
            };
        }
        catch (OperationCanceledException)
        {
            finished = true;
            reply.Text = _services.Responder.Offline.Answer(question);
            reply.SourceNote = L.T("A IA local demorou demais; resposta da base de conhecimento");
        }
        catch (Exception ex)
        {
            finished = true;
            _services.Logger.Error("Falha na Maid Assistant.", ex);
            reply.Text = L.T("Ops, me enrolei aqui. Pode perguntar de novo? ♡");
        }
        finally
        {
            finished = true;
            reply.IsStreaming = false;
            IsTyping = false;
            CommandManager.InvalidateRequerySuggested(); // reativa as sugestões na hora
        }
    }
}
