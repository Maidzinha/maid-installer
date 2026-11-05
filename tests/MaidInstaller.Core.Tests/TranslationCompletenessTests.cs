using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MaidInstaller.Core.Assistant;
using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Collections;
using MaidInstaller.Core.Logging;
using MaidInstaller.Core.PostFormat;

namespace MaidInstaller.Core.Tests;

/// <summary>
/// Garante que todo texto de interface tem tradução em en.json, com os mesmos valores ({0}, {nome}…).
/// Varre o código-fonte (L.T, L.Plural, {loc:T '…'}) e os dados (catálogo, base de conhecimento, perfis).
/// </summary>
public partial class TranslationCompletenessTests
{
    private static readonly string Repo = FindRepo();
    private static readonly Dictionary<string, string> English =
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Localization", "en.json")))!;
    private static readonly string CatalogDir = Path.Combine(AppContext.BaseDirectory, "Catalog");

    static TranslationCompletenessTests() =>
        L.LoadEnglish(Path.Combine(AppContext.BaseDirectory, "Localization", "en.json"));

    private static string FindRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "MaidInstaller.slnx"))) return d.FullName;
        throw new DirectoryNotFoundException("Pasta do repositório não encontrada.");
    }

    // ---------- chaves do código ----------

    private static IEnumerable<(string Key, string File)> SourceKeys()
    {
        var src = Path.Combine(Repo, "src");
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            var text = File.ReadAllText(file);
            var name = Path.GetRelativePath(Repo, file);
            foreach (Match m in CallT().Matches(text))
                yield return (ReadKey(text, m.Index + m.Length - (m.Groups[1].Value == "$" ? 2 : 1)), name);
            foreach (Match m in CallPlural().Matches(text))
            {
                yield return (Unescape(m.Groups[1].Value), name);
                yield return (Unescape(m.Groups[2].Value), name);
            }
        }
        foreach (var file in Directory.EnumerateFiles(src, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            foreach (Match m in XamlT().Matches(File.ReadAllText(file)))
                yield return (WebUtility.HtmlDecode(m.Groups[1].Value), Path.GetRelativePath(Repo, file));
        }
    }

    /// <summary>A chave de um literal C# (começando em '"' ou '$'), do mesmo jeito que <see cref="LText"/> monta.</summary>
    private static string ReadKey(string s, int i)
    {
        var interpolated = s[i] == '$';
        var j = i + (interpolated ? 2 : 1);
        var key = new StringBuilder();
        var lit = new StringBuilder();
        var holes = 0;
        void Flush() { key.Append(interpolated ? Unescape(lit.ToString()).Replace("{", "{{").Replace("}", "}}") : Unescape(lit.ToString())); lit.Clear(); }
        while (true)
        {
            var c = s[j];
            if (c == '\\') { lit.Append(s, j, 2); j += 2; continue; }
            if (interpolated && c == '{' && s[j + 1] == '{') { lit.Append('{'); j += 2; continue; }
            if (interpolated && c == '}' && s[j + 1] == '}') { lit.Append('}'); j += 2; continue; }
            if (interpolated && c == '{')
            {
                Flush();
                int depth = 1, k = j + 1;
                while (depth > 0)
                {
                    if (s[k] == '"') { k++; while (s[k] != '"') k += s[k] == '\\' ? 2 : 1; }
                    else if (s[k] == '{') depth++;
                    else if (s[k] == '}') depth--;
                    k++;
                }
                var expr = s[(j + 1)..(k - 1)];
                if (expr.TrimStart().StartsWith("L.Name(", StringComparison.Ordinal)) key.Append(L.NameToken);
                else
                {
                    var format = FormatOf(expr);
                    key.Append('{').Append(holes++).Append(format is null ? "" : ":" + format).Append('}');
                }
                j = k;
                continue;
            }
            if (c == '"') break;
            lit.Append(c);
            j++;
        }
        Flush();
        return key.ToString();
    }

    private static string? FormatOf(string expr)
    {
        var depth = 0;
        for (var i = 0; i < expr.Length; i++)
        {
            var c = expr[i];
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (c == '?' && depth == 0) return null;
            else if (c == ':' && depth == 0) return expr[(i + 1)..];
        }
        return null;
    }

    private static string Unescape(string s) => Regex.Unescape(s.Replace("\\\"", "\""));

    // ---------- chaves dos dados ----------

    private static IEnumerable<(string Key, string File)> DataKeys()
    {
        var catalog = CatalogLoader.Load(Path.Combine(CatalogDir, "catalog.json"), NullLogger.Instance);
        foreach (var c in catalog.Categories) yield return (c.Name, "catalog.json");
        foreach (var a in catalog.Apps)
        {
            yield return (a.Description, $"catalog.json:{a.Id}");
            yield return (a.Price, $"catalog.json:{a.Id}");
            if (!IsLicenseId(a.License)) yield return (a.License, $"catalog.json:{a.Id}");
            if (a.ApproxSize is { } size && !IsLicenseId(size)) yield return (size, $"catalog.json:{a.Id}");
            if (a.Notes is { } n) yield return (n, $"catalog.json:{a.Id}");
        }
        var kb = KnowledgeBase.Load(Path.Combine(CatalogDir, "knowledge.json"));
        foreach (var a in kb.Apps)
        {
            yield return (a.Summary, $"knowledge.json:{a.Id}");
            if (!IsLicenseId(a.License)) yield return (a.License, $"knowledge.json:{a.Id}");
            foreach (var h in a.Highlights) yield return (h, $"knowledge.json:{a.Id}");
            if (a.Notes is { } n) yield return (n, $"knowledge.json:{a.Id}");
        }
        foreach (var t in kb.Topics) yield return (t.Answer, $"knowledge.json:{t.Id}");
        foreach (var tool in AdminTools.All)
        {
            yield return (tool.Name, "AdminTools.cs");
            yield return (tool.Description, "AdminTools.cs");
        }
        using var _ = L.Use(L.Portuguese);
        foreach (var p in DefaultProfiles.Load(Path.Combine(CatalogDir, "profiles.json"), catalog, NullLogger.Instance))
        {
            yield return (p.Name, "profiles.json");
            if (p.Description is { } d) yield return (d, "profiles.json");
        }
    }

    /// <summary>Licenças como "MIT" ou "GPL-2.0" são iguais em todos os idiomas.</summary>
    private static bool IsLicenseId(string text) => !English.ContainsKey(text) && LicenseId().IsMatch(text);

    [Fact]
    public void Every_interface_text_has_an_english_translation()
    {
        var missing = SourceKeys().Concat(DataKeys())
            .Where(k => LetterPattern().IsMatch(k.Key) && !English.ContainsKey(k.Key))
            .Select(k => $"{k.File}: {k.Key}")
            .Distinct()
            .ToList();
        Assert.True(missing.Count == 0, "Sem tradução em en.json:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Translations_keep_the_same_values()
    {
        static string Placeholders(string s) => string.Join(",", Placeholder().Matches(s.Replace("{{", "").Replace("}}", ""))
            .Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal));
        var wrong = English.Where(e => Placeholders(e.Key) != Placeholders(e.Value)).Select(e => $"{e.Key} → {e.Value}").ToList();
        Assert.True(wrong.Count == 0, "Valores diferentes entre o português e o inglês:\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void English_dictionary_has_no_leftover_entries()
    {
        var used = SourceKeys().Concat(DataKeys()).Select(k => k.Key).ToHashSet(StringComparer.Ordinal);
        var unused = English.Keys.Where(k => !used.Contains(k)).ToList();
        Assert.True(unused.Count == 0, "Entradas que ninguém usa em en.json:\n" + string.Join("\n", unused));
    }

    [Fact]
    public void Catalog_knowledge_and_profiles_load_in_english()
    {
        using var _ = L.Use(L.English);
        var catalog = CatalogLoader.Load(Path.Combine(CatalogDir, "catalog.json"), NullLogger.Instance);
        catalog.Localize();
        var discord = catalog.FindApp("discord")!;
        Assert.StartsWith("Text, voice and video", discord.Description);
        Assert.True(discord.IsFree);
        Assert.Equal("Browsers", L.T(catalog.Categories[0].Name));

        var kb = KnowledgeBase.Load(Path.Combine(CatalogDir, "knowledge.json"));
        kb.Localize();
        kb.AddFromCatalog(catalog);
        var answer = new OfflineResponder(kb, () => "").Answer("What is Discord for?");
        Assert.Contains("communication app", answer);
        Assert.DoesNotContain("{nome}", answer);

        var profiles = DefaultProfiles.Load(Path.Combine(CatalogDir, "profiles.json"), catalog, NullLogger.Instance);
        Assert.Contains(profiles, p => p.Name == "Gaming PC");
    }

    [Fact]
    public void Built_in_profiles_saved_in_portuguese_are_relabeled_in_english()
    {
        using var dir = new TempDir();
        var catalog = CatalogLoader.Load(Path.Combine(CatalogDir, "catalog.json"), NullLogger.Instance);
        var store = new CollectionStore(dir.Path, CollectionKind.Profile, NullLogger.Instance);
        IReadOnlyList<AppCollection> pt;
        using (L.Use(L.Portuguese))
        {
            pt = DefaultProfiles.Load(Path.Combine(CatalogDir, "profiles.json"), catalog, NullLogger.Instance);
            DefaultProfiles.Seed(store, pt, store.LoadAll(catalog), []);
        }
        // O usuário renomeia um dos perfis: esse não pode ser trocado.
        var renamed = store.LoadAll(catalog).First(p => p.Name == "PC Trabalho");
        renamed.Name = "Meu trabalho";
        store.Save(renamed);

        using (L.Use(L.English))
        {
            var en = DefaultProfiles.Load(Path.Combine(CatalogDir, "profiles.json"), catalog, NullLogger.Instance);
            DefaultProfiles.Relabel(store, en, store.LoadAll(catalog));
        }
        var names = store.LoadAll(catalog).Select(p => p.Name).ToList();
        Assert.Contains("Gaming PC", names);
        Assert.Contains("Complete PC", names);
        Assert.Contains("Meu trabalho", names);
        Assert.DoesNotContain("PC Gamer", names);
    }

    [GeneratedRegex(@"\bL\.T\(\s*(\$?)""")]
    private static partial Regex CallT();

    [GeneratedRegex(@"\bL\.Plural\([^,]+,\s*""((?:[^""\\]|\\.)*)"",\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex CallPlural();

    [GeneratedRegex(@"\{loc:T '([^']*)'\}")]
    private static partial Regex XamlT();

    [GeneratedRegex(@"^[A-Za-z0-9.\-+ ]+$")]
    private static partial Regex LicenseId();

    [GeneratedRegex(@"[A-Za-zÀ-ú]")]
    private static partial Regex LetterPattern();

    [GeneratedRegex(@"\{(\d+|nome)(?::[^}]*)?\}")]
    private static partial Regex Placeholder();
}
