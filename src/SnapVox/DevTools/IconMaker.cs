using System.Buffers.Binary;
using SnapVox.UI;

namespace SnapVox.DevTools;

/// <summary>
/// Generates SnapVox.ico (several sizes, PNG inside the ICO) from the logo drawn in code.
/// Usage: SnapVox.exe --make-ico &lt;file.ico&gt;
/// </summary>
internal static class IconMaker
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    public static int Run(string path)
    {
        var images = Sizes.Select(size =>
        {
            using var ms = new MemoryStream();
            LogoRenderer.Render(size).Save(ms);
            return (size, data: ms.ToArray());
        }).ToList();

        using var output = File.Create(path);
        Span<byte> header = stackalloc byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(header, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 1); // 1 = icon
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)images.Count);
        output.Write(header);

        int offset = 6 + 16 * images.Count;
        Span<byte> entry = stackalloc byte[16];
        foreach (var (size, data) in images)
        {
            entry[0] = (byte)(size >= 256 ? 0 : size); // 0 = 256
            entry[1] = (byte)(size >= 256 ? 0 : size);
            entry[2] = 0; // colors (0 = more than 256)
            entry[3] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);  // planos
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32); // bits por pixel
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);
            output.Write(entry);
            offset += data.Length;
        }
        foreach (var (_, data) in images)
            output.Write(data);

        // Big logo for the GitHub README, in the same folder.
        LogoRenderer.Render(192).Save(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "readme-logo.png"));
        return 0;
    }
}
