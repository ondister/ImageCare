using Avalonia.Controls;

using Prism.Services.Dialogs;

namespace ImageCare.Mvvm.Views;

public partial class ChildWindow : Window, IDialogWindow
{
    public ChildWindow()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    public IDialogResult Result { get; set; }
}