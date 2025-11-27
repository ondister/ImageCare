using Ursa.Controls;

namespace ImageCare.UI.Common.Desktop.Views;

public partial class ChildWindow : UrsaWindow, IDialogWindow
{
    /// <inheritdoc />
    public IDialogResult Result { get; set; }

    public ChildWindow()
    {
        InitializeComponent();
    }
}