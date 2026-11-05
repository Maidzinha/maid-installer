using System.Windows.Controls;

namespace MaidInstaller.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        // Mantém a conversa rolada até a última mensagem.
        ChatScroll.ScrollChanged += (_, e) =>
        {
            if (e.ExtentHeightChange > 0) ChatScroll.ScrollToEnd();
        };
    }
}
