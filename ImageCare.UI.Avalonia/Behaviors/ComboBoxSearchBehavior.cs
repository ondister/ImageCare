using System;

using Avalonia;
using Avalonia.Controls;

namespace ImageCare.UI.Avalonia.Behaviors;

internal sealed class ComboBoxSearchBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ComboBoxSearchBehavior, ComboBox, bool>("IsEnabled");

    static ComboBoxSearchBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<ComboBox>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(ComboBox comboBox) => comboBox.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ComboBox comboBox, bool value) => comboBox.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(ComboBox comboBox, AvaloniaPropertyChangedEventArgs args)
    {
        comboBox.PropertyChanged -= OnComboBoxPropertyChanged;
        if (args.NewValue is true)
        {
            comboBox.PropertyChanged += OnComboBoxPropertyChanged;
        }
    }

    private static void OnComboBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (sender is not ComboBox comboBox || args.Property != ComboBox.TextProperty)
        {
            return;
        }

        comboBox.IsDropDownOpen = !string.IsNullOrWhiteSpace(comboBox.Text);
    }
}
