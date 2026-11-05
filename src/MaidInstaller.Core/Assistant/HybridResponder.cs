using System.Text;
using System.Text.RegularExpressions;

namespace MaidInstaller.Core.Assistant;

/// <summary>Prompt de chat (formato neutro; o motor converte para o template do modelo).</summary>
public sealed record ChatPrompt(string System, string User);

/// <summary>Um modelo de linguagem local. Só gera texto: não tem acesso a nada do sistema.</summary>
public interface IChatModel
{
    string DisplayName { get; }
    IAsyncEnumerable<string> GenerateAsync(ChatPrompt prompt, CancellationToken cancellationToken);
}

public enum AnswerSource
{
    /// <summary>Dado exato (código de erro, preço, link, saudação, tópico revisado).</summary>
    Rules,
    /// <summary>Resposta pré-programada montada com a base local (sem IA).</summary>
    KnowledgeBase,
    /// <summary>Gerada pela IA local a partir dos fatos da base.</summary>
    Model,
    /// <summary>A IA respondeu algo fora dos fatos; foi trocada pela resposta da base.</summary>
    ModelRejected,
}

public sealed record AssistantAnswer(string Text, AnswerSource Source);

/// <summary>
/// Decide como responder: dados exatos e "não tenho essa informação" vêm das regras;
/// explicações, comparações e categorias usam a IA local quando ela está disponível,
/// SEMPRE limitada aos fatos encontrados na base. Sem IA, tudo continua funcionando.
/// </summary>
public sealed class HybridResponder(KnowledgeBase knowledge, Func<string> userName, Logging.IAppLogger? logger = null)
{
    private readonly OfflineResponder _offline = new(knowledge, userName);

    public OfflineResponder Offline => _offline;

    /// <summary>IA carregada (null = só base local).</summary>
    public IChatModel? Model { get; set; }

    public AnswerPlan Plan(string question, bool modelAvailable)
    {
        var r = _offline.Retriever.Retrieve(question);
        if (r.NormalizedQuestion.Length == 0 || _offline.DirectAnswer(question, r) is not null)
            return new AnswerPlan(_offline.Answer(question, r), AnswerSource.Rules, null, "");

        var offline = _offline.Answer(question, r);
        if (!modelAvailable)
            return new AnswerPlan(offline, AnswerSource.KnowledgeBase, null, "");

        var facts = PromptBuilder.BuildFacts(r);
        if (facts.Length == 0)
            return new AnswerPlan(offline, AnswerSource.KnowledgeBase, null, "");
        return new AnswerPlan(offline, AnswerSource.Model, PromptBuilder.Build(userName(), facts, question), facts);
    }

    /// <summary>
    /// Responde à pergunta. <paramref name="onPartial"/> recebe o texto parcial enquanto a IA escreve.
    /// </summary>
    public async Task<AssistantAnswer> AnswerAsync(string question, Action<string>? onPartial = null, CancellationToken cancellationToken = default)
    {
        var model = Model;
        var plan = Plan(question, model is not null);
        if (plan.Prompt is null || model is null)
            return new AssistantAnswer(plan.Fallback, plan.Source);

        var sb = new StringBuilder();
        try
        {
            await foreach (var piece in model.GenerateAsync(plan.Prompt, cancellationToken).ConfigureAwait(false))
            {
                sb.Append(piece);
                onPartial?.Invoke(AnswerGuard.Clean(sb.ToString()));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.Error("Falha ao gerar resposta com a IA local; usando a base.", ex);
            return new AssistantAnswer(plan.Fallback, AnswerSource.ModelRejected);
        }

        var cleaned = AnswerGuard.Clean(sb.ToString());
        // Só os fatos, a pergunta e o nome contam como apoio — não o texto das instruções do prompt
        // (no teste real, "não é mais disponibilizado pela Biblioteca" passava por usar palavras das instruções).
        var text = AnswerGuard.Ground(cleaned, plan.Facts + "\n" + question + "\n" + userName(), out var dropped);
        if (dropped > 0)
            logger?.Warn($"IA: {dropped} frase(s) sem apoio nos fatos removida(s). Pergunta: {question}");
        var problem = AnswerGuard.Check(text, plan.Facts, question);
        if (problem is not null)
        {
            logger?.Warn($"Resposta da IA descartada ({problem}); usando a base. Pergunta: {question}");
            return new AssistantAnswer(plan.Fallback, AnswerSource.ModelRejected);
        }
        return new AssistantAnswer(text, AnswerSource.Model);
    }
}

/// <param name="Fallback">Resposta da base (usada sem IA, ou se a IA falhar).</param>
/// <param name="Prompt">Prompt para a IA; null quando a resposta é a da base.</param>
public sealed record AnswerPlan(string Fallback, AnswerSource Source, ChatPrompt? Prompt, string Facts);

/// <summary>Monta o prompt com a personalidade da Maid e SOMENTE os fatos encontrados.</summary>
public static class PromptBuilder
{
    /// <summary>
    /// Limite de caracteres para os fatos. O contexto é de 1024 tokens; com o texto fixo,
    /// a pergunta e ~200 tokens de resposta, sobra com folga para ~1600 caracteres em português.
    /// </summary>
    public const int MaxFactsChars = 1600;
    public const int MaxQuestionChars = 300;

    public static ChatPrompt Build(string userName, string facts, string question)
    {
        var hasName = !string.IsNullOrWhiteSpace(userName);
        // O prompt não passa pelo dicionário: é escrito inteiro em cada idioma, para o modelo responder no idioma do app.
        var system = L.IsEnglish
            ? "You are Maid Assistant, the assistant of the Maid Installer app. Speak English, " +
              "in a warm, polite, friendly, slightly playful, natural and objective way." +
              (hasName ? $" The user's name is {userName.Trim()}." : "") + "\n" +
              "Rules:\n" +
              "- Use ONLY the FACTS below. If the answer is not in the facts, say that this information is not on record yet.\n" +
              "- Do not make up versions, prices, numbers, links, features or requirements.\n" +
              "- You do not install, remove, update or run anything. To install, the user uses the Maid Installer Library.\n" +
              "- Answer in up to 4 short sentences. You may end with ♡.\n\n" +
              "FACTS:\n" + facts
            : "Você é a Maid Assistant, a assistente do aplicativo Maid Installer. Fale em português do Brasil, " +
              "de forma carinhosa, educada, simpática, levemente divertida, natural e objetiva." +
              (hasName ? $" O usuário se chama {userName.Trim()}." : "") + "\n" +
              "Regras:\n" +
              "- Use SOMENTE os FATOS abaixo. Se a resposta não estiver nos fatos, diga que essa informação ainda não está cadastrada.\n" +
              "- Não invente versões, preços, números, links, recursos nem requisitos.\n" +
              "- Você não instala, remove, atualiza nem executa nada. Para instalar, o usuário usa a Biblioteca do Maid Installer.\n" +
              "- Responda em até 4 frases curtas. Pode terminar com ♡.\n\n" +
              "FATOS:\n" + facts;
        var q = question.Trim();
        if (q.Length > MaxQuestionChars) q = q[..MaxQuestionChars];
        return new ChatPrompt(system, q);
    }

    public static string BuildFacts(Retrieval r)
    {
        var lines = new List<string>();
        foreach (var app in r.Apps.Take(3))
            lines.Add(AppFacts(app, detailed: r.Apps.Count == 1));
        if (r.Apps.Count == 0)
            foreach (var app in r.CategoryApps.Take(8))
                lines.Add(L.T($"- {app.Name} ({L.T(app.Category)}): {FirstSentence(app.Summary)} Preço: {app.License}."));
        if (r.Topic is not null)
            lines.Add("- " + L.Address(r.Topic.Answer, L.T("o usuário")));

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (sb.Length + line.Length + 1 > MaxFactsChars) break;
            sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd();
    }

    private static string AppFacts(AppKnowledge app, bool detailed)
    {
        var sb = new StringBuilder(L.T($"- {app.Name} (categoria: {L.T(app.Category)}). {app.Summary}"));
        if (app.Highlights.Count > 0)
            sb.Append(L.T(" Recursos: ")).Append(string.Join("; ", detailed ? app.Highlights : app.Highlights.Take(2))).Append('.');
        sb.Append(L.T($" Desenvolvedor: {app.Developer}. Preço/licença: {app.License}."));
        if (detailed && !string.IsNullOrWhiteSpace(app.Notes)) sb.Append(' ').Append(app.Notes);
        if (app.HasWinGetPackage is true) sb.Append(L.T(" Pode ser instalado pela Biblioteca do Maid Installer."));
        if (app.HasWinGetPackage is false) sb.Append(L.T(" Não tem pacote no WinGet: instala-se pelo site oficial."));
        return sb.ToString();
    }

    private static string FirstSentence(string text)
    {
        var i = text.IndexOf(". ", StringComparison.Ordinal);
        return i < 0 ? text : text[..(i + 1)];
    }
}

/// <summary>
/// Conferência da resposta da IA: se ela citar números, links ou escrever em outro alfabeto
/// (algo que não está nos fatos), a resposta é descartada e a da base é usada.
/// </summary>
public static partial class AnswerGuard
{
    private static readonly string[] StopTokens = ["<|im_end|>", "<|im_start|>", "<|endoftext|>"];

    public static string Clean(string raw)
    {
        var text = raw;
        foreach (var stop in StopTokens)
        {
            var i = text.IndexOf(stop, StringComparison.Ordinal);
            if (i >= 0) text = text[..i];
        }
        // Um "<|" solto no fim é o começo de um token especial ainda chegando.
        var partial = text.LastIndexOf("<|", StringComparison.Ordinal);
        if (partial >= 0 && text.IndexOf("|>", partial, StringComparison.Ordinal) < 0) text = text[..partial];
        return text.Trim();
    }

    /// <summary>Null se a resposta é aceitável; senão, o motivo.</summary>
    public static string? Check(string answer, string facts, string question)
    {
        if (answer.Length < 8) return "resposta vazia";
        if (answer.Any(IsForeignScript)) return "outro alfabeto";

        var allowed = facts + "\n" + question;
        foreach (Match url in UrlRegex().Matches(answer))
            if (!allowed.Contains(url.Value.TrimEnd('.', ',', ')', '!'), StringComparison.OrdinalIgnoreCase))
                return "link fora dos fatos";

        var withoutListMarkers = ListMarkerRegex().Replace(answer, "");
        foreach (Match number in NumberRegex().Matches(withoutListMarkers))
            if (!allowed.Contains(number.Value, StringComparison.Ordinal))
                return $"número fora dos fatos ({number.Value})";
        return null;
    }

    /// <summary>
    /// Tira as frases que falam de coisas que não estão nos fatos (o modelo pequeno às vezes "completa" com invenções).
    /// Uma frase fica se a maior parte das palavras de conteúdo dela aparece no texto permitido (fatos + pergunta).
    /// </summary>
    public static string Ground(string answer, string allowedText, out int dropped)
    {
        var allowed = WordsOf(allowedText).ToHashSet();
        var stems = allowed.Where(w => w.Length >= StemLength).Select(w => w[..StemLength]).ToHashSet();
        var kept = new List<string>();
        dropped = 0;
        foreach (var sentence in SentenceRegex().Split(answer.Trim()))
        {
            if (sentence.Length == 0) continue;
            var words = WordsOf(sentence).Where(w => !StopWords.Contains(w)).ToList();
            var known = words.Count(w => allowed.Contains(w) || (w.Length >= StemLength && stems.Contains(w[..StemLength])));
            if (words.Count <= 1 || known >= words.Count * MinKnownRatio) kept.Add(sentence);
            else
            {
                dropped++;
                // Sem a 1ª frase (a principal), o resto fica sem sujeito ("Ele tem…"): melhor usar a base.
                if (kept.Count == 0) return "";
            }
        }
        return string.Join(" ", kept).Trim();
    }

    private const int StemLength = 5;          // "gravar" ~ "gravação", "instalar" ~ "instalação"
    private const double MinKnownRatio = 0.7;

    private static IEnumerable<string> WordsOf(string text) =>
        KnowledgeRetriever.Normalize(text).Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 4);

    private static readonly char[] WordSeparators = [' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?', '(', ')', '"', '\'', '-', '/', '♡', '*'];

    // Palavras comuns que não carregam fato nenhum (já sem acento, como sai do Normalize).
    private static readonly HashSet<string> StopWords =
    [
        "para", "como", "mais", "muito", "muita", "tambem", "esse", "essa", "isso", "este", "esta", "isto", "voce", "pode", "podem",
        "entre", "enquanto", "ambos", "ambas", "alem", "disso", "algumas", "alguns", "outro", "outra", "outros", "outras", "quando",
        "onde", "porque", "sobre", "todo", "toda", "todos", "todas", "mas", "pelo", "pela", "pelos", "pelas", "seus", "suas",
        "eles", "elas", "ainda", "entao", "assim", "apenas", "sempre", "nunca", "aqui", "dele", "dela", "deles", "delas", "qual",
        "quais", "ser", "sao", "esta", "estao", "fica", "ficam", "tem", "existe", "voces", "nosso", "nossa", "mesmo", "mesma",
        "cada", "qualquer", "bastante", "bom", "boa", "otimo", "otima", "util", "uteis", "ideal", "opcao", "caso", "serve",
        "usar", "usado", "usada", "fazer", "feito", "precisa", "quer", "querer", "ajuda", "ajudar", "claro", "certo", "dica",
        // Inglês
        "this", "that", "these", "those", "with", "from", "your", "yours", "have", "also", "more", "very", "what", "which",
        "when", "where", "they", "them", "their", "there", "then", "than", "into", "like", "just", "only", "some", "such",
        "each", "other", "others", "about", "because", "while", "both", "even", "does", "will", "would", "could", "should",
        "been", "being", "here", "help", "helps", "great", "good", "best", "useful", "used", "using", "make", "makes",
        "made", "need", "needs", "want", "sure", "option", "lets", "allows", "well", "many", "much", "most", "really",
        // O nome do próprio app aparece em todo lugar e não sustenta fato nenhum.
        "maid", "installer", "assistant",
    ];

    [GeneratedRegex(@"(?<=[.!?♡])\s+")]
    private static partial Regex SentenceRegex();

    private static bool IsForeignScript(char c) =>
        c is >= 'Ѐ' and <= 'ۿ'      // cirílico, hebraico, árabe
          or >= '฀' and <= '๿'      // tailandês
          or >= '぀' and <= '鿿'      // japonês, chinês
          or >= '가' and <= '힯'      // coreano
          or >= '＀' and <= '￯';     // formas de largura total

    [GeneratedRegex(@"https?://\S+|www\.\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\d+(?:[.,]\d+)*")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"(?m)^\s*\d{1,2}[.)]\s")]
    private static partial Regex ListMarkerRegex();
}
