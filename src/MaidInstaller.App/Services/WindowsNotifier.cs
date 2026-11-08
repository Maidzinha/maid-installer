using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MaidInstaller.App.Services;

/// <summary>
/// Aviso do Windows (no Windows 10 e 11 aparece como notificação no canto da tela).
/// Usa um ícone temporário na área de notificação. Ele fica lá (e o aviso fica na central de notificações)
/// até o usuário clicar nele ou voltar para o app; com o "Não incomodar" ligado, o aviso vai direto para a central.
/// Só mostra texto: clicar no aviso traz a janela do app de volta, nada mais.
/// </summary>
public sealed class WindowsNotifier : IDisposable
{
    private const int CallbackMessage = 0x8000 + 0x4D; // WM_APP + 0x4D
    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4;
    private const uint NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4, NifInfo = 0x10;
    private const uint NiifUser = 0x4, NiifLargeIcon = 0x20;
    private const int NinBalloonUserClick = 0x405;
    private const int WmLButtonUp = 0x202;

    private readonly Window _window;
    private HwndSource? _source;
    private IntPtr _icon;
    private bool _added;
    private Action? _onClick;

    public WindowsNotifier(Window window)
    {
        _window = window;
        _window.Activated += (_, _) => Remove(); // voltou para o app: o aviso já não é necessário
    }

    /// <summary>Mostra o aviso. <paramref name="onClick"/> roda na thread da interface se o usuário clicar.</summary>
    public bool Show(string title, string text, Action? onClick = null)
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero) return false;
        if (_source is null)
        {
            _source = HwndSource.FromHwnd(hwnd);
            _source?.AddHook(Hook);
        }
        if (_icon == IntPtr.Zero) _icon = LoadAppIcon();

        var data = NewData(hwnd);
        data.uFlags = NifMessage | NifIcon | NifTip | NifInfo;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.szTip = "Maid Installer";
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(text, 255);
        data.dwInfoFlags = NiifUser | NiifLargeIcon;
        data.hBalloonIcon = _icon;

        bool ok;
        if (!_added)
        {
            ok = Shell_NotifyIcon(NimAdd, ref data);
            if (ok)
            {
                data.uVersion = 4; // NOTIFYICON_VERSION_4
                Shell_NotifyIcon(NimSetVersion, ref data);
                _added = true;
            }
        }
        else ok = Shell_NotifyIcon(NimModify, ref data);
        _onClick = onClick;
        return ok;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != CallbackMessage) return IntPtr.Zero;
        var ev = (int)(lParam.ToInt64() & 0xFFFF);
        switch (ev)
        {
            case NinBalloonUserClick:
            case WmLButtonUp:
                var click = _onClick;
                Remove();
                click?.Invoke();
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    private void Remove()
    {
        _onClick = null;
        if (!_added) return;
        var data = NewData(new WindowInteropHelper(_window).Handle);
        Shell_NotifyIcon(NimDelete, ref data);
        _added = false;
    }

    public void Dispose()
    {
        Remove();
        _source?.RemoveHook(Hook);
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        _icon = IntPtr.Zero;
    }

    private static NotifyIconData NewData(IntPtr hwnd) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        hWnd = hwnd,
        uID = 1,
        szTip = "", szInfo = "", szInfoTitle = "",
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    /// <summary>O ícone do próprio executável (o mesmo da barra de tarefas).</summary>
    private static IntPtr LoadAppIcon()
    {
        var exe = Environment.ProcessPath;
        if (exe is not null && ExtractIconEx(exe, 0, out var large, out var small, 1) > 0)
        {
            if (small != IntPtr.Zero) DestroyIcon(small);
            if (large != IntPtr.Zero) return large;
        }
        return LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
}
