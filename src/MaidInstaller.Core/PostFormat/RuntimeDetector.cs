using Microsoft.Win32;

namespace MaidInstaller.Core.PostFormat;

/// <summary>
/// Detecta runtimes que o WinGet nem sempre reconhece (ex.: o .NET que veio junto com o SDK ou com outro programa).
/// Só leitura: pastas oficiais do .NET, registro do Java/Visual C++, System32 e o PATH. Funciona mesmo sem WinGet.
/// </summary>
public static class RuntimeDetector
{
    /// <summary>Versão mais alta do .NET Windows Desktop Runtime com esse número principal, ou null.</summary>
    public static string? DotNetDesktop(int major, string? dotnetRoot = null)
    {
        var root = dotnetRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
        var dir = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateDirectories(dir)
            .Select(Path.GetFileName)
            .Where(n => n is not null && n.StartsWith($"{major}.", StringComparison.Ordinal))
            .Select(n => (name: n!, ver: Version.TryParse(n!.Split('-')[0], out var v) ? v : null))
            .Where(x => x.ver is not null)
            .OrderByDescending(x => x.ver)
            .Select(x => x.name)
            .FirstOrDefault();
    }

    /// <summary>Procura um Java instalado (qualquer fornecedor). Retorna a versão ou "encontrado" e de onde veio.</summary>
    public static string? Java()
    {
        foreach (var path in new[] { @"SOFTWARE\JavaSoft\JRE", @"SOFTWARE\JavaSoft\JDK", @"SOFTWARE\JavaSoft\Java Runtime Environment", @"SOFTWARE\JavaSoft\Java Development Kit" })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key?.GetValue("CurrentVersion") is string v && v.Length > 0) return v;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        if (Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } home && File.Exists(Path.Combine(home, "bin", "java.exe")))
            return "encontrado (JAVA_HOME)";
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var p in pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                // O "java.exe" falso da Microsoft (atalho da Loja) fica em WindowsApps e não conta.
                if (p.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(Path.Combine(p.Trim(), "java.exe"))) return "encontrado (PATH)";
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    /// <summary>
    /// Microsoft Visual C++ 2015–2022 Redistributable, pela chave documentada pela Microsoft:
    /// HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\(x64|x86) → Installed = 1, Version.
    /// </summary>
    public static string? VisualCpp(bool x64)
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, x64 ? RegistryView.Registry64 : RegistryView.Registry32);
            using var key = hklm.OpenSubKey($@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\{(x64 ? "x64" : "x86")}");
            if (key?.GetValue("Installed") is int installed && installed == 1)
                return key.GetValue("Version") as string is { Length: > 0 } v ? v.TrimStart('v') : "encontrado";
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return null;
    }

    /// <summary>DirectX End-User Runtime (bibliotecas D3DX9/10/11 de junho/2010): a última, d3dx9_43.dll, fica no System32.</summary>
    public static string? DirectXLegacy(string? system32 = null)
    {
        var dir = system32 ?? Environment.GetFolderPath(Environment.SpecialFolder.System);
        return File.Exists(Path.Combine(dir, "d3dx9_43.dll")) ? "d3dx9_43.dll presente" : null;
    }

    /// <summary>Detecção extra por id do catálogo. Null = este app não tem detecção extra.</summary>
    public static Func<string?>? For(string catalogId) => catalogId switch
    {
        "dotnet-desktop-8" => () => DotNetDesktop(8),
        "dotnet-desktop-10" => () => DotNetDesktop(10),
        "java-temurin-21" => Java,
        "vcredist-x64" => () => VisualCpp(x64: true),
        "vcredist-x86" => () => VisualCpp(x64: false),
        "directx" => () => DirectXLegacy(),
        _ => null,
    };
}
