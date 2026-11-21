using System.Buffers.Binary;

namespace SnapVox.Speech;

/// <summary>
/// Adds a little silence at the start of the audio. Many sound cards and headsets (especially Bluetooth)
/// "wake up" when sound starts and swallow the first milliseconds — with silence in front,
/// it's the silence that gets cut, not the first word. Word timings are shifted along.
/// </summary>
internal static class AudioPadding
{
    public static readonly TimeSpan Lead = TimeSpan.FromMilliseconds(300);

    public static SpeechAudio AddLeadingSilence(SpeechAudio audio)
    {
        var (bytes, added) = audio.MimeType switch
        {
            "audio/wav" => PadWav(audio.Audio),
            "audio/mpeg" => PadMp3(audio.Audio),
            _ => (audio.Audio, TimeSpan.Zero),
        };
        if (added == TimeSpan.Zero)
            return audio;

        var words = audio.Words.Select(w => w with { Start = w.Start + added }).ToList();
        return audio with { Audio = bytes, Words = words };
    }

    /// <summary>PCM WAV: inserts zeros at the start of the "data" chunk and fixes the header sizes.</summary>
    private static (byte[], TimeSpan) PadWav(byte[] wav)
    {
        if (wav.Length < 44 || BinaryPrimitives.ReadUInt32BigEndian(wav) != 0x52494646) // "RIFF"
            return (wav, TimeSpan.Zero);

        int pos = 12, byteRate = 0, blockAlign = 1;
        while (pos + 8 <= wav.Length)
        {
            uint id = BinaryPrimitives.ReadUInt32BigEndian(wav.AsSpan(pos));
            int size = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(pos + 4));
            if (id == 0x666D7420) // "fmt "
            {
                byteRate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(pos + 16));
                blockAlign = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(pos + 20));
            }
            else if (id == 0x64617461 && byteRate > 0) // "data"
            {
                int pad = (int)(byteRate * Lead.TotalSeconds) / blockAlign * blockAlign;
                var result = new byte[wav.Length + pad];
                Buffer.BlockCopy(wav, 0, result, 0, pos + 8);                         // headers
                Buffer.BlockCopy(wav, pos + 8, result, pos + 8 + pad, wav.Length - pos - 8); // data after the silence
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), result.Length - 8);
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(pos + 4), size + pad);
                return (result, TimeSpan.FromSeconds(pad / (double)byteRate));
            }
            pos += 8 + size + (size & 1);
        }
        return (wav, TimeSpan.Zero);
    }

    /// <summary>
    /// Edge MP3 (MPEG-2 layer III, 24 kHz, 48 kbit/s, mono): prepends frames of digital silence
    /// (valid header, the rest zeroed). Each frame has 576 samples = 24 ms.
    /// </summary>
    private static (byte[], TimeSpan) PadMp3(byte[] mp3)
    {
        // FF F3 = MPEG-2 layer III without CRC; 64 = 48 kbit/s, 24 kHz, no padding; 4th byte = mono (+ flags).
        if (mp3.Length < 4 || mp3[0] != 0xFF || mp3[1] != 0xF3 || mp3[2] != 0x64 || (mp3[3] & 0xC0) != 0xC0)
            return (mp3, TimeSpan.Zero); // not the expected format: leave it alone
        ReadOnlySpan<byte> header = mp3.AsSpan(0, 4).ToArray();

        const int frameBytes = 144; // 72 × 48000 / 24000
        int frames = (int)Math.Ceiling(Lead.TotalMilliseconds / 24);
        var result = new byte[frames * frameBytes + mp3.Length];
        for (int f = 0; f < frames; f++)
            header.CopyTo(result.AsSpan(f * frameBytes));
        mp3.CopyTo(result, frames * frameBytes);
        return (result, TimeSpan.FromMilliseconds(frames * 24));
    }
}
