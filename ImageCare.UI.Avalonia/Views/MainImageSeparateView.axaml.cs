using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;

using Prism.Commands.Ex;

namespace ImageCare.UI.Avalonia.Views;

public partial class MainImageSeparateView : UserControl
{
    public static readonly StyledProperty<ICommand> ResetMatrixCommandProperty = AvaloniaProperty.Register<MainImageView, ICommand>(nameof(ResetMatrixCommand));

    public MainImageSeparateView()
    {
        InitializeComponent();
        ResetMatrixCommand = new DelegateCommand(ResetMatrix);
    }

    public ICommand ResetMatrixCommand
    {
        get => GetValue(ResetMatrixCommandProperty);
        set => SetValue(ResetMatrixCommandProperty, value);
    }

    private void ResetMatrix()
    {
        ZoomBorder.ResetMatrix();
    }
}