using MaidInstaller.Core.PostFormat;
using Microsoft.Win32;

namespace MaidInstaller.Core.Tests;

public sealed class PostFormatTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "maid-pf-" + Guid.NewGuid().ToString("N"));
    private readonly string _regPath = @"Software\MaidInstallerTests\" + Guid.NewGuid().ToString("N");

    public PostFormatTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
        Registry.CurrentUser.DeleteSubKeyTree(_regPath, throwOnMissingSubKey: false);
    }

    [Fact]
    public void Parses_powercfg_list_in_portuguese()
    {
        const string output = """
            Esquemas de Energia Existentes (* Ativos)
            -----------------------------------
            GUID do Esquema de Energia: 381b4222-f694-41f0-9685-ff5bb260df2e  (Equilibrado)
            GUID do Esquema de Energia: 58d266a2-3731-4154-9174-f6d119993ea3  (Meu Plano Personalizado 1) *
            GUID do Esquema de Energia: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (Alto desempenho)
            """;
        var plans = PowerPlans.Parse(output);
        Assert.Equal(3, plans.Count);
        Assert.Equal("Meu Plano Personalizado 1", Assert.Single(plans, p => p.IsActive).Name);
        Assert.Equal(Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), plans[2].Id);
    }

    [Fact]
    public void Parses_powercfg_list_in_english()
    {
        var plans = PowerPlans.Parse("Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *\r\n");
        Assert.True(Assert.Single(plans).IsActive);
        Assert.Equal("Balanced", plans[0].Name);
    }

    private string MakeFile(string relative, int size, DateTime time)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        File.SetCreationTime(path, time);
        File.SetLastWriteTime(path, time);
        File.SetLastAccessTime(path, time);
        return path;
    }

    [Fact]
    public void Temp_cleaner_only_removes_old_files()
    {
        var now = DateTime.Now;
        var old1 = MakeFile("antigo.tmp", 1000, now.AddDays(-3));
        var old2 = MakeFile(@"sub\velho.log", 500, now.AddDays(-2));
        var fresh = MakeFile("novo.tmp", 2000, now.AddMinutes(-5));
        Directory.SetLastWriteTime(Path.Combine(_dir, "sub"), now.AddDays(-2));

        var scan = TempCleaner.Scan(_dir, TempCleaner.DefaultMinAge, now);
        Assert.Equal(3, scan.TotalFiles);
        Assert.Equal(2, scan.EligibleFiles);
        Assert.Equal(1500, scan.EligibleBytes);

        var result = TempCleaner.Clean(_dir, TempCleaner.DefaultMinAge, now);
        Assert.Equal(2, result.DeletedFiles);
        Assert.Equal(1500, result.FreedBytes);
        Assert.False(File.Exists(old1));
        Assert.False(File.Exists(old2));
        Assert.True(File.Exists(fresh));
        Assert.True(Directory.Exists(_dir)); // a raiz nunca é apagada
    }

    [Fact]
    public void Temp_cleaner_skips_files_in_use_and_read_only()
    {
        var now = DateTime.Now;
        var locked = MakeFile("emuso.tmp", 10, now.AddDays(-5));
        var ro = MakeFile("protegido.tmp", 10, now.AddDays(-5));
        File.SetAttributes(ro, FileAttributes.ReadOnly);
        try
        {
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var result = TempCleaner.Clean(_dir, TempCleaner.DefaultMinAge, now);
                Assert.Equal(0, result.DeletedFiles);
                Assert.Equal(2, result.SkippedFiles);
            }
            Assert.True(File.Exists(locked));
            Assert.True(File.Exists(ro));
        }
        finally { File.SetAttributes(ro, FileAttributes.Normal); }
    }

    [Fact]
    public void Temp_cleaner_does_not_follow_junctions()
    {
        // Uma junção dentro do "TEMP" apontando para outra pasta: o conteúdo de lá não pode ser apagado.
        var outside = Path.Combine(Path.GetTempPath(), "maid-pf-fora-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var precious = Path.Combine(outside, "importante.txt");
        File.WriteAllText(precious, "não apagar");
        File.SetLastWriteTime(precious, DateTime.Now.AddDays(-30));
        File.SetCreationTime(precious, DateTime.Now.AddDays(-30));
        File.SetLastAccessTime(precious, DateTime.Now.AddDays(-30));
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_dir, "link"), outside);
        }
        catch (IOException) { return; } // sem permissão para criar link neste PC: nada a testar
        catch (UnauthorizedAccessException) { return; }
        try
        {
            TempCleaner.Clean(_dir, TempCleaner.DefaultMinAge);
            Assert.True(File.Exists(precious));
        }
        finally { Directory.Delete(outside, true); }
    }

    [Fact]
    public void File_extensions_setting_roundtrip_in_test_key()
    {
        Assert.False(FileExtensions.AreVisible(Registry.CurrentUser, _regPath)); // sem valor = escondidas (padrão do Windows)
        FileExtensions.SetVisible(true, Registry.CurrentUser, _regPath);
        Assert.True(FileExtensions.AreVisible(Registry.CurrentUser, _regPath));
        FileExtensions.SetVisible(false, Registry.CurrentUser, _regPath);
        Assert.False(FileExtensions.AreVisible(Registry.CurrentUser, _regPath));
    }

    [Theory]
    [InlineData(new byte[] { 0x02, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x06, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x03, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x07, 0, 0, 0 }, false)]
    public void Startup_approved_flag(byte[] value, bool enabled) => Assert.Equal(enabled, StartupPrograms.IsEnabled(value));

    [Fact]
    public void Startup_approved_missing_is_unknown() => Assert.Null(StartupPrograms.IsEnabled(null));

    [Theory]
    [InlineData("10/15/2026", 2026, 10, 15)]
    [InlineData("1/2/2026", 2026, 1, 2)]
    [InlineData("20261001", 2026, 10, 1)]
    public void Parses_hotfix_dates(string raw, int y, int m, int d) =>
        Assert.Equal(new DateTime(y, m, d), WindowsUpdateCheck.ParseDate(raw));

    [Fact]
    public void Unknown_hotfix_date_is_null() => Assert.Null(WindowsUpdateCheck.ParseDate("lixo"));

    [Fact]
    public void Device_codes_are_explained() => Assert.Equal("nenhum driver instalado", DriverCheck.Explain(28));

    [Fact]
    public void Admin_tools_are_fixed_windows_tools()
    {
        Assert.All(AdminTools.All, t =>
        {
            Assert.DoesNotContain(' ', t.Target);
            Assert.DoesNotContain('\\', t.Target);
            Assert.True(t.Target.EndsWith(".msc") || t.Target.EndsWith(".exe"));
            Assert.Null(t.Arguments);
        });
    }

    [Fact]
    public void Detects_highest_dotnet_desktop_version()
    {
        foreach (var v in new[] { "8.0.10", "8.0.9", "10.0.12", "8.0.33" })
            Directory.CreateDirectory(Path.Combine(_dir, "shared", "Microsoft.WindowsDesktop.App", v));
        Assert.Equal("8.0.33", RuntimeDetector.DotNetDesktop(8, _dir));
        Assert.Equal("10.0.12", RuntimeDetector.DotNetDesktop(10, _dir));
        Assert.Null(RuntimeDetector.DotNetDesktop(9, _dir));
        Assert.Null(RuntimeDetector.DotNetDesktop(8, Path.Combine(_dir, "nao-existe")));
    }

    [Fact]
    public void Formats_bytes() => Assert.Equal("1.5 MB".Replace(".", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator),
        TempCleaner.FormatBytes(1536 * 1024));
}
