using System.Runtime.InteropServices;

namespace MaidInstaller.Core.Shell;

/// <summary>Um programa visível no Menu Iniciar (pasta virtual shell:AppsFolder).</summary>
/// <param name="Name">Nome exibido no Menu Iniciar.</param>
/// <param name="ParsingName">Identificador usado em "shell:AppsFolder\{ParsingName}" (AUMID ou caminho).</param>
public sealed record ShellApp(string Name, string ParsingName)
{
    public string ShellPath => @"shell:AppsFolder\" + ParsingName;
}

/// <summary>
/// Lista os programas instalados como o Menu Iniciar os vê — inclui apps clássicos e da Microsoft Store
/// (WhatsApp, Spotify da loja etc.). Somente leitura: nada é executado aqui.
/// </summary>
public static class ShellAppsFolder
{
    private const string AppsFolderClsid = "shell:::{4234d49b-0245-4df3-b780-3893943456e1}";

    private static readonly string[] IgnoredWords =
        ["uninstall", "desinstalar", "remover", "readme", "leia-me", "help", "ajuda", "website", "release notes"];

    /// <summary>Precisa rodar em thread STA (use <see cref="StaThread.Run{T}"/>).</summary>
    public static IReadOnlyList<ShellApp> Enumerate()
    {
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null) return [];

        object? shellObj = Activator.CreateInstance(type);
        if (shellObj is null) return [];
        try
        {
            dynamic shell = shellObj;
            dynamic? folder = shell.NameSpace(AppsFolderClsid);
            if (folder is null) return [];

            var result = new List<ShellApp>();
            foreach (dynamic item in folder.Items())
            {
                string? name = item.Name;
                string? path = item.Path;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path))
                    result.Add(new ShellApp(name, path));
            }
            return result;
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellObj);
        }
    }

    /// <summary>Encontra o primeiro app cujo nome bate com algum dos nomes (exato primeiro, depois prefixo).</summary>
    public static ShellApp? Match(IEnumerable<ShellApp> apps, IEnumerable<string> matchNames)
    {
        var candidates = apps.Where(a => !IgnoredWords.Any(w => a.Name.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var wanted in matchNames)
        {
            var exact = candidates.FirstOrDefault(a => a.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
        }
        foreach (var wanted in matchNames)
        {
            var prefix = candidates
                .Where(a => a.Name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Name.Length)
                .FirstOrDefault();
            if (prefix is not null) return prefix;
        }
        // Nomes traduzidos, ex.: "Navegador Opera GX" no Windows em português.
        foreach (var wanted in matchNames)
        {
            var contains = candidates
                .Where(a => (" " + a.Name + " ").Contains(" " + wanted + " ", StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Name.Length)
                .FirstOrDefault();
            if (contains is not null) return contains;
        }
        return null;
    }
}

public static class StaThread
{
    /// <summary>Executa uma função em uma thread STA dedicada (exigida por APIs COM do Shell).</summary>
    public static Task<T> Run<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        { IsBackground = true, Name = "MaidInstaller.STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
