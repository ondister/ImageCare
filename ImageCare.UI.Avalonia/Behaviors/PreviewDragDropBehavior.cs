using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

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

        var item = new DataTransferItem();
        item.SetText(state.SourceContext?.ToString() ?? string.Empty);
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

    private static void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not ListBox listBox || GetDropHandler(listBox) is not { } handler)
        {
            return;
        }

        e.DragEffects = GetRequestedEffect(e.KeyModifiers);
        handler.Execute(listBox, e, _sourceContext, listBox.DataContext, null);
        e.Handled = true;
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
}
