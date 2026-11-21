using System.IO.Compression;
using System.Runtime.InteropServices;

namespace SnapVox;

/// <summary>
/// Where SnapVox keeps its files. Installed: %APPDATA%\SnapVox and %LOCALAPPDATA%\SnapVox.
/// Portable: a "SnapVox Data" folder next to the exe (or %LOCALAPPDATA%\SnapVox if that folder is read-only).
/// </summary>
internal static partial class AppPaths
{
    private const string PayloadName = "SnapVox.Portable.zip";

#if PORTABLE
    public static bool IsPortable => true;
#else
    public static bool IsPortable => false;
#endif

    private static readonly string LocalFallback = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapVox");

    private static readonly string? PortableRoot = IsPortable ? FindPortableRoot() : null;

    /// <summary>Settings folder.</summary>
    public static string Roaming { get; } = PortableRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapVox");

    /// <summary>Log and downloaded voices.</summary>
    public static string Local { get; } = PortableRoot ?? LocalFallback;

    private static string? FindPortableRoot()
    {
        if (Path.GetDirectoryName(Environment.ProcessPath) is not { } exeDir)
            return null;
        string root = Path.Combine(exeDir, "SnapVox Data");
        try
        {
            Directory.CreateDirectory(root);
            string probe = Path.Combine(root, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return root;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // read-only location (e.g. a locked USB drive)
        }
    }

    /// <summary>
    /// Portable build: unpacks the native libraries embedded in the exe (only the first time)
    /// and tells Windows to load them from there. Must run before any Avalonia or sherpa-onnx code.
    /// </summary>
    public static void PreparePortableRuntime()
    {
        if (!IsPortable)
            return;
        using var payload = typeof(AppPaths).Assembly.GetManifestResourceStream(PayloadName)
            ?? throw new InvalidOperationException("The portable payload is missing.");

        string runtimes = Path.Combine(Local, "runtime");
        string target = Path.Combine(runtimes, $"{typeof(AppPaths).Assembly.GetName().Version}-{payload.Length}");
        string marker = Path.Combine(target, ".complete");

        using var gate = new Mutex(false, @"Local\SnapVox.PortableUnpack");
        gate.WaitOne();
        try
        {
            if (!File.Exists(marker))
            {
                Directory.CreateDirectory(target);
                using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
                zip.ExtractToDirectory(target, overwriteFiles: true);
                File.WriteAllText(marker, "");
                RemoveOldRuntimes(runtimes, target);
            }
        }
        finally
        {
            gate.ReleaseMutex();
        }

        if (!SetDllDirectoryW(target))
            throw new InvalidOperationException("Couldn't set the library folder.");
    }

    private static void RemoveOldRuntimes(string runtimes, string keep)
    {
        foreach (string dir in Directory.GetDirectories(runtimes))
        {
            if (string.Equals(dir, keep, StringComparison.OrdinalIgnoreCase))
                continue;
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* still in use */ }
        }
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDllDirectoryW(string path);
}
