using System.Globalization;
using MaidInstaller.Core.Localization;

namespace MaidInstaller.Core.Tests;

public class LocalizationMechanicsTests
{
    static LocalizationMechanicsTests() =>
        L.LoadEnglish(Path.Combine(AppContext.BaseDirectory, "Localization", "en.json"));

    [Fact]
    public void Portuguese_returns_the_text_itself()
    {
        using var _ = L.Use(L.Portuguese);
        Assert.Equal("Instalar", L.T("Instalar"));
        var app = "Discord";
        Assert.Equal("Instalando o Discord…", L.T($"Instalando o {app}…"));
        Assert.Equal("Atualizado em 05/11/2026 14:30", L.T($"Atualizado em {new DateTime(2026, 11, 5, 14, 30, 0):dd/MM/yyyy HH:mm}"));
    }

    [Fact]
    public void English_uses_the_dictionary_with_values_and_formats()
    {
        using var _ = L.Use(L.English);
        Assert.Equal("Install", L.T("Instalar"));
        var app = "Discord";
        Assert.Equal("Installing Discord…", L.T($"Instalando o {app}…"));
        // A tradução pode trocar o formato: data no padrão americano.
        Assert.Equal("Updated on 11/05/2026 14:30", L.T($"Atualizado em {new DateTime(2026, 11, 5, 14, 30, 0):dd/MM/yyyy HH:mm}"));
        Assert.Equal("1,500 files", L.Plural(1500, "arquivo", "arquivos"));
    }

    [Fact]
    public void Missing_translation_falls_back_to_portuguese()
    {
        using var _ = L.Use(L.English);
        Assert.Equal("Texto sem tradução", L.T("Texto sem tradução"));
        Assert.Contains("Texto sem tradução", L.MissingKeys);
    }

    [Theory]
    [InlineData(L.Portuguese, "Helder", "Oii, Helder!")]
    [InlineData(L.Portuguese, "", "Oii!")]
    [InlineData(L.English, "Ana", "Hi, Ana!")]
    [InlineData(L.English, null, "Hi!")]
    public void User_name_is_optional(string lang, string? name, string expected)
    {
        using var _ = L.Use(lang);
        Assert.Equal(expected, L.T($"Oii, {L.Name(name)}!"));
    }

    [Theory]
    [InlineData("Hmm, {nome}, ainda não tenho.", "Hmm, ainda não tenho.")]
    [InlineData("{nome}, estou instalando o Discord!", "Estou instalando o Discord!")]
    [InlineData("Limpei os temporários, {nome}: liberei 2 GB.", "Limpei os temporários: liberei 2 GB.")]
    [InlineData("Depois de formatar, {nome}, uma boa base é…", "Depois de formatar, uma boa base é…")]
    [InlineData("Sem nome aqui.", "Sem nome aqui.")]
    public void Address_removes_the_vocative_when_there_is_no_name(string text, string expected)
    {
        using var _ = L.Use(L.Portuguese);
        Assert.Equal(expected, L.Address(text, " "));
    }

    [Fact]
    public void Auto_follows_the_windows_language()
    {
        Assert.Equal(L.Portuguese, L.Resolve(L.Auto, CultureInfo.GetCultureInfo("pt-BR")));
        Assert.Equal(L.Portuguese, L.Resolve(L.Auto, CultureInfo.GetCultureInfo("pt-PT")));
        Assert.Equal(L.English, L.Resolve(L.Auto, CultureInfo.GetCultureInfo("es-ES")));
        Assert.Equal(L.English, L.Resolve(L.English, CultureInfo.GetCultureInfo("pt-BR")));
        Assert.Equal(L.Portuguese, L.Resolve(L.Portuguese, CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void Plural_is_translated()
    {
        using var _ = L.Use(L.Portuguese);
        Assert.Equal("1 arquivo", L.Plural(1, "arquivo", "arquivos"));
        Assert.Equal("1.500 arquivos", L.Plural(1500, "arquivo", "arquivos"));
    }
}
