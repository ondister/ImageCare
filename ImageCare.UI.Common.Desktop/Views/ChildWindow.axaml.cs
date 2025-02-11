using Prism.Services.Dialogs;

using Ursa.Controls;

namespace ImageCare.UI.Common.Desktop.Views;

public partial class ChildWindow : UrsaWindow, IDialogWindow
{
    public ChildWindow()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    public IDialogResult Result { get; set; }
}