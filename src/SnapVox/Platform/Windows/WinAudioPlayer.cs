using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace SnapVox.Platform.Windows;

/// <summary>
/// Windows' own player (MediaPlayer). The playback rate changes instantly and Windows
/// keeps the pitch (time-stretch), so no extra library is needed for that.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class WinAudioPlayer : IAudioPlayer
{
    private readonly MediaPlayer _player = new() { AutoPlay = false, AudioCategory = MediaPlayerAudioCategory.Speech };
    private InMemoryRandomAccessStream? _stream;
    private double _rate = 1.0;

    public event Action? Ended;

    public WinAudioPlayer()
    {
        _player.MediaEnded += (_, _) => Ended?.Invoke();
        _player.MediaFailed += (_, e) => Log.Info($"Audio playback failed: {e.Error} {e.ErrorMessage}");
    }

    public async Task LoadAsync(byte[] audio, string mimeType)
    {
        _stream?.Dispose();
        _stream = new InMemoryRandomAccessStream();
        await _stream.WriteAsync(audio.AsBuffer());
        _stream.Seek(0);

        var source = MediaSource.CreateFromStream(_stream, mimeType);
        await source.OpenAsync(); // reads the header so the duration is known right away
        _player.Source = source;
        _player.PlaybackSession.PlaybackRate = _rate;
    }

    public void Play()
    {
        _player.Play();
        _player.PlaybackSession.PlaybackRate = _rate;
    }

    public void Pause() => _player.Pause();

    public bool IsPlaying => _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

    public TimeSpan Position
    {
        get => _player.PlaybackSession.Position;
        set => _player.PlaybackSession.Position = value;
    }

    public TimeSpan Duration => _player.PlaybackSession.NaturalDuration;

    public double Rate
    {
        get => _rate;
        set
        {
            _rate = value;
            _player.PlaybackSession.PlaybackRate = value;
        }
    }

    public void Dispose()
    {
        _player.Pause();
        _player.Source = null;
        _player.Dispose();
        _stream?.Dispose();
    }
}
