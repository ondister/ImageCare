using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ImageCare.UI.Avalonia.Controls;

public partial class MediaPreviewCard : UserControl
{
    public static readonly StyledProperty<ICommand?> SelectCommandProperty =
        AvaloniaProperty.Register<MediaPreviewCard, ICommand?>(nameof(SelectCommand));

    public static readonly StyledProperty<object?> SelectCommandParameterProperty =
        AvaloniaProperty.Register<MediaPreviewCard, object?>(nameof(SelectCommandParameter));

    public MediaPreviewCard()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
    }

    public ICommand? SelectCommand
    {
        get => GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    public object? SelectCommandParameter
    {
        get => GetValue(SelectCommandParameterProperty);
        set => SetValue(SelectCommandParameterProperty, value);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            SelectCommand?.CanExecute(SelectCommandParameter) != true)
        {
            return;
        }

        SelectCommand.Execute(SelectCommandParameter);
    }
}
