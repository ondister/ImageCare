using System;
using System.Threading.Tasks;

namespace ImageCare.UI.Avalonia.Services.MediaPlayer;

internal interface IMediaPlayerAdapter : IDisposable
{
    event EventHandler? StateChanged;

    event EventHandler? PositionChanged;

    event Action<Exception>? Failed;

    string Source { get; set; }

    TimeSpan Position { get; set; }

    TimeSpan Duration { get; }

    TimeSpan Remaining { get; }

    double Percent { get; set; }

    bool IsSeekable { get; }

    double Volume { get; set; }

    bool IsPlaying { get; }

    bool HasMediaLoaded { get; }

    void Attach();

    void Detach();

    Task LoadAsync();

    Task PlayAsync();

    void TogglePause();

    void Stop();
}
