namespace DiskAnalyzer.Core.Tests;

internal sealed class FakeDirectoryEnumerator : IDirectoryEnumerator
{
    private readonly Dictionary<string, FileEntry[]> _dirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public List<string> Visited { get; } = new();
    public HashSet<string> Denied { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Action<string>? OnEnumerate { get; set; }

    public void Set(string directory, params FileEntry[] children)
    {
        _dirs[Key(directory)] = children;
    }

    public bool DirectoryExists(string path) => _dirs.ContainsKey(Key(path));

    public IEnumerable<FileEntry> Enumerate(string directoryPath)
    {
        var key = Key(directoryPath);
        OnEnumerate?.Invoke(key);
        lock (_gate)
            Visited.Add(key);

        if (Denied.Contains(key))
            throw new UnauthorizedAccessException(directoryPath);
        if (!_dirs.TryGetValue(key, out var children))
            throw new DirectoryNotFoundException(directoryPath);
        return children;
    }

    public static string Key(string path)
    {
        var normalized = path.Replace('/', '\\');
        if (normalized.Length == 3 && normalized[1] == ':' && normalized[2] == '\\')
            return normalized;
        return normalized.TrimEnd('\\');
    }
}

internal static class Entries
{
    public static FileEntry Dir(string fullPath, FileAttributes extra = 0)
    {
        return new FileEntry
        {
            FullPath = fullPath,
            Name = WindowsPath.GetFileName(fullPath),
            Extension = string.Empty,
            IsDirectory = true,
            Attributes = FileAttributes.Directory | extra,
            LastWriteTimeUtc = DateTimeOffset.UnixEpoch,
            LastAccessTimeUtc = DateTimeOffset.UnixEpoch
        };
    }

    public static FileEntry File(
        string fullPath,
        long size,
        DateTimeOffset writeUtc,
        DateTimeOffset? accessUtc = null,
        FileAttributes attributes = FileAttributes.Archive)
    {
        var name = WindowsPath.GetFileName(fullPath);
        return new FileEntry
        {
            FullPath = fullPath,
            Name = name,
            Extension = WindowsPath.GetExtension(name),
            IsDirectory = false,
            Length = size,
            Attributes = attributes,
            LastWriteTimeUtc = writeUtc,
            LastAccessTimeUtc = accessUtc ?? writeUtc
        };
    }
}
