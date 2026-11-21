namespace DiskAnalyzer.Core;

/// <summary>
/// Один файл или каталог, как его увидел обходчик.
/// Даты хранятся в UTC. «Последнее использование» считается позже, в фильтре.
/// </summary>
public sealed record FileEntry
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required string Extension { get; init; }
    public required bool IsDirectory { get; init; }
    public long Length { get; init; }
    public FileAttributes Attributes { get; init; }
    public DateTimeOffset LastWriteTimeUtc { get; init; }
    public DateTimeOffset LastAccessTimeUtc { get; init; }

    public bool IsReparsePoint => (Attributes & FileAttributes.ReparsePoint) != 0;
    public bool IsSystem => (Attributes & FileAttributes.System) != 0;
}
