using Avalonia.Controls;
using Avalonia.Interactivity;

using ImageCare.UI.Avalonia.ViewModels.Domain;

namespace ImageCare.UI.Avalonia.Views;

public partial class FoldersView : UserControl
{
    public FoldersView()
    {
        InitializeComponent();
    }

    private static void OnNameTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: DirectoryViewModel viewModel } &&
            viewModel.NameTextBoxLostFocusCommand.CanExecute(null))
        {
            viewModel.NameTextBoxLostFocusCommand.Execute(null);
        }
    }
}
