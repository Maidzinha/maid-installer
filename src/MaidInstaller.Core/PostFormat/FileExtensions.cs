using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MaidInstaller.Core.PostFormat;

/// <summary>
/// "Mostrar extensões de nomes de arquivos" do Explorer (HKCU, só o usuário atual, sem administrador).
/// É a mesma opção do menu Exibir do Explorer e pode ser desfeita a qualquer momento.
/// </summary>
public static partial class FileExtensions
{
    public const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ValueName = "HideFileExt";

    /// <param name="baseKey">Para testes; por padrão HKEY_CURRENT_USER.</param>
    public static bool AreVisible(RegistryKey? baseKey = null, string keyPath = AdvancedKey)
    {
        using var key = (baseKey ?? Registry.CurrentUser).OpenSubKey(keyPath);
        // Sem o valor, o padrão do Windows é esconder as extensões.
        return key?.GetValue(ValueName) is int v && v == 0;
    }

    public static void SetVisible(bool visible, RegistryKey? baseKey = null, string keyPath = AdvancedKey)
    {
        using var key = (baseKey ?? Registry.CurrentUser).CreateSubKey(keyPath, writable: true);
        key.SetValue(ValueName, visible ? 0 : 1, RegistryValueKind.DWord);
        if (baseKey is null) NotifyShell();
    }

    /// <summary>Avisa o Explorer para redesenhar as janelas abertas.</summary>
    private static void NotifyShell()
    {
        const int SHCNE_ASSOCCHANGED = 0x08000000;
        SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
    }

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
