using System;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.VisualTree;

using HanumanInstitute.LibMpv.Avalonia;
using HanumanInstitute.LibMpv.Core;

using ImageCare.UI.Avalonia.Platforms.MacOS;
using ImageCare.UI.Avalonia.Services.MediaPlayer;
using ImageCare.UI.Avalonia.ViewModels;

namespace ImageCare.UI.Avalonia.Views;

public partial class MainVideoView : UserControl
{
    private readonly IMediaPlayerAdapter? _playerAdapter;
    private MainVideoViewModel? _attachedViewModel;
    private bool _isAttachedToVisualTree;

    public MainVideoView()
    {
        InitializeComponent();

        if (OperatingSystem.IsMacOS())
        {
            var macPlayer = new MacVideoPlayerView();
            _playerAdapter = new MacPlayerAdapter(macPlayer);
            VideoHost.Content = macPlayer;
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
                var mpvAdapter = new MpvPlayerAdapter();
                var mpvView = new MpvView
                {
                    DataContext = mpvAdapter
                };
                mpvView.Bind(MpvView.MpvContextProperty, new Binding(nameof(MpvPlayerAdapter.Context))
                {
                    Mode = BindingMode.OneWayToSource
                });

                _playerAdapter = mpvAdapter;
                VideoHost.Content = mpvView;
            }
            catch (Exception exception)
            {
                Serilog.Log.Error(exception, "Failed to initialize the Windows video player");
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

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_isAttachedToVisualTree)
        {
            AttachPlayerToViewModel(DataContext as MainVideoViewModel);
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (_isAttachedToVisualTree)
        {
            return;
        }

        _isAttachedToVisualTree = true;
        _playerAdapter?.Attach();
        AttachPlayerToViewModel(DataContext as MainVideoViewModel);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (!_isAttachedToVisualTree)
        {
            return;
        }

        AttachPlayerToViewModel(null);
        _playerAdapter?.Detach();
        _isAttachedToVisualTree = false;
    }

    private void AttachPlayerToViewModel(MainVideoViewModel? viewModel)
    {
        if (ReferenceEquals(_attachedViewModel, viewModel))
        {
            return;
        }

        if (_attachedViewModel != null)
        {
            _attachedViewModel.Player = null;
        }

        _attachedViewModel = viewModel;

        if (_attachedViewModel != null)
        {
            _attachedViewModel.Player = _playerAdapter;
        }
    }
}
