using Prism.Dialogs;

using Ursa.Controls;

namespace ImageCare.UI.Avalonia.Views;

public partial class ChildWindow : UrsaWindow, IDialogWindow
{
    /// <inheritdoc />
    public IDialogResult Result { get; set; }

    public ChildWindow()
    {
        InitializeComponent();
    }
}