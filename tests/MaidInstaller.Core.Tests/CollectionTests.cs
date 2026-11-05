using MaidInstaller.Core.Catalog;
using MaidInstaller.Core.Collections;
using MaidInstaller.Core.Logging;
using MaidInstaller.Core.Storage;

namespace MaidInstaller.Core.Tests;

public sealed class CollectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "maid-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly string CatalogDir = Path.Combine(AppContext.BaseDirectory, "Catalog");
    private static readonly AppCatalog Catalog = CatalogLoader.Load(Path.Combine(CatalogDir, "catalog.json"), NullLogger.Instance);

    public CollectionTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private CollectionStore Store(string sub = "Lists") => new(Path.Combine(_dir, sub), CollectionKind.List, NullLogger.Instance);

    [Theory]
    [InlineData("Meu PC Gamer", "meu-pc-gamer-")]
    [InlineData("Programas de edição!!", "programas-de-edicao-")]
    [InlineData("   ", "colecao-")]
    [InlineData("../../Windows", "windows-")]
    public void NewId_is_a_safe_slug(string name, string prefix)
    {
        var id = AppCollection.NewId(name);
        Assert.StartsWith(prefix, id);
        Assert.True(AppCollection.IsValidId(id));
    }

    [Theory]
    [InlineData("..\\evil")]
    [InlineData("a/b")]
    [InlineData("CON ")]
    [InlineData("")]
    public void Invalid_ids_are_rejected_for_paths(string id)
    {
        Assert.False(AppCollection.IsValidId(id));
        Assert.Throws<ArgumentException>(() => Store().PathFor(id));
    }

    [Fact]
    public void Save_load_roundtrip_and_cleanup()
    {
        var store = Store();
        var list = new AppCollection { Name = "  Meu   PC  principal ", AppIds = ["7zip", "discord", "7zip", "nao-existe"] };
        store.Save(list);

        var loaded = Assert.Single(store.LoadAll(Catalog));
        Assert.Equal("Meu PC principal", loaded.Name);
        Assert.Equal(["7zip", "discord"], loaded.AppIds); // repetido e app fora do catálogo removidos
        Assert.True(File.Exists(store.PathFor(loaded.Id)));
    }

    [Fact]
    public void Empty_name_is_not_saved()
    {
        Assert.Throws<ArgumentException>(() => Store().Save(new AppCollection { Name = "   " }));
    }

    [Fact]
    public void Corrupt_or_mismatched_files_are_skipped_not_deleted()
    {
        var store = Store();
        Directory.CreateDirectory(store.Directory);
        var bad = Path.Combine(store.Directory, "quebrado.json");
        File.WriteAllText(bad, "{ isto não é json");
        var other = Path.Combine(store.Directory, "outro-nome.json");
        JsonStore.WriteAtomic(other, new AppCollection { Id = "id-diferente", Name = "X" });
        store.Save(new AppCollection { Name = "Boa" });

        var all = store.LoadAll(Catalog);
        Assert.Equal("Boa", Assert.Single(all).Name);
        Assert.True(File.Exists(bad));
    }

    [Fact]
    public void Duplicate_and_delete()
    {
        var store = Store();
        var original = new AppCollection { Name = "Essenciais", BuiltInId = "pc-gamer", AppIds = ["chrome"] };
        store.Save(original);
        var copy = store.Duplicate(original, ["Essenciais"]);

        Assert.Equal("Essenciais (cópia)", copy.Name);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.False(copy.IsBuiltIn);
        Assert.Equal(2, store.LoadAll().Count);

        store.Delete(original);
        Assert.Equal(copy.Id, Assert.Single(store.LoadAll()).Id);
    }

    [Fact]
    public void UniqueName_adds_number()
    {
        Assert.Equal("Lista", CollectionStore.UniqueName("Lista", ["Outra"]));
        Assert.Equal("Lista 3", CollectionStore.UniqueName("Lista", ["lista", "Lista 2"]));
    }

    [Fact]
    public void Export_then_import_keeps_apps()
    {
        var path = Path.Combine(_dir, "minha" + CollectionTransfer.FileExtension);
        var list = new AppCollection { Name = "Edição", Description = "Vídeo e áudio", AppIds = ["obs", "audacity", "vlc"] };
        CollectionTransfer.Export(list, CollectionKind.List, Catalog, path);

        var result = CollectionTransfer.Import(path, CollectionKind.Profile, Catalog);
        Assert.Equal("Edição", result.Collection.Name);
        Assert.Equal(["obs", "audacity", "vlc"], result.Collection.AppIds);
        Assert.Empty(result.Unknown);
        Assert.Equal(CollectionKind.List, result.SourceKind);
        Assert.Contains("\"winGetId\": \"OBSProject.OBSStudio\"", File.ReadAllText(path));
    }

    [Fact]
    public void Import_matches_by_winget_id_and_drops_unknown_apps()
    {
        var path = Path.Combine(_dir, "externo.json");
        File.WriteAllText(path, """
        {
          "format": "maid-installer/colecao", "schemaVersion": 1, "kind": "list", "name": "De outro PC",
          "apps": [
            { "id": "id-antigo", "winGetId": "7ZIP.7ZIP" },
            { "id": "malicioso", "winGetId": "Evil.Package", "name": "Programa estranho" },
            { "id": "discord" }
          ]
        }
        """);
        var result = CollectionTransfer.Import(path, CollectionKind.List, Catalog);
        Assert.Equal(["7zip", "discord"], result.Collection.AppIds);
        Assert.Equal(["Programa estranho"], result.Unknown);
    }

    [Theory]
    [InlineData("{ \"format\": \"outra-coisa\", \"apps\": [] }", "não foi exportado")]
    [InlineData("nada a ver", "JSON válido")]
    [InlineData("{ \"format\": \"maid-installer/colecao\", \"schemaVersion\": 9, \"apps\": [] }", "versão mais nova")]
    public void Import_rejects_foreign_files(string content, string expected)
    {
        var path = Path.Combine(_dir, "x.json");
        File.WriteAllText(path, content);
        var ex = Assert.Throws<InvalidDataException>(() => CollectionTransfer.Import(path, CollectionKind.List, Catalog));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Import_rejects_huge_files()
    {
        var path = Path.Combine(_dir, "grande.json");
        File.WriteAllText(path, new string(' ', (int)CollectionTransfer.MaxFileBytes + 10));
        Assert.Throws<InvalidDataException>(() => CollectionTransfer.Import(path, CollectionKind.List, Catalog));
    }

    [Fact]
    public void Shipped_default_profiles_use_only_catalog_apps()
    {
        var defaults = DefaultProfiles.Load(Path.Combine(CatalogDir, "profiles.json"), Catalog, NullLogger.Instance);
        Assert.Equal(["pc-gamer", "pc-trabalho", "pc-completo"], defaults.Select(d => d.BuiltInId));
        Assert.All(defaults, d =>
        {
            Assert.NotEmpty(d.AppIds);
            Assert.All(d.AppIds, id => Assert.NotNull(Catalog.FindApp(id)));
            Assert.All(d.AppIds, id => Assert.True(Catalog.FindApp(id)!.HasWinGet, $"{d.Name}: {id} sem WinGet"));
        });
        var gamer = defaults[0];
        Assert.Contains("steam", gamer.AppIds);
        Assert.Contains("discord", gamer.AppIds);
        Assert.Contains("epic", gamer.AppIds);
        Assert.Contains("obs", gamer.AppIds);
    }

    [Fact]
    public void List_from_newer_version_keeps_unknown_fields_and_version()
    {
        var dir = Path.Combine(_dir, "Lists");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "minha-lista.json"),
            """{ "schemaVersion": 3, "id": "minha-lista", "name": "Minha lista", "appIds": ["steam"], "corNova": "rosa" }""");
        var store = new CollectionStore(dir, CollectionKind.List, NullLogger.Instance);
        var list = store.LoadAll(Catalog).Single().Clone();
        list.AppIds.Add("discord");
        store.Save(list);

        var json = File.ReadAllText(Path.Combine(dir, "minha-lista.json"));
        Assert.Contains("\"schemaVersion\": 3", json);
        Assert.Contains("\"corNova\": \"rosa\"", json);
        Assert.Contains("discord", json);
    }

    [Fact]
    public void Seed_creates_once_and_respects_deletion()
    {
        var store = new CollectionStore(Path.Combine(_dir, "Profiles"), CollectionKind.Profile, NullLogger.Instance);
        var defaults = DefaultProfiles.Load(Path.Combine(CatalogDir, "profiles.json"), Catalog, NullLogger.Instance);
        var seeded = new List<string>();

        Assert.Equal(3, DefaultProfiles.Seed(store, defaults, store.LoadAll(Catalog), seeded));
        Assert.Equal(0, DefaultProfiles.Seed(store, defaults, store.LoadAll(Catalog), seeded));

        var gamer = store.LoadAll(Catalog).Single(p => p.BuiltInId == "pc-gamer");
        store.Delete(gamer);
        Assert.Equal(0, DefaultProfiles.Seed(store, defaults, store.LoadAll(Catalog), seeded)); // não volta sozinho
        Assert.Equal(1, DefaultProfiles.Seed(store, defaults, store.LoadAll(Catalog), seeded, force: true)); // restaurar
        Assert.Equal(3, store.LoadAll(Catalog).Count);
    }
}
