using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SnapVox;

/// <summary>
/// Where SnapVox keeps its files. Installed: %APPDATA%\SnapVox and %LOCALAPPDATA%\SnapVox.
/// Portable: a "SnapVox Data" folder next to the exe (or %LOCALAPPDATA%\SnapVox if that folder is read-only).
/// The portable build's native libraries always live in the user's profile, never next to the exe.
/// </summary>
internal static partial class AppPaths
{
    private const string PayloadName = "SnapVox.Portable.zip";
    private const string HashesEntry = "portable-hashes.txt";
    private const uint LoadLibrarySearchUserDirs = 0x400, LoadLibrarySearchSystem32 = 0x800;

#if PORTABLE
    public static bool IsPortable => true;
#else
    public static bool IsPortable => false;
#endif

    // Kept open (read-only sharing) so a verified library can't be swapped before Windows loads it.
    private static readonly List<FileStream> LockedLibraries = [];

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
    /// Portable build: stops Windows from loading libraries from the exe's folder (it is often Downloads),
    /// unpacks the embedded libraries to the user's profile and verifies each one on every start.
    /// Must run before any Avalonia or sherpa-onnx code.
    /// </summary>
    public static void PreparePortableRuntime()
    {
        if (!IsPortable)
            return;
        if (!SetDefaultDllDirectories(LoadLibrarySearchSystem32 | LoadLibrarySearchUserDirs))
            throw new InvalidOperationException("Couldn't restrict the library search.");
        // .NET looks in the "app folder" first. Until the verified folder is ready, that means System32.
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", Environment.SystemDirectory + Path.DirectorySeparatorChar);

        using var payload = typeof(AppPaths).Assembly.GetManifestResourceStream(PayloadName)
            ?? throw new InvalidOperationException("The portable payload is missing.");
        using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
        var (hashes, id) = ReadHashes(zip);

        string runtimes = Path.Combine(LocalFallback, "portable-runtime");
        string target = Path.Combine(runtimes, $"{typeof(AppPaths).Assembly.GetName().Version}-{id}");

        using var gate = new Mutex(false, @"Local\SnapVox.PortableUnpack");
        try
        {
            gate.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // The previous holder died; we own the mutex now.
        }
        try
        {
            Directory.CreateDirectory(target);
            foreach (var (name, hash) in hashes)
                LockedLibraries.Add(OpenVerified(zip, target, name, hash));
            RemoveOldRuntimes(runtimes, target);
            RemoveLegacyRuntime();
        }
        finally
        {
            gate.ReleaseMutex();
        }

        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", target + Path.DirectorySeparatorChar);
        if (AddDllDirectory(target) == IntPtr.Zero)
            throw new InvalidOperationException("Couldn't set the library folder.");
    }

    private static (List<(string Name, string Hash)> Hashes, string Id) ReadHashes(ZipArchive zip)
    {
        var entry = zip.GetEntry(HashesEntry) ?? throw new InvalidDataException("The portable payload has no hashes.");
        using var reader = new StreamReader(entry.Open(), Encoding.ASCII);
        string text = reader.ReadToEnd();

        var hashes = new List<(string, string)>();
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', 2);
            if (parts.Length != 2 || parts[0].Length != 64 || parts[1] != Path.GetFileName(parts[1]))
                throw new InvalidDataException("Invalid portable hash list.");
            hashes.Add((parts[1], parts[0]));
        }
        if (hashes.Count == 0)
            throw new InvalidDataException("Empty portable hash list.");
        return (hashes, Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(text)))[..16]);
    }

    /// <summary>Opens the library if its hash is right; otherwise restores it from the exe and checks again.</summary>
    private static FileStream OpenVerified(ZipArchive zip, string folder, string name, string expected)
    {
        string path = Path.Combine(folder, name);
        if (TryOpenVerified(path, expected) is { } existing)
            return existing;

        var entry = zip.GetEntry(name) ?? throw new InvalidDataException("Missing library: " + name);
        entry.ExtractToFile(path, overwrite: true);
        return TryOpenVerified(path, expected) ?? throw new InvalidDataException("Library failed verification: " + name);
    }

    private static FileStream? TryOpenVerified(string path, string expected)
    {
        if (!File.Exists(path))
            return null;
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (Convert.ToHexString(SHA256.HashData(file)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            return file;
        file.Dispose();
        return null;
    }

    private static void RemoveOldRuntimes(string runtimes, string keep)
    {
        foreach (string dir in Directory.GetDirectories(runtimes))
        {
            if (!string.Equals(dir, keep, StringComparison.OrdinalIgnoreCase))
                TryDeleteFolder(dir);
        }
    }

    /// <summary>Versions before 0.1.2 unpacked the libraries next to the exe.</summary>
    private static void RemoveLegacyRuntime()
    {
        if (PortableRoot is not null)
            TryDeleteFolder(Path.Combine(PortableRoot, "runtime"));
    }

    private static void TryDeleteFolder(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // still in use by another copy
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDefaultDllDirectories(uint flags);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr AddDllDirectory(string path);
}
