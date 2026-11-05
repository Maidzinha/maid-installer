using System.Globalization;
using System.Text;

namespace MaidInstaller.Core.Assistant;

/// <summary>
/// Busca na <see cref="KnowledgeBase"/> os fatos ligados a uma pergunta: apps citados,
/// tópico e categoria. É o "R" do RAG: o modelo de IA só recebe o que for encontrado aqui.
/// </summary>
public sealed class KnowledgeRetriever(KnowledgeBase knowledge)
{
    /// <summary>
    /// Palavras (português e inglês) que indicam cada categoria. A categoria é o nome em português do catálogo,
    /// que é a chave interna; na tela ela aparece traduzida.
    /// </summary>
    private static readonly (string Category, string[] Words)[] CategoryWords =
    [
        ("Navegadores", ["navegador", "navegadores", "browser", "navegar", "browsers", "web browser"]),
        ("Comunicação", ["comunicacao", "conversar", "mensagem", "mensagens", "chamada", "chamadas", "chat",
                         "communication", "messaging", "message", "messages", "call", "calls"]),
        ("Jogos e plataformas", ["jogo", "jogos", "game", "games", "jogar", "launcher", "gaming"]),
        ("Utilitários", ["utilitario", "utilitarios", "compactar", "compactador", "descompactar", "extrair", "captura de tela", "print",
                         "utility", "utilities", "compress", "unzip", "extract", "screenshot", "screenshots"]),
        ("Multimídia", ["multimidia", "musica", "musicas", "video", "videos", "gravar", "gravacao", "transmitir", "live", "audio", "player",
                        "multimedia", "music", "record", "recording", "stream", "streaming"]),
        ("Desenvolvimento", ["desenvolvimento", "programar", "programacao", "codigo", "programador", "dev",
                             "development", "programming", "coding", "code editor"]),
        ("Drivers e componentes", ["driver", "drivers", "placa de video", "runtime", "runtimes", "componente", "componentes",
                                   "graphics card", "component", "components"]),
        ("Escritório", ["escritorio", "documento", "documentos", "planilha", "planilhas", "pdf", "trabalho",
                        "office", "document", "documents", "spreadsheet", "spreadsheets"]),
    ];

    public KnowledgeBase Knowledge => knowledge;

    public Retrieval Retrieve(string question)
    {
        var q = Normalize(question);
        var apps = FindApps(q);
        var topic = FindTopic(q);
        var category = FindCategory(q);
        var categoryApps = category is null
            ? []
            : knowledge.Apps.Where(a => string.Equals(a.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
        return new Retrieval(q, apps, topic, category, categoryApps);
    }

    /// <summary>Apps citados, na ordem em que aparecem na pergunta.</summary>
    public List<AppKnowledge> FindApps(string normalizedQuestion)
    {
        var padded = " " + normalizedQuestion + " ";
        return knowledge.Apps
            .Select(app => (app, pos: app.Aliases.Append(app.Name)
                .Select(a => Normalize(a))
                .Where(a => a.Length > 0)
                .Select(a => padded.IndexOf(" " + a + " ", StringComparison.Ordinal))
                .Where(p => p >= 0)
                .DefaultIfEmpty(-1)
                .Min()))
            .Where(x => x.pos >= 0)
            .OrderBy(x => x.pos)
            .Select(x => x.app)
            .DistinctBy(a => a.Id)
            .ToList();
    }

    public TopicKnowledge? FindTopic(string normalizedQuestion)
    {
        var padded = " " + normalizedQuestion + " ";
        return knowledge.Topics
            .Select(t => (t, score: t.Keywords.Count(k => padded.Contains(" " + Normalize(k), StringComparison.Ordinal))))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Select(x => x.t)
            .FirstOrDefault();
    }

    public static string? FindCategory(string normalizedQuestion)
    {
        var padded = " " + normalizedQuestion + " ";
        foreach (var (category, words) in CategoryWords)
            if (words.Any(w => padded.Contains(" " + w + " ", StringComparison.Ordinal)))
                return category;
        return null;
    }

    public static bool ContainsAny(string normalizedQuestion, IEnumerable<string> words)
    {
        var padded = " " + normalizedQuestion + " ";
        return words.Any(w => padded.Contains(" " + w + " ", StringComparison.Ordinal));
    }

    /// <summary>Minúsculas, sem acentos, só letras/números separados por um espaço.</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c) || c == '+' || c == '#')
            {
                sb.Append(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }
        return sb.ToString().Trim();
    }
}

/// <summary>O que a base tem sobre a pergunta.</summary>
public sealed record Retrieval(
    string NormalizedQuestion,
    IReadOnlyList<AppKnowledge> Apps,
    TopicKnowledge? Topic,
    string? Category,
    IReadOnlyList<AppKnowledge> CategoryApps)
{
    public bool IsEmpty => Apps.Count == 0 && Topic is null && CategoryApps.Count == 0;
}
