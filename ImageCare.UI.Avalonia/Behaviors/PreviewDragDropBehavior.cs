using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

using ImageCare.UI.Avalonia.ViewModels.Domain;

namespace ImageCare.UI.Avalonia.Behaviors;

public sealed class PreviewDragDropBehavior
{
    public static readonly AttachedProperty<bool> IsDragSourceProperty =
        AvaloniaProperty.RegisterAttached<PreviewDragDropBehavior, Control, bool>(
            "IsDragSource",
            defaultValue: false);

    public static readonly AttachedProperty<ImagePreviewDropHandler?> DropHandlerProperty =
        AvaloniaProperty.RegisterAttached<PreviewDragDropBehavior, ListBox, ImagePreviewDropHandler?>(
            "DropHandler");

    private static readonly AttachedProperty<DragSourceState?> DragSourceStateProperty =
        AvaloniaProperty.RegisterAttached<PreviewDragDropBehavior, Control, DragSourceState?>(
            "DragSourceState");

    private static object? _sourceContext;
    private static DragPreviewState? _dragPreview;

    static PreviewDragDropBehavior()
    {
        IsDragSourceProperty.Changed.AddClassHandler<Control>(OnIsDragSourceChanged);
        DropHandlerProperty.Changed.AddClassHandler<ListBox>(OnDropHandlerChanged);
    }

    public static bool GetIsDragSource(Control control) => control.GetValue(IsDragSourceProperty);

    public static void SetIsDragSource(Control control, bool value) =>
        control.SetValue(IsDragSourceProperty, value);

    public static ImagePreviewDropHandler? GetDropHandler(ListBox listBox) =>
        listBox.GetValue(DropHandlerProperty);

    public static void SetDropHandler(ListBox listBox, ImagePreviewDropHandler? value) =>
        listBox.SetValue(DropHandlerProperty, value);

    private static void OnIsDragSourceChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.PointerPressed -= OnPointerPressed;
        control.PointerMoved -= OnPointerMoved;
        control.PointerReleased -= OnPointerReleased;

        if (args.NewValue is true)
        {
            control.PointerPressed += OnPointerPressed;
            control.PointerMoved += OnPointerMoved;
            control.PointerReleased += OnPointerReleased;
        }
    }

    private static void OnDropHandlerChanged(ListBox listBox, AvaloniaPropertyChangedEventArgs args)
    {
        listBox.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
        listBox.RemoveHandler(DragDrop.DropEvent, OnDrop);

        if (args.NewValue is ImagePreviewDropHandler)
        {
            DragDrop.SetAllowDrop(listBox, true);
            listBox.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            listBox.AddHandler(DragDrop.DropEvent, OnDrop);
        }
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control ||
            !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        control.SetValue(
            DragSourceStateProperty,
            new DragSourceState(e, e.GetPosition(control), control.DataContext));
    }

    private static async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Control control ||
            control.GetValue(DragSourceStateProperty) is not { } state ||
            state.IsDragging ||
            !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var currentPosition = e.GetPosition(control);
        if (Math.Abs(currentPosition.X - state.StartPosition.X) < 4 &&
            Math.Abs(currentPosition.Y - state.StartPosition.Y) < 4)
        {
            return;
        }

        state.IsDragging = true;
        _sourceContext = state.SourceContext;
        ShowDragPreview(control, state.SourceContext, e);

        var item = new DataTransferItem();
        item.SetText((state.SourceContext as MediaPreviewViewModel)?.Url ?? string.Empty);
        var data = new DataTransfer();
        data.Add(item);

        try
        {
            await DragDrop.DoDragDropAsync(
                state.PointerPressedEventArgs,
                data,
                DragDropEffects.Copy | DragDropEffects.Move);
        }
        finally
        {
            HideDragPreview();
            _sourceContext = null;
            control.ClearValue(DragSourceStateProperty);
        }
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control control &&
            control.GetValue(DragSourceStateProperty) is { IsDragging: false })
        {
            control.ClearValue(DragSourceStateProperty);
        }
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not ListBox listBox || GetDropHandler(listBox) is not { } handler)
        {
            return;
        }

        e.DragEffects = GetRequestedEffect(e.KeyModifiers);
        if (!handler.Validate(listBox, e, _sourceContext, listBox.DataContext, null))
        {
            e.DragEffects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private static async void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not ListBox listBox || GetDropHandler(listBox) is not { } handler)
        {
            return;
        }

        e.DragEffects = GetRequestedEffect(e.KeyModifiers);
        e.Handled = true;
        await handler.ExecuteAsync(listBox, e, _sourceContext, listBox.DataContext, null);
    }

    private static void ShowDragPreview(Control source, object? sourceContext, PointerEventArgs pointerEventArgs)
    {
        HideDragPreview();

        if (sourceContext is not MediaPreviewViewModel { PreviewBitmap: { } bitmap } ||
            TopLevel.GetTopLevel(source) is not { } topLevel ||
            OverlayLayer.GetOverlayLayer(source) is not { } overlayLayer)
        {
            return;
        }

        var previewImage = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform
        };
        var previewFrame = new Border
        {
            Width = 180,
            Height = 130,
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(180, 24, 24, 24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Opacity = 0.62,
            IsHitTestVisible = false,
            Child = previewImage
        };
        var overlay = new Canvas
        {
            IsHitTestVisible = false
        };
        overlay.Children.Add(previewFrame);
        overlayLayer.Children.Add(overlay);

        var previousAllowDrop = DragDrop.GetAllowDrop(topLevel);
        DragDrop.SetAllowDrop(topLevel, true);
        topLevel.AddHandler(
            DragDrop.DragOverEvent,
            OnTopLevelDragOver,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);

        _dragPreview = new DragPreviewState(
            topLevel,
            overlayLayer,
            overlay,
            previewFrame,
            previousAllowDrop);
        UpdateDragPreview(pointerEventArgs.GetPosition(topLevel));
    }

    private static void OnTopLevelDragOver(object? sender, DragEventArgs e)
    {
        if (_dragPreview is { TopLevel: var topLevel })
        {
            UpdateDragPreview(e.GetPosition(topLevel));
        }
    }

    private static void UpdateDragPreview(Point pointerPosition)
    {
        if (_dragPreview is not { PreviewFrame: var previewFrame })
        {
            return;
        }

        Canvas.SetLeft(previewFrame, pointerPosition.X + 16);
        Canvas.SetTop(previewFrame, pointerPosition.Y + 16);
    }

    private static void HideDragPreview()
    {
        if (_dragPreview is not { } preview)
        {
            return;
        }

        preview.TopLevel.RemoveHandler(DragDrop.DragOverEvent, OnTopLevelDragOver);
        DragDrop.SetAllowDrop(preview.TopLevel, preview.PreviousAllowDrop);
        preview.OverlayLayer.Children.Remove(preview.Overlay);
        _dragPreview = null;
    }

    private static DragDropEffects GetRequestedEffect(KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Alt)
            ? DragDropEffects.Copy
            : DragDropEffects.Move;

    private sealed class DragSourceState(
        PointerPressedEventArgs pointerPressedEventArgs,
        Point startPosition,
        object? sourceContext)
    {
        public PointerPressedEventArgs PointerPressedEventArgs { get; } = pointerPressedEventArgs;
        public Point StartPosition { get; } = startPosition;
        public object? SourceContext { get; } = sourceContext;
        public bool IsDragging { get; set; }
    }

    private sealed record DragPreviewState(
        TopLevel TopLevel,
        OverlayLayer OverlayLayer,
        Canvas Overlay,
        Border PreviewFrame,
        bool PreviousAllowDrop);
}
