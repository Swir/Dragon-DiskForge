namespace DragonDiskForge.Core.Models;

public sealed record OpticalTrackInfo(
    int Number,
    string Mode,
    string FileName,
    string FilePath,
    int SectorSize,
    long StartSector,
    long SectorCount,
    long StartByte,
    long LengthBytes,
    int? Index00Frames,
    int Index01Frames,
    bool IsAudio);

public sealed record OpticalTrackLayoutInfo(
    string CuePath,
    IReadOnlyList<OpticalTrackInfo> Tracks,
    IReadOnlyList<string> DataFiles)
{
    public bool IsMultiFile => DataFiles.Count > 1;
    public int TrackCount => Tracks.Count;
    public int AudioTrackCount => Tracks.Count(track => track.IsAudio);
    public int DataTrackCount => Tracks.Count - AudioTrackCount;
}
