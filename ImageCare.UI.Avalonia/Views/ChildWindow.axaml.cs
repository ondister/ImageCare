using Avalonia.Controls;

using Prism.Dialogs;

namespace ImageCare.UI.Avalonia.Views;

public partial class ChildWindow : Window, IDialogWindow
{
    /// <inheritdoc />
    public IDialogResult? Result { get; set; }

    public ChildWindow()
    {
        InitializeComponent();
    }
}
