using System.Windows.Input;
using MaidInstaller.App.Mvvm;

namespace MaidInstaller.App.ViewModels;

/// <summary>
/// Tour rápido de boas-vindas: alguns balões que abrem cada tela principal e explicam para que ela serve.
/// Aparece uma vez na primeira abertura; pode ser pulado e revisto em Configurações. Não faz nenhuma ação no PC.
/// </summary>
public sealed class TourViewModel : ObservableObject
{
    private sealed record Step(PageKey Page, string Title, string Text);

    private readonly IReadOnlyList<Step> _steps;
    private readonly Action<PageKey> _navigate;
    private readonly Action _finished;
    private int _index;

    public TourViewModel(Action<PageKey> navigate, Action finished)
    {
        _navigate = navigate;
        _finished = finished;
        _steps =
        [
            new(PageKey.Home, L.T("Oi! Eu sou a Maid ♡"),
                L.T("Vou te mostrar rapidinho o principal do Maid Installer. Leva menos de um minuto, e dá para pular quando quiser.")),
            new(PageKey.Library, L.T("Biblioteca de Apps"),
                L.T("Aqui ficam os programas verificados. Marque os que quiser e clique em Instalar: tudo vem das fontes oficiais pelo WinGet, e você confirma a lista antes.")),
            new(PageKey.Profiles, L.T("Perfis de Instalação"),
                L.T("Conjuntos prontos, como PC Gamer e PC Trabalho, para instalar vários programas de uma vez. Você também pode criar os seus.")),
            new(PageKey.PostFormat, L.T("Kit Pós-Formatação"),
                L.T("Verificações do sistema depois de formatar: Windows Update, drivers, componentes e mais. Cada ação é opcional e pede confirmação.")),
            new(PageKey.Home, L.T("Maid Assistant"),
                L.T("No painel ao lado, pergunte o que quiser sobre os programas e as instalações. Quando a fila terminar com o app em segundo plano, eu aviso pelo Windows. Bom proveito! ♡")),
        ];
        NextCommand = new RelayCommand(Next);
        BackCommand = new RelayCommand(Back, () => _index > 0);
        SkipCommand = new RelayCommand(Finish);
        _navigate(_steps[0].Page);
    }

    public string Title => _steps[_index].Title;
    public string Text => _steps[_index].Text;
    public string StepText => L.T($"{_index + 1} de {_steps.Count}");
    public bool IsFirst => _index == 0;
    public bool IsLast => _index == _steps.Count - 1;
    public string NextText => IsLast ? L.T("Começar") : L.T("Próximo");

    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SkipCommand { get; }

    private void Next()
    {
        if (IsLast) { Finish(); return; }
        Go(_index + 1);
    }

    private void Back()
    {
        if (_index > 0) Go(_index - 1);
    }

    private void Go(int index)
    {
        _index = index;
        _navigate(_steps[_index].Page);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(StepText));
        OnPropertyChanged(nameof(IsFirst));
        OnPropertyChanged(nameof(IsLast));
        OnPropertyChanged(nameof(NextText));
    }

    private void Finish()
    {
        _navigate(PageKey.Home);
        _finished();
    }
}
