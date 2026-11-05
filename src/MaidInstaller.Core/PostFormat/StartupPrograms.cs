using Microsoft.Win32;

namespace MaidInstaller.Core.PostFormat;

/// <param name="Enabled">true/false conforme o Gerenciador de Tarefas; null quando o Windows não registrou nada.</param>
public sealed record StartupEntry(string Name, string Command, string Location, bool ForAllUsers, bool? Enabled);

/// <summary>Consulta (somente leitura) os programas que abrem junto com o Windows.</summary>
public static class StartupPrograms
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static IReadOnlyList<StartupEntry> Read()
    {
        var list = new List<StartupEntry>();
        ReadRun(list, Registry.CurrentUser, RunKey, L.T("Registro (usuário)"), false, "Run");
        ReadRun(list, Registry.LocalMachine, RunKey, L.T("Registro (todos os usuários)"), true, "Run");
        ReadRun(list, Registry.LocalMachine, Run32Key, L.T("Registro 32 bits (todos)"), true, "Run32");
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), L.T("Pasta Inicializar (usuário)"), false, Registry.CurrentUser);
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), L.T("Pasta Inicializar (todos)"), true, Registry.LocalMachine);
        return [.. list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// Valor binário do StartupApproved: o primeiro byte par (0x02, 0x06) significa ativado;
    /// ímpar (0x03, 0x07) significa desativado pelo usuário.
    /// </summary>
    public static bool? IsEnabled(byte[]? approved) => approved is { Length: > 0 } ? (approved[0] & 1) == 0 : null;

    private static void ReadRun(List<StartupEntry> list, RegistryKey hive, string path, string location, bool allUsers, string approvedSub)
    {
        try
        {
            using var key = hive.OpenSubKey(path);
            if (key is null) return;
            using var approved = hive.OpenSubKey(ApprovedKey + approvedSub);
            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(name)) continue;
                var cmd = key.GetValue(name)?.ToString() ?? "";
                list.Add(new StartupEntry(name, cmd, location, allUsers, IsEnabled(approved?.GetValue(name) as byte[])));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
    }

    private static void ReadFolder(List<StartupEntry> list, string folder, string location, bool allUsers, RegistryKey hive)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        try
        {
            using var approved = hive.OpenSubKey(ApprovedKey + "StartupFolder");
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new StartupEntry(Path.GetFileNameWithoutExtension(file), file, location, allUsers,
                    IsEnabled(approved?.GetValue(fileName) as byte[])));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }
}
