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
        else if (OperatingSystem.IsWindows())
        {
            try
            {
                var nativePlayerDirectory = Path.Combine(AppContext.BaseDirectory, "player-native");
                var nativeLibraryPath = Path.Combine(nativePlayerDirectory, "libmpv-2.dll");
                if (!File.Exists(nativeLibraryPath))
                {
                    throw new FileNotFoundException(
                        "The native Windows video player library was not deployed.",
                        nativeLibraryPath);
                }

                MpvApi.RootPath = nativePlayerDirectory;
                var mpvView = new MpvView();
                mpvView.Bind(MpvView.MpvContextProperty, new Binding(nameof(MainVideoViewModel.Mpv))
                {
                    Mode = BindingMode.OneWayToSource
                });
                VideoHost.Content = mpvView;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to initialize the Windows video player");
                VideoHost.Content = new TextBlock
                {
                    Text = "Video playback is unavailable. See the application log for details.",
                    TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Thickness(24)
                };
            }
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
