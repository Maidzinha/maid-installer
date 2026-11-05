using MaidInstaller.Core.Storage;

namespace MaidInstaller.Core.Tests;

public sealed class PortableTests : IDisposable
{
    private readonly TempDir _root = new();
    private readonly string _app;
    private readonly string _local;
    private readonly string _usb;

    public PortableTests()
    {
        _app = Dir("app");
        _local = Dir("local");
        _usb = Dir("Pendrive Ventoy ♡");
        Write(_app, "MaidInstaller.exe", "exe");
        Write(_app, "MaidInstaller.dll", "dll");
        Write(_app, @"Catalog\catalog.json", "{}");
        Write(_app, @"runtimes\win-x64\native\avx2\llama.dll", "native");
        Write(_app, "lixo.tmp", "x");
    }

    public void Dispose() => _root.Dispose();

    private string Dir(string name) => Directory.CreateDirectory(Path.Combine(_root.Path, name)).FullName;

    private static void Write(string dir, string rel, string content)
    {
        var path = Path.Combine(dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private AppPaths InstalledWithData()
    {
        var paths = AppPaths.Detect(_app, _local);
        paths.EnsureCreated();
        Write(paths.ConfigDir, "settings.json", "{\"userName\":\"Helder\"}");
        Write(paths.ProfilesDir, "pc-gamer.json", "{}");
        Write(paths.ListsDir, "minha-lista.json", "{}");
        Write(paths.LogsDir, "maid-20261104.log", "log deste PC");
        Write(paths.DataDir, "historico.json", "[]");
        Write(paths.CacheDir, "installed-cache.json", "{}");
        Write(paths.ModelsDir, "modelo.gguf", "GGUF");
        return paths;
    }

    [Fact]
    public async Task Copy_brings_app_and_user_lists_but_not_logs_cache_history_or_model()
    {
        var paths = InstalledWithData();
        var plan = PortableCopy.Plan(paths, _usb, includeModel: false, Path.Combine(paths.ModelsDir, "modelo.gguf"));
        var dest = Path.Combine(_usb, "MaidInstaller");
        Assert.Equal(dest, plan.Destination);
        Assert.False(plan.UpdatesExisting);
        Assert.False(plan.IncludesModel);

        await PortableCopy.ExecuteAsync(plan, null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(dest, AppPaths.PortableMarkerFile)));
        Assert.True(File.Exists(Path.Combine(dest, "MaidInstaller.exe")));
        Assert.True(File.Exists(Path.Combine(dest, @"runtimes\win-x64\native\avx2\llama.dll")));
        Assert.True(File.Exists(Path.Combine(dest, @"Config\settings.json")));
        Assert.True(File.Exists(Path.Combine(dest, @"Profiles\pc-gamer.json")));
        Assert.True(File.Exists(Path.Combine(dest, @"Lists\minha-lista.json")));
        Assert.False(File.Exists(Path.Combine(dest, "lixo.tmp")));
        Assert.Empty(Directory.GetFiles(Path.Combine(dest, "Logs")));
        Assert.Empty(Directory.GetFiles(Path.Combine(dest, "Cache")));
        Assert.Empty(Directory.GetFiles(Path.Combine(dest, "Data")));
        Assert.Empty(Directory.GetFiles(Path.Combine(dest, "Models")));
        Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));

        // A cópia abre em modo portátil, com os dados dentro dela.
        var portable = AppPaths.Detect(dest, _local);
        Assert.True(portable.IsPortable);
        Assert.Equal(dest, portable.DataRoot);
    }

    [Fact]
    public async Task Model_is_copied_only_when_asked()
    {
        var paths = InstalledWithData();
        var model = Path.Combine(paths.ModelsDir, "modelo.gguf");
        var plan = PortableCopy.Plan(paths, _usb, includeModel: true, model);
        Assert.True(plan.IncludesModel);
        await PortableCopy.ExecuteAsync(plan, null, CancellationToken.None);
        Assert.Equal("GGUF", File.ReadAllText(Path.Combine(plan.Destination, @"Models\modelo.gguf")));
    }

    [Fact]
    public async Task Updating_an_existing_copy_keeps_its_own_data()
    {
        var paths = InstalledWithData();
        await PortableCopy.ExecuteAsync(PortableCopy.Plan(paths, _usb, false), null, CancellationToken.None);
        var dest = Path.Combine(_usb, "MaidInstaller");
        File.WriteAllText(Path.Combine(dest, @"Lists\minha-lista.json"), "editada no pendrive");
        File.WriteAllText(Path.Combine(dest, @"Logs\log.txt"), "log do pendrive");
        Write(paths.ListsDir, "nova.json", "{}");
        Write(_app, "MaidInstaller.dll", "dll v2");

        // Escolher a própria pasta da cópia (ou o pendrive) atualiza a mesma cópia.
        var plan = PortableCopy.Plan(paths, dest, false);
        Assert.True(plan.UpdatesExisting);
        Assert.Equal(dest, plan.Destination);
        await PortableCopy.ExecuteAsync(plan, null, CancellationToken.None);

        Assert.Equal("editada no pendrive", File.ReadAllText(Path.Combine(dest, @"Lists\minha-lista.json")));
        Assert.Equal("log do pendrive", File.ReadAllText(Path.Combine(dest, @"Logs\log.txt")));
        Assert.True(File.Exists(Path.Combine(dest, @"Lists\nova.json")));
        Assert.Equal("dll v2", File.ReadAllText(Path.Combine(dest, "MaidInstaller.dll")));
    }

    [Fact]
    public void Copy_from_a_portable_copy_does_not_duplicate_its_data_folders_as_app_files()
    {
        File.WriteAllText(Path.Combine(_app, AppPaths.PortableMarkerFile), "");
        var paths = AppPaths.Detect(_app, _local);
        paths.EnsureCreated();
        Write(paths.LogsDir, "maid.log", "x");
        Write(paths.ListsDir, "lista.json", "{}");
        var plan = PortableCopy.Plan(paths, _usb, false);
        Assert.DoesNotContain(plan.Files, f => f.RelativePath.StartsWith("Logs", StringComparison.OrdinalIgnoreCase));
        Assert.Single(plan.Files, f => f.RelativePath == @"Lists\lista.json" && f.IsUserData);
        Assert.DoesNotContain(plan.Files, f => f.RelativePath == AppPaths.PortableMarkerFile);
    }

    [Fact]
    public void Refuses_destination_inside_the_app_or_an_unrelated_non_empty_folder()
    {
        var paths = InstalledWithData();
        Assert.Throws<PortableCopyException>(() => PortableCopy.Plan(paths, Path.Combine(_app, "sub"), false));
        Assert.Throws<PortableCopyException>(() => PortableCopy.Plan(paths, _local, false));

        var other = Dir(@"outro\MaidInstaller");
        File.WriteAllText(Path.Combine(other, "documento.txt"), "não é do Maid");
        var ex = Assert.Throws<PortableCopyException>(() => PortableCopy.Plan(paths, Path.GetDirectoryName(other)!, false));
        Assert.Contains("não é uma cópia portátil", ex.Message);
    }

    [Fact]
    public async Task Cancelled_copy_leaves_no_marker()
    {
        var paths = InstalledWithData();
        var plan = PortableCopy.Plan(paths, _usb, false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PortableCopy.ExecuteAsync(plan, null, cts.Token));
        Assert.False(File.Exists(Path.Combine(plan.Destination, AppPaths.PortableMarkerFile)));
    }

    [Fact]
    public async Task Progress_is_real_and_reaches_total()
    {
        var paths = InstalledWithData();
        var plan = PortableCopy.Plan(paths, _usb, false);
        var reports = new List<CopyProgress>();
        await PortableCopy.ExecuteAsync(plan, new SyncProgress(reports.Add), CancellationToken.None);
        Assert.Equal(plan.TotalBytes, reports[^1].BytesCopied);
        Assert.True(reports.Select(r => r.BytesCopied).SequenceEqual(reports.Select(r => r.BytesCopied).Order()));
    }

    [Theory]
    [InlineData(@"E:\MaidInstaller\", @"E:\MaidInstaller")]
    [InlineData(@"E:\MaidInstaller", @"E:\MaidInstaller")]
    [InlineData(@"E:\", @"E:\")]
    public void App_directory_is_normalized_for_any_drive_letter(string input, string expected) =>
        Assert.Equal(expected, AppPaths.Detect(input, _local).AppDirectory);

    [Fact]
    public void Self_contained_detection_looks_for_the_bundled_runtime()
    {
        Assert.False(PortableCopy.IsSelfContained(_app));
        File.WriteAllText(Path.Combine(_app, "hostfxr.dll"), "");
        File.WriteAllText(Path.Combine(_app, "coreclr.dll"), "");
        Assert.True(PortableCopy.IsSelfContained(_app));
    }

    private sealed class SyncProgress(Action<CopyProgress> report) : IProgress<CopyProgress>
    {
        public void Report(CopyProgress value) => report(value);
    }
}
