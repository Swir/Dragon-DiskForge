using System.Security.Cryptography;
using System.Text;
using DragonDiskForge.Core.Services;

var failures = new List<string>();

void Check(bool condition, string name)
{
    if (condition)
    {
        Console.WriteLine($"PASS  {name}");
        return;
    }

    failures.Add(name);
    Console.Error.WriteLine($"FAIL  {name}");
}

async Task<string> WithTempFileAsync(string extension, byte[] content, Func<string, Task<string>> action)
{
    var path = Path.Combine(Path.GetTempPath(), $"dragon-diskforge-{Guid.NewGuid():N}{extension}");
    await File.WriteAllBytesAsync(path, content);
    try
    {
        return await action(path);
    }
    finally
    {
        File.Delete(path);
    }
}

var detector = new ImageDetectionService();
var verifier = new ImageVerificationService();

Check(SupportedFormats.FromPath("sample.ISO")?.Name == "ISO", "extension lookup is case-insensitive");
Check(SupportedFormats.FromPath("disk.qcow2")?.Name == "QCOW/QCOW2", "QCOW2 extension is catalogued");
Check(SupportedFormats.FromPath("unknown.xyz") is null, "unknown extension stays unknown");

var vhdx = new byte[64];
Encoding.ASCII.GetBytes("vhdxfile").CopyTo(vhdx, 0);
var vhdxFormat = await WithTempFileAsync(".bin", vhdx, async path => (await detector.InspectAsync(path)).Format);
Check(vhdxFormat == "VHDX", "VHDX signature overrides extension");

var qcow = new byte[64];
qcow[0] = 0x51;
qcow[1] = 0x46;
qcow[2] = 0x49;
qcow[3] = 0xFB;
var qcowFormat = await WithTempFileAsync(".img", qcow, async path => (await detector.InspectAsync(path)).Format);
Check(qcowFormat == "QCOW/QCOW2", "QCOW signature overrides extension");

var iso = new byte[0x9000];
Encoding.ASCII.GetBytes("CD001").CopyTo(iso, 0x8001);
var isoInfo = await WithTempFileAsync(".bin", iso, async path =>
{
    var info = await detector.InspectAsync(path);
    Check(info.DetectionMethod == "Signature", "ISO reports signature detection");
    Check(info.CanMount, "ISO is marked native-mount capable");
    return info.Format;
});
Check(isoInfo == "ISO", "ISO-9660 signature is detected");

var extensionFallback = await WithTempFileAsync(".vmdk", new byte[32], async path =>
{
    var info = await detector.InspectAsync(path);
    Check(info.DetectionMethod == "Extension", "known extension uses extension fallback");
    return info.Format;
});
Check(extensionFallback == "VMDK", "VMDK extension fallback is detected");

var verifyPayload = Encoding.UTF8.GetBytes("Dragon DiskForge verification smoke test");
var computedSha256 = await WithTempFileAsync(".img", verifyPayload, verifier.ComputeSha256Async);
var expectedSha256 = Convert.ToHexString(SHA256.HashData(verifyPayload));
Check(computedSha256 == expectedSha256, "Core SHA-256 verification returns the expected digest");

try
{
    await detector.InspectAsync(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.iso"));
    Check(false, "missing image throws FileNotFoundException");
}
catch (FileNotFoundException)
{
    Check(true, "missing image throws FileNotFoundException");
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"\n{failures.Count} smoke test(s) failed.");
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("\nDragon DiskForge Core smoke tests passed.");
