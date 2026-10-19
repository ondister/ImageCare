using System;
using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using ImageCare.UI.Avalonia.Services;
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
        DragDrop.SetAllowDrop(listBox, args.NewValue is ImagePreviewDropHandler);
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        var point = e.GetCurrentPoint(control);
        var modifiers = PlatformModifierKeys.GetCurrent(e.KeyModifiers);
        var isMacControlClick = OperatingSystem.IsMacOS() &&
                                modifiers.HasFlag(KeyModifiers.Control) &&
                                point.Properties.IsRightButtonPressed;
        if (!point.Properties.IsLeftButtonPressed && !isMacControlClick)
        {
            return;
        }

        var sourceListBox = control.FindAncestorOfType<ListBox>();
        var dropHandler = sourceListBox == null ? null : GetDropHandler(sourceListBox);
        control.SetValue(
            DragSourceStateProperty,
            new DragSourceState(
                e.GetPosition(control),
                control.DataContext,
                isMacControlClick,
                dropHandler));
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Control control ||
            control.GetValue(DragSourceStateProperty) is not { } state)
        {
            return;
        }

        if (!state.IsDragging)
        {
            var point = e.GetCurrentPoint(control);
            var dragButtonPressed = point.Properties.IsLeftButtonPressed ||
                                    (state.IsMacControlClick && point.Properties.IsRightButtonPressed);
            if (!dragButtonPressed)
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
            e.Pointer.Capture(control);
            state.DropHandler?.BeginDrag(e.KeyModifiers);
            ShowDragPreview(control, state.SourceContext, e, state.DropHandler);
        }

        state.DropHandler?.UpdateOperation(e.KeyModifiers);
        if (TopLevel.GetTopLevel(control) is { } topLevel)
        {
            UpdateDragPreview(e.GetPosition(topLevel));
        }

        e.Handled = true;
    }

    private static async void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control control ||
            control.GetValue(DragSourceStateProperty) is not { } state)
        {
            return;
        }

        control.ClearValue(DragSourceStateProperty);
        e.Pointer.Capture(null);
        if (!state.IsDragging)
        {
            return;
        }

        e.Handled = true;
        var operation = state.DropHandler?.UpdateOperation(e.KeyModifiers)
                        ?? FileTransferOperation.Move;
        var topLevel = TopLevel.GetTopLevel(control);
        var targetListBox = topLevel == null
            ? null
            : FindDropTarget(topLevel, e.GetPosition(topLevel));
        var targetHandler = targetListBox == null ? null : GetDropHandler(targetListBox);

        HideDragPreview();
        try
        {
            if (targetListBox != null && targetHandler != null)
            {
                await targetHandler.ExecuteAsync(
                    targetListBox,
                    state.SourceContext,
                    targetListBox.DataContext,
                    operation);
            }
        }
        finally
        {
            state.DropHandler?.EndDrag();
        }
    }

    private static ListBox? FindDropTarget(TopLevel topLevel, Point position)
    {
        if (topLevel.InputHitTest(position) is not Visual hit)
        {
            return null;
        }

        var listBox = hit as ListBox ?? hit.FindAncestorOfType<ListBox>();
        return listBox != null && GetDropHandler(listBox) != null ? listBox : null;
    }

    private static void ShowDragPreview(
        Control source,
        object? sourceContext,
        PointerEventArgs pointerEventArgs,
        ImagePreviewDropHandler? dropHandler)
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
        var copyBadge = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false,
            IsVisible = dropHandler?.CurrentOperation == FileTransferOperation.Copy,
            Child = new TextBlock
            {
                Text = "+",
                FontSize = 22,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            }
        };
        var operationSubscription = dropHandler?.OperationStatusChanged.Subscribe(status =>
        {
            copyBadge.IsVisible = status.Operation == FileTransferOperation.Copy;
        });
        var overlay = new Canvas
        {
            IsHitTestVisible = false
        };
        overlay.Children.Add(previewFrame);
        overlay.Children.Add(copyBadge);
        overlayLayer.Children.Add(overlay);

        _dragPreview = new DragPreviewState(
            overlayLayer,
            overlay,
            previewFrame,
            copyBadge,
            operationSubscription);
        UpdateDragPreview(pointerEventArgs.GetPosition(topLevel));
    }

    private static void UpdateDragPreview(Point pointerPosition)
    {
        if (_dragPreview is not { PreviewFrame: var previewFrame, CopyBadge: var copyBadge })
        {
            return;
        }

        const double offset = 16;
        Canvas.SetLeft(previewFrame, pointerPosition.X + offset);
        Canvas.SetTop(previewFrame, pointerPosition.Y + offset);
        Canvas.SetLeft(copyBadge, pointerPosition.X + offset + previewFrame.Width - 18);
        Canvas.SetTop(copyBadge, pointerPosition.Y + offset + previewFrame.Height - 18);
    }

    private static void HideDragPreview()
    {
        if (_dragPreview is not { } preview)
        {
            return;
        }

        preview.OperationSubscription?.Dispose();
        preview.OverlayLayer.Children.Remove(preview.Overlay);
        _dragPreview = null;
    }

    private sealed class DragSourceState(
        Point startPosition,
        object? sourceContext,
        bool isMacControlClick,
        ImagePreviewDropHandler? dropHandler)
    {
        public Point StartPosition { get; } = startPosition;
        public object? SourceContext { get; } = sourceContext;
        public bool IsMacControlClick { get; } = isMacControlClick;
        public ImagePreviewDropHandler? DropHandler { get; } = dropHandler;
        public bool IsDragging { get; set; }
    }

    private sealed record DragPreviewState(
        OverlayLayer OverlayLayer,
        Canvas Overlay,
        Border PreviewFrame,
        Border CopyBadge,
        IDisposable? OperationSubscription);
}
