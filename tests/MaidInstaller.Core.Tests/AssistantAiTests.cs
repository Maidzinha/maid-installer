using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using MaidInstaller.Core.Assistant;
using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Logging;

namespace MaidInstaller.Core.Tests;

/// <summary>Modelo falso: devolve um texto fixo em pedaços e guarda o prompt recebido.</summary>
internal sealed class FakeChatModel(params string[] pieces) : IChatModel
{
    public ChatPrompt? LastPrompt { get; private set; }
    public int Calls { get; private set; }
    public string DisplayName => "fake";

    public async IAsyncEnumerable<string> GenerateAsync(ChatPrompt prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastPrompt = prompt;
        Calls++;
        foreach (var p in pieces)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return p;
        }
    }
}

public class AssistantAiTests
{
    private static readonly string CatalogDir = Path.Combine(AppContext.BaseDirectory, "Catalog");

    private static KnowledgeBase LoadKb()
    {
        var kb = KnowledgeBase.Load(Path.Combine(CatalogDir, "knowledge.json"));
        kb.AddFromCatalog(CatalogLoader.Load(Path.Combine(CatalogDir, "catalog.json"), NullLogger.Instance));
        return kb;
    }

    private static readonly KnowledgeBase Kb = LoadKb();

    private static HybridResponder Responder(IChatModel? model = null) => new(Kb, () => "Helder") { Model = model };

    // ---------- Busca na base ----------

    [Fact]
    public void Retriever_finds_apps_in_question_order()
    {
        var r = new KnowledgeRetriever(Kb).Retrieve("Qual a diferença entre o Opera GX e o Chrome?");
        Assert.Equal(["operagx", "chrome"], r.Apps.Select(a => a.Id).ToArray());
    }

    [Fact]
    public void Retriever_finds_category_apps()
    {
        var r = new KnowledgeRetriever(Kb).Retrieve("Qual navegador escolher?");
        Assert.Equal("Navegadores", r.Category);
        Assert.Contains(r.CategoryApps, a => a.Id == "firefox");
        Assert.All(r.CategoryApps, a => Assert.Equal("Navegadores", a.Category));
    }

    [Fact]
    public void Every_catalog_app_is_in_the_knowledge_base_with_package_info()
    {
        Assert.True(Kb.Apps.Count >= 39);
        Assert.All(Kb.Apps, a => Assert.NotNull(a.HasWinGetPackage));
    }

    // ---------- Respostas sem IA ----------

    [Theory]
    [InlineData("deu o erro 0x8A150061", "Já estava instalado")]
    [InlineData("o que é o código 0x8a150105?", "espaço suficiente")]
    [InlineData("apareceu -1978335135 aqui", "0x8A150061")]
    public void Known_winget_error_codes_are_explained(string question, string expected) =>
        Assert.Contains(expected, Responder().Offline.Answer(question));

    [Fact]
    public void Unknown_error_code_is_not_invented()
    {
        var answer = Responder().Offline.Answer("o que é 0x8A15FFFF?");
        Assert.Contains("Não tenho o código 0x8A15FFFF cadastrado", answer);
    }

    [Fact]
    public void Error_code_parser_accepts_hex_and_negative_decimal()
    {
        Assert.Equal(unchecked((int)0x8A150061), OfflineResponder.FindErrorCode("0x8A150061"));
        Assert.Equal(unchecked((int)0x8A150061), OfflineResponder.FindErrorCode("erro -1978335135."));
        Assert.Null(OfflineResponder.FindErrorCode("versão 1.29.380"));
        Assert.Null(OfflineResponder.FindErrorCode("tenho 16 GB"));
    }

    [Fact]
    public void Link_answer_uses_official_url_from_catalog()
    {
        var answer = Responder().Offline.Answer("onde baixo o Discord?");
        Assert.Contains("https://discord.com", answer);
        Assert.Contains("Biblioteca", answer);
    }

    [Fact]
    public void Category_answer_lists_catalog_apps()
    {
        var answer = Responder().Offline.Answer("qual navegador escolher?");
        Assert.Contains("Mozilla Firefox", answer);
        Assert.Contains("Brave", answer);
    }

    [Theory]
    [InlineData("o que fazer se faltar MSVCP140.dll?", "Visual C++ Redistributable")]
    [InlineData("vai pedir administrador?", "UAC")]
    [InlineData("vai reiniciar meu pc?", "nunca reinicio")]
    [InlineData("como desinstalar um programa?", "não desinstala")]
    public void New_topics_answer(string question, string expected) =>
        Assert.Contains(expected, Responder().Offline.Answer(question));

    // ---------- Plano híbrido ----------

    [Theory]
    [InlineData("O OBS é grátis?")]
    [InlineData("deu erro 0x8A150105")]
    [InlineData("oii")]
    [InlineData("Qual a capital da Mongólia?")]
    [InlineData("vai pedir administrador?")]
    public void Exact_data_and_unknowns_never_use_the_model(string question)
    {
        var model = new FakeChatModel("qualquer coisa");
        var plan = Responder(model).Plan(question, modelAvailable: true);
        Assert.Null(plan.Prompt);
        Assert.Equal(AnswerSource.Rules, plan.Source);
    }

    [Fact]
    public async Task Unknown_subject_answers_not_on_record_even_with_model()
    {
        var model = new FakeChatModel("Ulan Bator!");
        var answer = await Responder(model).AnswerAsync("Qual a capital da Mongólia?");
        Assert.Contains("não tenho essa informação", answer.Text);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public void Prompt_contains_only_retrieved_facts_and_fits_the_context()
    {
        var plan = Responder(new FakeChatModel()).Plan("Me conta sobre o Discord e o WhatsApp", modelAvailable: true);
        Assert.NotNull(plan.Prompt);
        Assert.Contains("Discord Inc.", plan.Prompt!.System);
        Assert.Contains("WhatsApp", plan.Prompt.System);
        Assert.DoesNotContain("Steam", plan.Prompt.System);
        Assert.Contains("SOMENTE os FATOS", plan.Prompt.System);
        Assert.Contains("Helder", plan.Prompt.System);
        // 1024 tokens de contexto: o prompt inteiro fica bem abaixo de ~2800 caracteres.
        Assert.True(plan.Prompt.System.Length + plan.Prompt.User.Length < 2800, $"prompt com {plan.Prompt.System.Length} caracteres");
    }

    [Fact]
    public void Category_prompt_is_trimmed_to_budget()
    {
        var facts = PromptBuilder.BuildFacts(new KnowledgeRetriever(Kb).Retrieve("quais drivers e componentes eu preciso?"));
        Assert.True(facts.Length <= PromptBuilder.MaxFactsChars);
        Assert.NotEmpty(facts);
    }

    [Fact]
    public async Task Model_answer_is_streamed_and_returned()
    {
        var model = new FakeChatModel("Oii, Helder! ", "O Discord é para conversar ", "por texto, voz e vídeo ♡", "<|im_end|>");
        var partials = new List<string>();
        var answer = await Responder(model).AnswerAsync("Para que serve o Discord?", partials.Add);
        Assert.Equal(AnswerSource.Model, answer.Source);
        Assert.Equal("Oii, Helder! O Discord é para conversar por texto, voz e vídeo ♡", answer.Text);
        Assert.True(partials.Count >= 3);
        Assert.DoesNotContain(partials, p => p.Contains("<|"));
    }

    [Theory]
    [InlineData("O Discord custa R$ 25 por mês.")]                 // número que não está nos fatos
    [InlineData("Baixe em https://discord-gratis.example.com")]     // link inventado
    [InlineData("Discord 是一个聊天应用")]                           // outro alfabeto
    [InlineData("")]                                                // vazio
    public async Task Invented_model_answers_fall_back_to_knowledge_base(string invented)
    {
        var answer = await Responder(new FakeChatModel(invented)).AnswerAsync("Para que serve o Discord?");
        Assert.Equal(AnswerSource.ModelRejected, answer.Source);
        Assert.Contains("Discord Inc.", answer.Text); // a ficha da base
    }

    [Fact]
    public async Task Without_model_the_offline_answer_is_used()
    {
        var answer = await Responder(model: null).AnswerAsync("Para que serve o Discord?");
        Assert.Equal(AnswerSource.KnowledgeBase, answer.Source);
        Assert.Contains("Discord Inc.", answer.Text);
    }

    [Fact]
    public void Guard_accepts_numbers_and_links_from_facts()
    {
        const string facts = "- 7-Zip. Site https://www.7-zip.org. Windows 10 e 11.";
        Assert.Null(AnswerGuard.Check("O 7-Zip roda no Windows 10 e 11. Site: https://www.7-zip.org.", facts, "?"));
        Assert.Null(AnswerGuard.Check("Opções:\n1. 7-Zip\n2. WinRAR", facts, "?"));
    }

    [Fact]
    public async Task Comparison_comes_ready_from_the_knowledge_base()
    {
        // Teste real com o Qwen 0.5B: na comparação ele misturou os fatos dos dois apps e inventou coisas.
        var model = new FakeChatModel("O Spotify é gratuito para usuários do sistema operacional Maid Installer.");
        var answer = await Responder(model).AnswerAsync("Qual a diferença entre o VLC e o Spotify?");
        Assert.Equal(AnswerSource.Rules, answer.Source);
        Assert.Equal(0, model.Calls);
        Assert.Contains("VLC", answer.Text);
        Assert.Contains("Spotify", answer.Text);
    }

    [Fact]
    public void Ground_removes_sentences_not_backed_by_facts()
    {
        const string facts = "- VLC Media Player (VideoLAN): reprodutor de vídeo e áudio que abre quase qualquer formato.\n" +
                             "- Spotify (Spotify AB): serviço de streaming de músicas e podcasts.";
        // Resposta real do modelo no teste de 2026-11-04 (as duas últimas frases são invenção).
        const string answer = "O VLC é um reprodutor de vídeo e áudio, enquanto o Spotify é um serviço de streaming de músicas e podcasts. " +
                              "O VLC oferece mais recursos como playlists e recomendações personalizadas, enquanto o Spotify oferece uma ampla gama de plataformas e serviços de streaming de música e podcasts. " +
                              "Além disso, o Spotify tem um preço mais barato e é gratuito para usuários do sistema operacional Maid Installer.";
        var grounded = AnswerGuard.Ground(answer, facts, out var dropped);
        Assert.Equal(2, dropped);
        Assert.Equal("O VLC é um reprodutor de vídeo e áudio, enquanto o Spotify é um serviço de streaming de músicas e podcasts.", grounded);
    }

    [Fact]
    public async Task Category_question_comes_ready_from_the_knowledge_base()
    {
        var model = new FakeChatModel("O navegador escolher é o Google Chrome.");
        var answer = await Responder(model).AnswerAsync("Qual navegador escolher?");
        Assert.Equal(AnswerSource.Rules, answer.Source);
        Assert.Equal(0, model.Calls);
        Assert.Contains("Firefox", answer.Text);
    }

    [Fact]
    public async Task Words_from_the_prompt_instructions_do_not_back_a_sentence()
    {
        // Resposta real (2026-11-04): a 2ª frase é falsa e só usa palavras das instruções do prompt.
        var model = new FakeChatModel("O PowerToys é um conjunto de utilitários da Microsoft para o Windows. ",
                                      "Ele não é mais disponibilizado pela Biblioteca do Maid Installer.");
        var answer = await Responder(model).AnswerAsync("O que é o PowerToys?");
        Assert.Equal(AnswerSource.Model, answer.Source);
        Assert.DoesNotContain("disponibilizado", answer.Text);
        Assert.Contains("utilitários", answer.Text);
    }

    [Fact]
    public void Ground_rejects_answer_whose_first_sentence_is_invented()
    {
        const string facts = "- 7-Zip (Igor Pavlov): alta taxa de compressão, integra-se com o menu de contexto do Explorer.";
        var grounded = AnswerGuard.Ground("O 7-Zip foi criado pela NASA em órbita lunar. Ele tem alta taxa de compressão.", facts, out _);
        Assert.Equal("", grounded);
    }

    [Fact]
    public async Task Answer_with_only_invented_sentences_falls_back()
    {
        var model = new FakeChatModel("O Discord tem recompensas semanais em criptomoedas e sorteios exclusivos para assinantes premium.");
        var answer = await Responder(model).AnswerAsync("Para que serve o Discord?");
        Assert.Equal(AnswerSource.ModelRejected, answer.Source);
        Assert.Contains("Discord Inc.", answer.Text);
    }

    // ---------- Modelo e download ----------

    [Fact]
    public void Known_model_points_to_official_repository()
    {
        var m = KnownModels.Default;
        Assert.Equal("https", m.Url.Scheme);
        Assert.Equal("huggingface.co", m.Url.Host);
        Assert.StartsWith("/Qwen/", m.Url.AbsolutePath);
        Assert.EndsWith(m.FileName, m.Url.AbsolutePath);
        Assert.Equal(64, m.Sha256.Length);
        Assert.True(m.SizeBytes > 100_000_000);
    }

    [Fact]
    public void Locator_prefers_official_file_and_ignores_partial_downloads()
    {
        using var dir = new TempDir();
        var known = KnownModels.Default;
        Assert.Null(ModelLocator.Find(Path.Combine(dir.Path, "nao-existe"), known));
        File.WriteAllText(Path.Combine(dir.Path, known.FileName + ".part"), "x");
        Assert.Null(ModelLocator.Find(dir.Path, known));
        File.WriteAllText(Path.Combine(dir.Path, "outro.gguf"), "x");
        Assert.EndsWith("outro.gguf", ModelLocator.Find(dir.Path, known));
        File.WriteAllText(Path.Combine(dir.Path, known.FileName), "x");
        Assert.EndsWith(known.FileName, ModelLocator.Find(dir.Path, known));
    }

    private sealed class FakeHandler(byte[] body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestedUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
        }
    }

    private static ModelInfo TestModel(byte[] body, string? sha = null) => KnownModels.Default with
    {
        FileName = "teste.gguf",
        SizeBytes = body.Length,
        Sha256 = sha ?? Convert.ToHexStringLower(SHA256.HashData(body)),
    };

    [Fact]
    public async Task Download_verifies_hash_and_reports_real_progress()
    {
        using var dir = new TempDir();
        var body = new byte[3 * 1024 * 1024 + 123];
        Random.Shared.NextBytes(body);
        var handler = new FakeHandler(body);
        var reports = new List<DownloadProgress>();
        var progress = new SyncProgress(reports.Add);

        var path = await new ModelDownloader(new HttpClient(handler), NullLogger.Instance)
            .DownloadAsync(TestModel(body), dir.Path, progress, CancellationToken.None);

        Assert.Equal(KnownModels.Default.Url, handler.RequestedUri);
        Assert.Equal(body, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".part"));
        Assert.Contains(reports, r => r.BytesReceived == body.Length && r.Verifying);
        Assert.True(reports.Select(r => r.BytesReceived).SequenceEqual(reports.Select(r => r.BytesReceived).Order()));
    }

    [Fact]
    public async Task Download_with_wrong_hash_is_deleted()
    {
        using var dir = new TempDir();
        var body = new byte[4096];
        var model = TestModel(body, sha: new string('0', 64));
        var ex = await Assert.ThrowsAsync<ModelDownloadException>(() =>
            new ModelDownloader(new HttpClient(new FakeHandler(body)), NullLogger.Instance).DownloadAsync(model, dir.Path, null, CancellationToken.None));
        Assert.Contains("SHA-256", ex.Message);
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public async Task Download_with_wrong_size_is_refused()
    {
        using var dir = new TempDir();
        var body = new byte[4096];
        var model = TestModel(body) with { SizeBytes = 9999 };
        await Assert.ThrowsAsync<ModelDownloadException>(() =>
            new ModelDownloader(new HttpClient(new FakeHandler(body)), NullLogger.Instance).DownloadAsync(model, dir.Path, null, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public async Task Download_refuses_non_official_url_and_low_disk_space()
    {
        using var dir = new TempDir();
        var body = new byte[16];
        var http = new HttpClient(new FakeHandler(body));
        var evil = TestModel(body) with { Url = new Uri("https://example.com/modelo.gguf") };
        await Assert.ThrowsAsync<ModelDownloadException>(() =>
            new ModelDownloader(http, NullLogger.Instance).DownloadAsync(evil, dir.Path, null, CancellationToken.None));

        var ex = await Assert.ThrowsAsync<ModelDownloadException>(() =>
            new ModelDownloader(http, NullLogger.Instance, _ => 1024).DownloadAsync(TestModel(body), dir.Path, null, CancellationToken.None));
        Assert.Contains("Espaço insuficiente", ex.Message);
    }

    [Fact]
    public async Task Server_error_is_reported()
    {
        using var dir = new TempDir();
        var body = new byte[16];
        var ex = await Assert.ThrowsAsync<ModelDownloadException>(() =>
            new ModelDownloader(new HttpClient(new FakeHandler(body, HttpStatusCode.NotFound)), NullLogger.Instance)
                .DownloadAsync(TestModel(body), dir.Path, null, CancellationToken.None));
        Assert.Contains("404", ex.Message);
    }

    private sealed class SyncProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
