using System.Windows.Input;
using MaidInstaller.App.Mvvm;

namespace MaidInstaller.App.ViewModels;

public sealed record DialogItem(string Name, string Detail);

/// <summary>
/// Janela de confirmação dentro do app. Usada antes de qualquer ação que instale ou altere algo:
/// mostra exatamente o que será feito e só segue se o usuário confirmar.
/// </summary>
public sealed class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _result = new();

    public DialogViewModel(string title, string message, IReadOnlyList<DialogItem> items, IReadOnlyList<string> notes,
        string confirmText, string? cancelText = "Cancelar")
    {
        Title = title;
        Message = message;
        Items = items;
        Notes = notes;
        ConfirmText = confirmText;
        CancelText = cancelText;
        ConfirmCommand = new RelayCommand(() => _result.TrySetResult(true));
        CancelCommand = new RelayCommand(() => _result.TrySetResult(false));
    }

    public string Title { get; }
    public string Message { get; }
    public IReadOnlyList<DialogItem> Items { get; }
    public IReadOnlyList<string> Notes { get; }
    public string ConfirmText { get; }
    public string? CancelText { get; }
    public bool HasCancel => CancelText is not null;
    public bool HasItems => Items.Count > 0;
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }
    public Task<bool> Result => _result.Task;
}
