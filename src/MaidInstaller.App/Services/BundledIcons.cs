using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MaidInstaller.App.Services;

/// <summary>
/// Ícones que vêm junto com o app (Assets/AppIcons/&lt;id&gt;.png), tirados do site oficial de cada programa
/// (fontes em docs/ICONES.md). Usados só quando o Windows não tem um ícone real do programa
/// (ex.: ainda não está instalado). Sem arquivo → null e a tela mostra a letra inicial.
/// </summary>
public static class BundledIcons
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string Folder { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcons");

    public static ImageSource? Get(string catalogId) => Cache.GetOrAdd(catalogId, Load);

    private static ImageSource? Load(string id)
    {
        // Só ids simples do catálogo (letras, números e hífen) viram nome de arquivo.
        if (id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return null;
        var file = Path.Combine(Folder, id + ".png");
        if (!File.Exists(file)) return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad; // lê tudo e solta o arquivo
            img.UriSource = new Uri(file, UriKind.Absolute);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
