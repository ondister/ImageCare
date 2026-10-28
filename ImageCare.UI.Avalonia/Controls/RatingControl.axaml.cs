using System;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace ImageCare.UI.Avalonia.Controls;

public partial class RatingControl : UserControl
{
    public static readonly StyledProperty<int> RatingProperty =
        AvaloniaProperty.Register<RatingControl, int>(
            nameof(Rating),
            defaultBindingMode: BindingMode.TwoWay);

    private bool _isInitialized;

    public RatingControl()
    {
        InitializeComponent();
        _isInitialized = true;
        UpdateStars();
    }

    public int Rating
    {
        get => GetValue(RatingProperty);
        set => SetValue(RatingProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (_isInitialized && change.Property == RatingProperty)
        {
            UpdateStars();
        }
    }

    private void OnStarClick(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button button ||
            !int.TryParse(button.Tag?.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var clickedRating))
        {
            return;
        }

        SetCurrentValue(RatingProperty, Rating == clickedRating ? 0 : clickedRating);
    }

    private void UpdateStars()
    {
        var rating = Math.Clamp(Rating, 0, 5);
        UpdateStar(Star1Button, rating >= 1);
        UpdateStar(Star2Button, rating >= 2);
        UpdateStar(Star3Button, rating >= 3);
        UpdateStar(Star4Button, rating >= 4);
        UpdateStar(Star5Button, rating >= 5);
    }

    private static void UpdateStar(Button button, bool isFilled)
    {
        button.Classes.Set("filled", isFilled);
    }
}
