using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SnapVox.Platform.Windows;

/// <summary>
/// Language detection through Windows' built-in service ("Microsoft Language Detection",
/// Extended Linguistic Services). No extra packages, offline and lightweight.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class WinLanguageDetector : ILanguageDetector, IDisposable
{
    private static readonly Guid LangDetectService = new("CF7E00B1-909B-4D95-A8F4-611F7C377702");
    private IntPtr _service;

    public WinLanguageDetector()
    {
        var guid = LangDetectService;
        var options = new MAPPING_ENUM_OPTIONS { Size = (nuint)sizeof(MAPPING_ENUM_OPTIONS), pGuid = (IntPtr)(&guid) };
        int hr = MappingGetServices(ref options, out _service, out uint count);
        if (hr < 0 || count == 0)
        {
            _service = IntPtr.Zero;
            Log.Info($"Windows language detector unavailable (HRESULT 0x{hr:X8}).");
        }
    }

    public IReadOnlyList<string> DetectAll(string text)
    {
        if (_service == IntPtr.Zero || string.IsNullOrWhiteSpace(text))
            return [];

        var bag = new MAPPING_PROPERTY_BAG { Size = (nuint)sizeof(MAPPING_PROPERTY_BAG) };
        fixed (char* p = text)
        {
            int hr = MappingRecognizeText(_service, p, (uint)text.Length, 0, IntPtr.Zero, ref bag);
            if (hr < 0)
                return [];
        }

        try
        {
            if (bag.dwRangesCount == 0 || bag.prgResultRanges == IntPtr.Zero)
                return [];

            // The result is a list "pt\0es\0\0" ordered from most to least likely.
            var range = (MAPPING_DATA_RANGE*)bag.prgResultRanges;
            var result = new List<string>();
            char* s = (char*)range->pData;
            char* end = (char*)((byte*)range->pData + range->dwDataSize);
            while (s < end && *s != '\0')
            {
                string tag = new(s);
                s += tag.Length + 1;
                if (tag == "und")
                    continue;
                string code = tag.Split('-')[0].ToLowerInvariant();
                if (!result.Contains(code))
                    result.Add(code);
            }
            return result;
        }
        finally
        {
            MappingFreePropertyBag(ref bag);
        }
    }

    public void Dispose()
    {
        if (_service != IntPtr.Zero)
        {
            MappingFreeServices(_service);
            _service = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MAPPING_ENUM_OPTIONS
    {
        public nuint Size;
        public IntPtr pszCategory, pszInputLanguage, pszOutputLanguage, pszInputScript, pszOutputScript;
        public IntPtr pszInputContentType, pszOutputContentType;
        public IntPtr pGuid;
        public uint Flags; // OnlineService:2, ServiceType:2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MAPPING_PROPERTY_BAG
    {
        public nuint Size;
        public IntPtr prgResultRanges;
        public uint dwRangesCount;
        public IntPtr pServiceData;
        public uint dwServiceDataSize;
        public IntPtr pCallerData;
        public uint dwCallerDataSize;
        public IntPtr pContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MAPPING_DATA_RANGE
    {
        public uint dwStartIndex;
        public uint dwEndIndex;
        public IntPtr pszDescription;
        public uint dwDescriptionLength;
        public IntPtr pData;
        public uint dwDataSize;
        public IntPtr pszContentType;
        public IntPtr prgActionIds;
        public uint dwActionsCount;
        public IntPtr prgActionDisplayNames;
    }

    [LibraryImport("elscore.dll")]
    private static partial int MappingGetServices(ref MAPPING_ENUM_OPTIONS options, out IntPtr services, out uint count);

    [LibraryImport("elscore.dll")]
    private static partial int MappingFreeServices(IntPtr services);

    [LibraryImport("elscore.dll")]
    private static partial int MappingRecognizeText(IntPtr service, char* text, uint length, uint index, IntPtr options, ref MAPPING_PROPERTY_BAG bag);

    [LibraryImport("elscore.dll")]
    private static partial int MappingFreePropertyBag(ref MAPPING_PROPERTY_BAG bag);
}
