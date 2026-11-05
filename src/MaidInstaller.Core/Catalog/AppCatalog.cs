using System.Text.RegularExpressions;
using MaidInstaller.Core.Logging;
using MaidInstaller.Core.Storage;

namespace MaidInstaller.Core.Catalog;

public sealed class AppCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public string? VerifiedOn { get; set; }
    public List<CatalogCategory> Categories { get; set; } = [];
    public List<CatalogApp> Apps { get; set; } = [];

    public CatalogCategory? FindCategory(string id) => Categories.FirstOrDefault(c => c.Id == id);
    public CatalogApp? FindApp(string id) => Apps.FirstOrDefault(a => a.Id == id);
}

public sealed class CatalogCategory
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Glyph { get; set; } = "";
}

public sealed class CatalogApp
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Developer { get; set; } = "";
    public string Description { get; set; } = "";
    public string Price { get; set; } = "";
    public string License { get; set; } = "";
    /// <summary>Tamanho aproximado, quando conhecido (null = não informado).</summary>
    public string? ApproxSize { get; set; }
    public string OfficialUrl { get; set; } = "";
    /// <summary>Identificador WinGet verificado, ou null se o app não existe no WinGet.</summary>
    public string? WinGetId { get; set; }
    /// <summary>"winget" ou "msstore".</summary>
    public string? WinGetSource { get; set; }
    public string? VerifiedVersion { get; set; }
    public List<string> StartMenuNames { get; set; } = [];
    public List<string> Aliases { get; set; } = [];
    public string? Notes { get; set; }

    public bool HasWinGet => WinGetId is not null;
    public bool IsFree => Price.StartsWith("Gratuito", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Valida o catálogo antes do uso. Entradas inválidas são descartadas (e registradas):
/// ID WinGet com formato estranho, fonte desconhecida, link que não é HTTPS, categoria inexistente.
/// </summary>
public static partial class CatalogValidator
{
    public static readonly string[] AllowedSources = ["winget", "msstore"];

    public static IReadOnlyList<string> Validate(AppCatalog catalog, out AppCatalog clean)
    {
        var problems = new List<string>();
        var categoryIds = catalog.Categories.Select(c => c.Id).ToHashSet();
        var seen = new HashSet<string>();
        var apps = new List<CatalogApp>();

        foreach (var app in catalog.Apps)
        {
            var error = Check(app, categoryIds);
            if (error is null && !seen.Add(app.Id)) error = "id duplicado";
            if (error is null) apps.Add(app);
            else problems.Add($"{(string.IsNullOrEmpty(app.Id) ? "(sem id)" : app.Id)}: {error}");
        }

        clean = new AppCatalog
        {
            SchemaVersion = catalog.SchemaVersion,
            VerifiedOn = catalog.VerifiedOn,
            Categories = catalog.Categories,
            Apps = apps,
        };
        return problems;
    }

    private static string? Check(CatalogApp app, HashSet<string> categoryIds)
    {
        if (!AppIdRegex().IsMatch(app.Id)) return "id inválido";
        if (string.IsNullOrWhiteSpace(app.Name)) return "sem nome";
        if (!categoryIds.Contains(app.Category)) return $"categoria inexistente '{app.Category}'";
        if (!IsHttps(app.OfficialUrl)) return "link oficial precisa ser https";
        if (app.WinGetId is null) return app.WinGetSource is null ? null : "fonte sem identificador";

        if (!AllowedSources.Contains(app.WinGetSource)) return $"fonte não permitida '{app.WinGetSource}'";
        var idOk = app.WinGetSource == "msstore" ? StoreIdRegex().IsMatch(app.WinGetId) : WinGetIdRegex().IsMatch(app.WinGetId);
        return idOk ? null : $"identificador WinGet inválido '{app.WinGetId}'";
    }

    public static bool IsHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>Formato de PackageIdentifier do WinGet: Editor.Pacote[.Sufixo], sem espaços nem caracteres de shell.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\-_+]*(\.[A-Za-z0-9\-_+]+)+$")]
    public static partial Regex WinGetIdRegex();

    [GeneratedRegex(@"^[0-9A-Z]{12}$")]
    public static partial Regex StoreIdRegex();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9\-]*$")]
    private static partial Regex AppIdRegex();
}

public static class CatalogLoader
{
    public static AppCatalog Load(string path, IAppLogger logger)
    {
        var raw = JsonStore.Read<AppCatalog>(path) ?? new AppCatalog();
        var problems = CatalogValidator.Validate(raw, out var clean);
        foreach (var p in problems) logger.Warn($"Catálogo: entrada ignorada — {p}");
        logger.Info($"Catálogo carregado: {clean.Apps.Count} apps em {clean.Categories.Count} categorias.");
        return clean;
    }
}
