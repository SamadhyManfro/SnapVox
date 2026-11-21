using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage.Streams;

namespace SnapVox.Platform.Windows;

/// <summary>
/// MP3 with the encoder that ships with Windows (Media Foundation), no extra library.
/// Edge audio is already MP3 and is saved as is.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed partial class WinAudioExporter : IAudioExporter
{
    public async Task<byte[]> ToMp3Async(byte[] audio, string mimeType)
    {
        if (mimeType == "audio/mpeg")
            return audio;

        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(audio.AsBuffer());
        input.Seek(0);
        using var output = new InMemoryRandomAccessStream();

        var transcoder = new MediaTranscoder();
        var profile = MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High);
        var prepared = await transcoder.PrepareStreamTranscodeAsync(input, output, profile);
        if (!prepared.CanTranscode)
            throw new InvalidOperationException("Windows couldn't convert to MP3: " + prepared.FailureReason);
        await prepared.TranscodeAsync();

        output.Seek(0);
        var bytes = new byte[output.Size];
        using var reader = new DataReader(output.GetInputStreamAt(0));
        await reader.LoadAsync((uint)output.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    public string DownloadsFolder
    {
        get
        {
            var downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            if (SHGetKnownFolderPath(downloads, 0, IntPtr.Zero, out var path) == 0)
            {
                try { return Marshal.PtrToStringUni(path)!; }
                finally { Marshal.FreeCoTaskMem(path); }
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid rfid, uint flags, IntPtr token, out IntPtr path);
}
