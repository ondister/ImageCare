using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;

using ImageCare.UI.Avalonia.Controls;

using Prism.Commands;
using Prism.Ioc;

namespace ImageCare.UI.Avalonia.Views;

public partial class MainImageView : UserControl
{
    public static readonly StyledProperty<ICommand> ResetMatrixCommandProperty = AvaloniaProperty.Register<MainImageView, ICommand>(nameof(ResetMatrixCommand));

    public MainImageView(IContainerProvider containerProvider)
    {
        InitializeComponent();

        ResetMatrixCommand = new DelegateCommand(ResetMatrix);

        var mapMediator = containerProvider.Resolve<MapControlMediator>();
        mapMediator.SetMapControl(OsmMapControl);


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