using System.Runtime.CompilerServices;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using MaidInstaller.Core.Assistant;
using MaidInstaller.Core.Logging;

namespace MaidInstaller.AI;

/// <summary>
/// IA local da Maid Assistant: um modelo GGUF rodando no llama.cpp (via LLamaSharp), só na CPU.
/// Contexto de 1024 tokens. O modelo apenas gera texto; ele não tem acesso a comandos,
/// arquivos nem instalações — tudo isso continua passando pelo motor do Maid Installer.
/// </summary>
public sealed class LlamaChatModel : IChatModel, IDisposable
{
    public const uint ContextSize = 1024;
    public const int MaxAnswerTokens = 200;
    public const int MinAnswerTokens = 48;

    private static readonly string[] AntiPrompts = ["<|im_end|>", "<|im_start|>", "<|endoftext|>"];
    private static int _nativeConfigured;

    private readonly LLamaWeights _weights;
    private readonly ModelParams _params;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IAppLogger _logger;

    private LlamaChatModel(LLamaWeights weights, ModelParams parameters, string path, IAppLogger logger)
    {
        _weights = weights;
        _params = parameters;
        _logger = logger;
        FilePath = path;
        DisplayName = Path.GetFileNameWithoutExtension(path);
    }

    public string DisplayName { get; }
    public string FilePath { get; }

    /// <summary>Carrega o modelo (pode levar alguns segundos). Lança exceção se o arquivo for inválido.</summary>
    public static async Task<LlamaChatModel> LoadAsync(string path, IAppLogger logger, CancellationToken cancellationToken = default)
    {
        ConfigureNative(logger);
        var parameters = new ModelParams(path)
        {
            ContextSize = ContextSize,
            GpuLayerCount = 0,
            Threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 8),
            BatchSize = 512,
        };
        var weights = await LLamaWeights.LoadFromFileAsync(parameters, cancellationToken).ConfigureAwait(false);
        logger.Info($"IA local carregada: {Path.GetFileName(path)} ({weights.SizeInBytes / 1024 / 1024} MB, {weights.ParameterCount:N0} parâmetros, contexto {ContextSize}).");
        return new LlamaChatModel(weights, parameters, path, logger);
    }

    /// <summary>Formato de chat ChatML (usado pelo Qwen).</summary>
    public static string ToChatMl(ChatPrompt prompt) =>
        $"<|im_start|>system\n{prompt.System}<|im_end|>\n<|im_start|>user\n{prompt.User}<|im_end|>\n<|im_start|>assistant\n";

    public async IAsyncEnumerable<string> GenerateAsync(ChatPrompt prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var text = ToChatMl(prompt);
            var promptTokens = _weights.Tokenize(text, add_bos: true, special: true, Encoding.UTF8).Length;
            var room = (int)ContextSize - promptTokens - 8;
            if (room < MinAnswerTokens)
                throw new InvalidOperationException($"Prompt grande demais para o contexto ({promptTokens} tokens).");

            using var sampler = new DefaultSamplingPipeline
            {
                Temperature = 0.3f,
                TopP = 0.9f,
                TopK = 40,
                RepeatPenalty = 1.1f,
            };
            var inference = new InferenceParams
            {
                MaxTokens = Math.Min(MaxAnswerTokens, room),
                AntiPrompts = AntiPrompts,
                SamplingPipeline = sampler,
            };
            var executor = new StatelessExecutor(_weights, _params) { ApplyTemplate = false };
            await foreach (var piece in executor.InferAsync(text, inference, cancellationToken).ConfigureAwait(false))
                yield return piece;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Usa só a CPU (com o melhor conjunto AVX disponível) e manda avisos do llama.cpp para o log.</summary>
    private static void ConfigureNative(IAppLogger logger)
    {
        if (Interlocked.Exchange(ref _nativeConfigured, 1) == 1) return;
        try
        {
            NativeLibraryConfig.All
                .WithCuda(false)
                .WithVulkan(false)
                .WithAutoFallback(true)
                .WithLogCallback((level, message) =>
                {
                    if (level is LLamaLogLevel.Error) logger.Warn("llama.cpp: " + message.TrimEnd());
                });
        }
        catch (Exception ex)
        {
            // Já configurado (a biblioteca nativa carrega uma vez por processo).
            logger.Warn($"Configuração do llama.cpp ignorada: {ex.Message}");
        }
    }

    public void Dispose()
    {
        // Espera uma resposta em andamento terminar (ou ser cancelada) antes de liberar o modelo.
        var acquired = _gate.Wait(TimeSpan.FromSeconds(10));
        _weights.Dispose();
        if (acquired) _gate.Release();
        _gate.Dispose();
        _logger.Info("IA local descarregada.");
    }
}
