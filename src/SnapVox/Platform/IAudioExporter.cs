namespace SnapVox.Platform;

/// <summary>Converts the generated audio (WAV or MP3) into MP3 for saving.</summary>
public interface IAudioExporter
{
    Task<byte[]> ToMp3Async(byte[] audio, string mimeType);

    /// <summary>The user's real "Downloads" folder (it may have been moved to another drive).</summary>
    string DownloadsFolder { get; }
}
