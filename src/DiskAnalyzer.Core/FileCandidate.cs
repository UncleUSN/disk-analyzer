using System.Globalization;

namespace DiskAnalyzer.Core;

/// <summary>Файл, который прошёл все правила и показывается в таблице.</summary>
public sealed record FileCandidate
{
    public required string Name { get; init; }
    public required string Directory { get; init; }
    public required string FullPath { get; init; }
    public required long SizeBytes { get; init; }
    public required string SizeText { get; init; }
    public required DateTimeOffset LastUsedUtc { get; init; }
    public required DateTimeOffset LastWriteUtc { get; init; }
    public required DateTimeOffset LastAccessUtc { get; init; }
    public required string Extension { get; init; }

    public string LastUsedText => FormatLocal(LastUsedUtc);
    public string LastWriteText => FormatLocal(LastWriteUtc);

    public static FileCandidate From(FileEntry entry)
    {
        var fullPath = WindowsPath.ToDisplayPath(entry.FullPath);
        var extension = entry.Extension;
        if (extension.Length > 0 && !extension.StartsWith('.'))
            extension = "." + extension;

        return new FileCandidate
        {
            Name = entry.Name,
            Directory = WindowsPath.GetDirectory(fullPath),
            FullPath = fullPath,
            SizeBytes = entry.Length,
            SizeText = SizeFormatter.Format(entry.Length),
            LastUsedUtc = CandidateFilter.LastUsed(entry.LastAccessTimeUtc, entry.LastWriteTimeUtc),
            LastWriteUtc = entry.LastWriteTimeUtc,
            LastAccessUtc = entry.LastAccessTimeUtc,
            Extension = extension.ToLowerInvariant()
        };
    }

    private static string FormatLocal(DateTimeOffset utc)
    {
        var local = utc.ToLocalTime();
        return local.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
    }
}
