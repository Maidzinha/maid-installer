using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MaidInstaller.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyWindowsEffects();
        StateChanged += (_, _) => UpdateMaximizedState();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateMaximizedState()
    {
        // Com WindowChrome, a janela maximizada ultrapassa a tela pela espessura da borda.
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
        MaxButton.ToolTip = WindowState == WindowState.Maximized ? "Restaurar" : "Maximizar";
    }

    /// <summary>Cantos arredondados e barra escura no Windows 11 (ignorado no Windows 10).</summary>
    private void ApplyWindowsEffects()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int round = 2; // DWMWCP_ROUND
        _ = DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        int dark = 1; // DWMWA_USE_IMMERSIVE_DARK_MODE
        _ = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
