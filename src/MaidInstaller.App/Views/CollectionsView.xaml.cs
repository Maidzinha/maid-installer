using System.Windows.Controls;
using System.Windows.Input;

namespace MaidInstaller.App.Views;

public partial class CollectionsView : UserControl
{
    public CollectionsView() => InitializeComponent();

    /// <summary>Enter confirma o nome/descrição (salva na hora, igual a sair do campo).</summary>
    private void CommitOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }
}
