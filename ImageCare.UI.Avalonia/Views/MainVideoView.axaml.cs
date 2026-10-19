using System;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.VisualTree;

using HanumanInstitute.LibMpv.Avalonia;
using HanumanInstitute.LibMpv.Core;

using ImageCare.UI.Avalonia.Platforms.MacOS;
using ImageCare.UI.Avalonia.ViewModels;

namespace ImageCare.UI.Avalonia.Views;

public partial class MainVideoView : UserControl
{
    private readonly MacVideoPlayerView? _macPlayer;
    private MainVideoViewModel? _attachedViewModel;

    public MainVideoView()
    {
        InitializeComponent();

        if (OperatingSystem.IsMacOS())
        {
            _macPlayer = new MacVideoPlayerView();
            VideoHost.Content = _macPlayer;
        }
        else
        {
            MpvApi.RootPath = Path.Combine(AppContext.BaseDirectory, "player-native");
            var mpvView = new MpvView();
            mpvView.Bind(MpvView.MpvContextProperty, new Binding(nameof(MainVideoViewModel.Mpv))
            {
                Mode = BindingMode.OneWayToSource
            });
            VideoHost.Content = mpvView;
        }

        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachPlayerToViewModel(DataContext as MainVideoViewModel);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachPlayerToViewModel(DataContext as MainVideoViewModel);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachPlayerToViewModel(null);
    }

    private void AttachPlayerToViewModel(MainVideoViewModel? viewModel)
    {
        if (ReferenceEquals(_attachedViewModel, viewModel))
        {
            return;
        }

        if (_attachedViewModel != null)
        {
            _attachedViewModel.MacPlayer = null;
        }

        _attachedViewModel = viewModel;

        if (_attachedViewModel != null)
        {
            _attachedViewModel.MacPlayer = _macPlayer;
        }
    }
}
