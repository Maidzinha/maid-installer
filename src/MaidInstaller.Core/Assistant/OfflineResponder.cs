using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MaidInstaller.Core.Installation;

namespace MaidInstaller.Core.Assistant;

public interface IAssistantResponder
{
    string Answer(string question);
}

/// <summary>
/// Respostas pré-programadas da Maid Assistant, funcionando sem o modelo de IA.
/// Só usa fatos da <see cref="KnowledgeBase"/> (e da tabela de códigos do WinGet);
/// quando não encontra, diz que não tem a informação. Nunca inventa.
/// As palavras-chave valem em português e em inglês (já sem acento, como sai do Normalize).
/// </summary>
public sealed partial class OfflineResponder(KnowledgeBase knowledge, Func<string> userName) : IAssistantResponder
{
    internal static readonly string[] Greetings =
        ["oi", "oii", "oiii", "ola", "eai", "e ai", "bom dia", "boa tarde", "boa noite",
         "hello", "hey", "hi", "hii", "good morning", "good afternoon", "good evening"];
    internal static readonly string[] Thanks =
        ["obrigado", "obrigada", "valeu", "vlw", "brigado", "brigada", "thanks", "thank you", "thx", "ty"];
    internal static readonly string[] PriceWords =
        ["gratis", "gratuito", "gratuita", "pago", "paga", "preco", "custa", "valor", "licenca",
         "free", "price", "cost", "costs", "paid", "license", "how much"];
    internal static readonly string[] DeveloperWords =
        ["quem fez", "quem faz", "desenvolvedor", "desenvolvedora", "empresa", "fabricante", "criador",
         "who made", "who makes", "who created", "who develops", "developer", "company", "maker", "publisher"];
    internal static readonly string[] CompareWords =
        ["diferenca", "melhor", "comparar", "compara", "versus", "vs", "ou",
         "difference", "better", "compare", "or"];
    internal static readonly string[] LinkWords =
        ["site", "link", "baixar", "download", "onde baixo", "pagina oficial", "website", "official page", "where can i get"];

    private readonly KnowledgeRetriever _retriever = new(knowledge);

    public KnowledgeRetriever Retriever => _retriever;

    public string Answer(string question) => Answer(question, _retriever.Retrieve(question));

    /// <summary>Resposta sem IA para uma pergunta já analisada.</summary>
    public string Answer(string question, Retrieval r)
    {
        var q = r.NormalizedQuestion;
        if (q.Length == 0)
            return L.T($"Pode perguntar, {L.Name(userName())}! Sei falar sobre os aplicativos do Maid Installer ♡");

        if (DirectAnswer(question, r) is { } direct)
            return direct;

        var apps = r.Apps;
        if (apps.Count >= 2 && KnowledgeRetriever.ContainsAny(q, CompareWords))
            return Compare(apps);
        if (apps.Count >= 1)
            return Describe(apps[0]);

        if (r.Topic is not null)
            return TopicAnswer(r.Topic);

        if (r.CategoryApps.Count > 0)
            return CategoryAnswer(r.Category!, r.CategoryApps);

        return NotOnRecord();
    }

    /// <summary>
    /// Respostas que vêm direto de dados exatos (código de erro, preço, desenvolvedor, link,
    /// saudação). Nesses casos a IA não é usada: o dado já está pronto e não pode mudar.
    /// </summary>
    public string? DirectAnswer(string question, Retrieval r)
    {
        var name = userName();
        var q = r.NormalizedQuestion;

        if (FindErrorCode(question) is { } code)
            return ErrorCodeAnswer(code);

        var apps = r.Apps;
        // Comparação: o modelo pequeno mistura os fatos de um app com os do outro (visto no teste real),
        // então a resposta vem pronta da base, lado a lado.
        if (apps.Count >= 2 && KnowledgeRetriever.ContainsAny(q, CompareWords))
            return Compare(apps);
        if (apps.Count == 0 && KnowledgeRetriever.ContainsAny(q, Thanks))
            return L.T($"De nada, {L.Name(name)}! Estou sempre por aqui ♡");

        if (apps.Count == 0 && IsOnlyGreeting(q))
            return L.T($"Oii, {L.Name(name)}! ♡ Quer que eu explique para que serve algum aplicativo?");

        // "Qual navegador escolher?": a lista vem pronta da base. No teste real o modelo pequeno
        // escolheu um app sem motivo nenhum nos fatos.
        if (apps.Count == 0 && r.Topic is null && r.CategoryApps.Count > 0)
            return CategoryAnswer(r.Category!, r.CategoryApps);

        if (apps.Count == 1)
        {
            var app = apps[0];
            if (KnowledgeRetriever.ContainsAny(q, PriceWords))
                return $"{app.Name}: {app.License}.";
            if (KnowledgeRetriever.ContainsAny(q, DeveloperWords))
                return L.T($"O {app.Name} é desenvolvido por {app.Developer}.");
            if (KnowledgeRetriever.ContainsAny(q, LinkWords))
                return LinkAnswer(app);
        }

        // Tópico sem app citado: o texto da base já é a resposta revisada.
        if (apps.Count == 0 && r.Topic is not null)
            return TopicAnswer(r.Topic);

        if (r.IsEmpty)
            return NotOnRecord();

        return null;
    }

    public string NotOnRecord() =>
        L.T($"Hmm, {L.Name(userName())}, ainda não tenho essa informação cadastrada na minha base. Posso falar sobre os aplicativos do catálogo, como Discord, Steam ou 7-Zip ♡");

    private string TopicAnswer(TopicKnowledge topic) => L.Address(topic.Answer, userName());

    private static string LinkAnswer(AppKnowledge app)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(app.OfficialUrl))
            sb.Append(L.T($"O site oficial do {app.Name} é {app.OfficialUrl}"));
        else
            sb.Append(L.T($"Não tenho o site oficial do {app.Name} cadastrado"));
        sb.Append(app.HasWinGetPackage switch
        {
            true => L.T(". Você também pode instalar pela Biblioteca do Maid Installer, que baixa da fonte oficial pelo WinGet ♡"),
            false => L.T(". Ele não tem pacote no WinGet, então a instalação é pelo site oficial ♡"),
            null => " ♡",
        });
        return sb.ToString();
    }

    private string ErrorCodeAnswer(int code)
    {
        var hex = WinGetExitCodes.Hex(code);
        var info = WinGetExitCodes.Describe(code);
        if (!info.Known)
            return L.T($"Não tenho o código {hex} cadastrado, {L.Name(userName())}. Quando uma instalação falha, a tela de Instalações mostra a explicação que o próprio WinGet dá para o código (e o botão \"Ver log\" abre os detalhes) ♡");
        var tip = info.Outcome switch
        {
            InstallOutcome.Failed => L.T(" Depois de resolver, é só usar \"Tentar novamente\" na tela de Instalações ♡"),
            InstallOutcome.Cancelled => L.T(" Nada foi instalado. Se quiser, pode tentar de novo ♡"),
            _ => " ♡",
        };
        return L.T($"O código {hex} do WinGet quer dizer: {info.Message}{tip}");
    }

    /// <summary>Encontra um código de erro do WinGet na pergunta (0x8A150061 ou -1978335135).</summary>
    public static int? FindErrorCode(string question)
    {
        var hex = HexCodeRegex().Match(question);
        if (hex.Success && uint.TryParse(hex.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u))
            return unchecked((int)u);
        var dec = DecimalCodeRegex().Match(question);
        if (dec.Success && long.TryParse(dec.Value, CultureInfo.InvariantCulture, out var l) && l >= int.MinValue && l <= uint.MaxValue)
            return unchecked((int)l);
        return null;
    }

    [GeneratedRegex(@"\b0[xX]([0-9a-fA-F]{8})\b")]
    private static partial Regex HexCodeRegex();

    [GeneratedRegex(@"(?<![\w.,])-\d{9,10}\b")]
    private static partial Regex DecimalCodeRegex();

    internal static string Describe(AppKnowledge app)
    {
        var sb = new StringBuilder();
        sb.Append(app.Summary);
        if (app.Highlights.Count > 0)
        {
            sb.AppendLine().AppendLine();
            foreach (var h in app.Highlights) sb.Append("• ").AppendLine(h);
        }
        else
        {
            sb.AppendLine();
        }
        sb.AppendLine().Append(L.T($"Desenvolvedor: {app.Developer} · {app.License}"));
        if (!string.IsNullOrWhiteSpace(app.Notes)) sb.AppendLine().Append(app.Notes);
        return sb.ToString().TrimEnd();
    }

    private static string Compare(IReadOnlyList<AppKnowledge> apps)
    {
        var sb = new StringBuilder(L.T("Vamos comparar! ♡")).AppendLine();
        foreach (var app in apps.Take(3))
            sb.AppendLine().Append($"• {app.Name} ({L.T(app.Category)}): {app.Summary} — {app.License}.");
        return sb.ToString();
    }

    private string CategoryAnswer(string category, IReadOnlyList<AppKnowledge> apps)
    {
        var sb = new StringBuilder(L.T($"Em {L.T(category)}, {L.Name(userName())}, o catálogo tem:")).AppendLine().AppendLine();
        foreach (var app in apps.Take(8))
            sb.Append("• ").Append(app.Name).Append(" — ").AppendLine(app.License);
        sb.AppendLine().Append(L.T("Quer que eu explique algum deles? ♡"));
        return sb.ToString();
    }

    private static bool IsOnlyGreeting(string q) =>
        Greetings.Any(g => q == g || q.StartsWith(g + " ", StringComparison.Ordinal) && q.Split(' ').Length <= 3);

    /// <summary>Minúsculas, sem acentos, só letras/números separados por um espaço.</summary>
    public static string Normalize(string text) => KnowledgeRetriever.Normalize(text);
}
