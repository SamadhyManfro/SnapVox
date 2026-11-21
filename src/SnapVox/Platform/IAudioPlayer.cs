namespace SnapVox.Platform;

/// <summary>Plays the generated audio. Speed changes instantly without changing the pitch.</summary>
public interface IAudioPlayer : IDisposable
{
    Task LoadAsync(byte[] audio, string mimeType);
    void Play();
    void Pause();
    bool IsPlaying { get; }
    TimeSpan Position { get; set; }
    TimeSpan Duration { get; }
    double Rate { get; set; }

    /// <summary>Finished playing (raised on another thread).</summary>
    event Action? Ended;
}
