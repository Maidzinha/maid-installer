using System.Windows.Markup;

namespace MaidInstaller.App.Localization;

/// <summary>
/// Texto traduzido no XAML: <c>Text="{loc:T 'Instalar selecionados'}"</c>.
/// O idioma é definido ao abrir o app (trocar o idioma reabre o app), então o valor é fixo.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string text) => Text = text;

    [ConstructorArgument("text")]
    public string Text { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Text);
}
