namespace DiskAnalyzer.Core;

/// <summary>Насколько старым и большим должен быть файл, чтобы попасть в список.</summary>
public readonly record struct ScanCriteria(int OlderThanDays, long MinimumSizeBytes)
{
    public const int DefaultOlderThanDays = 365;
    public const long BytesPerMegabyte = 1024L * 1024L;
    public const long DefaultMinimumSizeBytes = 50 * BytesPerMegabyte;

    public static ScanCriteria Default { get; } = new(DefaultOlderThanDays, DefaultMinimumSizeBytes);

    public void Validate()
    {
        if (OlderThanDays < 0)
            throw new ArgumentOutOfRangeException(nameof(OlderThanDays), "Возраст не может быть отрицательным.");
        if (MinimumSizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(MinimumSizeBytes), "Размер не может быть отрицательным.");
    }
}

public enum ScanStatus
{
    Completed,
    Cancelled,
    FolderNotFound,
    AccessDenied,
    InvalidPath
}

public sealed class ScanProgress
{
    public required string CurrentPath { get; init; }
    public required long FilesEnumerated { get; init; }
    public required long CandidatesFound { get; init; }
    public required long DirectoriesSkipped { get; init; }
    public required IReadOnlyList<FileCandidate> NewCandidates { get; init; }
}

public sealed class ScanResult
{
    public ScanStatus Status { get; set; }
    public bool RootExcluded { get; set; }
    public List<FileCandidate> Candidates { get; } = new();
    public long FilesEnumerated { get; set; }
    public long DirectoriesSkipped { get; set; }
    public string? Warning { get; set; }
    public long TotalBytes => Candidates.Sum(candidate => candidate.SizeBytes);
}
