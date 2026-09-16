using DragonDiskForge.Core.Models;

namespace DragonDiskForge.Core.Services;

public static class SupportedFormats
{
    public static IReadOnlyList<SupportedFormat> All { get; } = new[]
    {
        new SupportedFormat("ISO", new[] { ".iso" }, true),
        new SupportedFormat("IMG / RAW", new[] { ".img", ".raw", ".dd" }, false),
        new SupportedFormat("IMA / Floppy", new[] { ".ima", ".flp" }, false),
        new SupportedFormat("BIN/CUE", new[] { ".bin", ".cue" }, false),
        new SupportedFormat("MDF/MDS", new[] { ".mdf", ".mds" }, false),
        new SupportedFormat("NRG", new[] { ".nrg" }, false),
        new SupportedFormat("CCD", new[] { ".ccd", ".sub" }, false),
        new SupportedFormat("VHD", new[] { ".vhd" }, true),
        new SupportedFormat("VHDX", new[] { ".vhdx" }, true),
        new SupportedFormat("VMDK", new[] { ".vmdk" }, false),
        new SupportedFormat("QCOW/QCOW2", new[] { ".qcow", ".qcow2" }, false),
        new SupportedFormat("DMG", new[] { ".dmg" }, false),
        new SupportedFormat("WIM/ESD", new[] { ".wim", ".esd" }, false),
        new SupportedFormat("FFU", new[] { ".ffu" }, false)
    };

    private static readonly Dictionary<string, SupportedFormat> ByExtension = All
        .SelectMany(format => format.Extensions.Select(ext => new KeyValuePair<string, SupportedFormat>(ext, format)))
        .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

    public static SupportedFormat? FromPath(string path)
        => ByExtension.GetValueOrDefault(System.IO.Path.GetExtension(path));
}
