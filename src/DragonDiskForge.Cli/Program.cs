using DragonDiskForge.Core.Services;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
var token = cancellation.Token;
try
{
    if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
    {
        Console.WriteLine("""
            Dragon DiskForge
            ddf inspect <image> [--json]
            ddf verify <image> [sha256|sha512] [expected-checksum]
            ddf create-raw <output> <size-bytes>
            ddf compress <image> <output.gz>
            ddf decompress <input.gz> <output> <maximum-output-bytes>
            ddf split <image> <new-output-folder> <part-size-bytes>
            ddf join <image.ddfparts.json> <output>
            Existing outputs are never overwritten. Ctrl+C cancels the operation.
            Exit codes: 0 success, 1 error, 2 usage/checksum mismatch, 130 cancelled.
            """);
        return 0;
    }
    var files = new ImageFileToolsService();
    switch (args[0].ToLowerInvariant())
    {
        case "inspect" when args.Length is 2 or 3:
            if (args.Length == 3 && args[2] != "--json") throw new ArgumentException("Expected --json.");
            var report = await new ImageReportService().AnalyzeAsync(args[1], token);
            Console.WriteLine(args.Length == 3 ? ImageReportService.ToJson(report) : ImageReportService.ToText(report));
            break;
        case "verify" when args.Length is >= 2 and <= 4:
            var algorithm = args.Length >= 3 ? args[2] : "sha256";
            var hash = await new ImageVerificationService().ComputeHashAsync(args[1], algorithm, cancellationToken: token);
            Console.WriteLine(hash);
            if (args.Length == 4 && !string.Equals(hash, args[3].Trim(), StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Checksum mismatch.");
                return 2;
            }
            break;
        case "create-raw" when args.Length == 3:
            await files.CreateRawAsync(args[1], long.Parse(args[2]), token);
            break;
        case "compress" when args.Length == 3:
            await files.CompressAsync(args[1], args[2], token: token);
            break;
        case "decompress" when args.Length == 4:
            await files.DecompressAsync(args[1], args[2], long.Parse(args[3]), token: token);
            break;
        case "split" when args.Length == 4:
            Console.WriteLine(await files.SplitAsync(args[1], args[2], long.Parse(args[3]), token: token));
            break;
        case "join" when args.Length == 3:
            await files.JoinAsync(args[1], args[2], token: token);
            break;
        default:
            Console.Error.WriteLine("Invalid arguments. Run ddf --help.");
            return 2;
    }
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled."); return 130; }
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
