using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using MaidInstaller.Core.Storage;

namespace MaidInstaller.Core.Localization;

/// <summary>
/// Tradução do Maid Installer. O português é o idioma do código: cada texto em português é a própria chave,
/// e <c>Localization/en.json</c> traz o inglês. Sem tradução, o texto aparece em português (nunca some).
///
/// Uso: <c>L.T("Instalar")</c> e, para frases com valores, <c>L.T($"Instalando o {app.Name}…")</c>.
/// No segundo caso, a chave é a frase com os valores trocados por {0}, {1}… (ex.: "Instalando o {0}…"),
/// e a tradução pode mudar a ordem deles. O nome do usuário entra com <c>{L.Name(nome)}</c> e vira "{nome}"
/// na chave; se o nome estiver vazio, o vocativo some da frase (veja <see cref="Address"/>).
/// </summary>
public static class L
{
    public const string Portuguese = "pt-BR";
    public const string English = "en";
    public const string Auto = "auto";
    public const string NameToken = "{nome}";

    private static readonly CultureInfo PtCulture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly CultureInfo EnCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly AsyncLocal<string?> ScopedLanguage = new();
    private static readonly ConcurrentDictionary<string, byte> Missing = new();
    private static Dictionary<string, string> _english = [];
    private static string _language = Portuguese;

    /// <summary>Idioma em uso ("pt-BR" ou "en").</summary>
    public static string Language => ScopedLanguage.Value ?? _language;
    public static bool IsEnglish => Language == English;
    public static CultureInfo Culture => IsEnglish ? EnCulture : PtCulture;

    /// <summary>Textos pedidos em inglês que não estão no dicionário (para diagnóstico e testes).</summary>
    public static IReadOnlyCollection<string> MissingKeys => [.. Missing.Keys];
    public static int DictionarySize => _english.Count;

    /// <summary>Configura o idioma do app (uma vez, ao abrir). <paramref name="setting"/> é "auto", "pt-BR" ou "en".</summary>
    public static void Initialize(string? setting, string? englishDictionaryPath)
    {
        if (englishDictionaryPath is not null) LoadEnglish(englishDictionaryPath);
        _language = Resolve(setting);
    }

    public static void LoadEnglish(string path)
    {
        var dict = JsonStore.Read<Dictionary<string, string>>(path) ?? [];
        _english = new Dictionary<string, string>(dict, StringComparer.Ordinal);
    }

    /// <summary>"auto" segue o idioma do Windows: português → pt-BR; qualquer outro → inglês.</summary>
    public static string Resolve(string? setting, CultureInfo? windowsUi = null) => setting switch
    {
        English => English,
        Portuguese => Portuguese,
        _ => (windowsUi ?? CultureInfo.InstalledUICulture).TwoLetterISOLanguageName == "pt" ? Portuguese : English,
    };

    /// <summary>Troca o idioma só no fluxo atual (usado nos testes, que rodam em paralelo).</summary>
    public static IDisposable Use(string language)
    {
        var previous = ScopedLanguage.Value;
        ScopedLanguage.Value = language;
        return new Scope(() => ScopedLanguage.Value = previous);
    }

    /// <summary>Traduz um texto fixo.</summary>
    public static string T(string portuguese)
    {
        if (!IsEnglish || string.IsNullOrEmpty(portuguese)) return portuguese;
        if (_english.TryGetValue(portuguese, out var en)) return en;
        Missing.TryAdd(portuguese, 0);
        return portuguese;
    }

    /// <summary>Traduz uma frase com valores (<c>$"..."</c>).</summary>
    public static string T(ref LText text) => text.Build();

    /// <summary>Marca o nome do usuário numa frase: <c>L.T($"Oii, {L.Name(nome)}!")</c>.</summary>
    public static UserName Name(string? name) => new(name);

    /// <summary>Plural simples: "1 arquivo" / "3 arquivos" (as duas formas são traduzidas).</summary>
    public static string Plural(int n, string one, string many) =>
        n == 1 ? $"1 {T(one)}" : $"{n.ToString("N0", Culture)} {T(many)}";

    /// <summary>
    /// Coloca o nome do usuário no lugar de <paramref name="token"/>. Sem nome, o vocativo sai da frase:
    /// "Oii, {nome}!" → "Oii!", "Hmm, {nome}, ainda…" → "Hmm, ainda…", "{nome}, estou…" → "Estou…".
    /// </summary>
    public static string Address(string text, string? name, string token = NameToken)
    {
        if (!text.Contains(token, StringComparison.Ordinal)) return text;
        if (!string.IsNullOrWhiteSpace(name)) return text.Replace(token, name.Trim(), StringComparison.Ordinal);

        text = text.Replace(", " + token, "", StringComparison.Ordinal);
        if (text.StartsWith(token + ", ", StringComparison.Ordinal))
        {
            var rest = text[(token.Length + 2)..];
            text = rest.Length == 0 ? rest : char.ToUpper(rest[0], Culture) + rest[1..];
        }
        text = text.Replace(token + ", ", "", StringComparison.Ordinal)
                   .Replace(" " + token, "", StringComparison.Ordinal)
                   .Replace(token, "", StringComparison.Ordinal);
        return text;
    }

    /// <summary>
    /// O mesmo texto em todos os idiomas: o português e a tradução (funciona a partir de qualquer um dos dois).
    /// Serve para reconhecer um texto padrão que foi gravado em outro idioma.
    /// </summary>
    public static IReadOnlySet<string> Variants(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal) { text };
        if (_english.TryGetValue(text, out var en)) set.Add(en);
        foreach (var (pt, value) in _english)
            if (value == text) set.Add(pt);
        return set;
    }

    internal static string Lookup(string key)
    {
        if (!IsEnglish) return key;
        if (_english.TryGetValue(key, out var en)) return en;
        Missing.TryAdd(key, 0);
        return key;
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

/// <summary>O nome do usuário dentro de uma frase traduzível.</summary>
public readonly record struct UserName(string? Value);

/// <summary>
/// Monta a chave de uma frase <c>$"..."</c>: o texto fixo fica igual e cada valor vira {0}, {1}…
/// (com o formato, se houver: {0:dd/MM/yyyy}). O nome do usuário vira "{nome}".
/// </summary>
[InterpolatedStringHandler]
public ref struct LText
{
    // Marca interna do nome: string.Format não aceita "{nome}", então ele passa por fora.
    private const string NameMark = "\u0001";
    private readonly StringBuilder _key;
    private readonly List<object?> _args;
    private string? _name;

    public LText(int literalLength, int formattedCount)
    {
        _key = new StringBuilder(literalLength + formattedCount * 4);
        _args = new List<object?>(formattedCount);
        _name = null;
    }

    public void AppendLiteral(string value) => _key.Append(value.Replace("{", "{{").Replace("}", "}}"));

    public void AppendFormatted<T>(T value) => AppendFormatted(value, null);

    public void AppendFormatted<T>(T value, string? format)
    {
        if (value is UserName name)
        {
            _key.Append(L.NameToken);
            _name = name.Value;
            return;
        }
        _key.Append('{').Append(_args.Count.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(format)) _key.Append(':').Append(format);
        _key.Append('}');
        _args.Add(value);
    }

    /// <summary>A chave como aparece no dicionário (ex.: "Instalando o {0}…").</summary>
    public readonly string Key => _key.ToString();

    public readonly string Build()
    {
        var template = L.Lookup(Key).Replace(L.NameToken, NameMark, StringComparison.Ordinal);
        string text;
        try
        {
            text = string.Format(L.Culture, template, [.. _args]);
        }
        catch (FormatException)
        {
            // Tradução com {n} errado: usa o português, que sempre bate com os valores.
            text = string.Format(L.Culture, Key.Replace(L.NameToken, NameMark, StringComparison.Ordinal), [.. _args]);
        }
        return L.Address(text, _name, NameMark);
    }
}
