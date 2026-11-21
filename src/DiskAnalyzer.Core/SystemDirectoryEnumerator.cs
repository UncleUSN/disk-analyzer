namespace DiskAnalyzer.Core;

/// <summary>
/// Читает каталог через обычные функции .NET. Содержимое файлов не открывается,
/// поэтому даты и байты на диске не меняются.
/// </summary>
public sealed class SystemDirectoryEnumerator : IDirectoryEnumerator
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        // По умолчанию .NET пропускает скрытые и системные файлы. Нам так нельзя:
        // системные мы отфильтруем сами, а скрытые крупные файлы как раз полезно показать.
        // Точки повторной обработки (junction, символические ссылки) не обходим, чтобы не зациклиться.
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
        BufferSize = 64 * 1024
    };

    public bool DirectoryExists(string path)
    {
        try
        {
            if (Directory.Exists(path))
                return true;

            var extended = WindowsPath.ToExtendedPath(path);
            return !string.Equals(extended, path, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(extended);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public IEnumerable<FileEntry> Enumerate(string directoryPath)
    {
        var probe = WindowsPath.ToExtendedPath(directoryPath);
        var directory = new DirectoryInfo(probe);
        foreach (var entry in directory.EnumerateFileSystemInfos("*", Options))
        {
            FileEntry? mapped;
            try
            {
                mapped = Map(entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
                continue;
            }

            if (mapped is not null)
                yield return mapped;
        }
    }

    private static FileEntry? Map(FileSystemInfo entry)
    {
        var attributes = entry.Attributes;
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        if (!TryToOffset(SafeRead(() => entry.LastWriteTimeUtc), out var lastWrite))
            return null;

        var lastAccess = TryToOffset(SafeRead(() => entry.LastAccessTimeUtc), out var access)
            ? access
            : lastWrite;

        long length = 0;
        if (!isDirectory && entry is FileInfo file)
            length = file.Length;

        var fullPath = WindowsPath.ToDisplayPath(entry.FullName);
        return new FileEntry
        {
            FullPath = fullPath,
            Name = entry.Name,
            Extension = WindowsPath.GetExtension(entry.Name),
            IsDirectory = isDirectory,
            Length = length,
            Attributes = attributes,
            LastWriteTimeUtc = lastWrite,
            LastAccessTimeUtc = lastAccess
        };
    }

    private static DateTime? SafeRead(Func<DateTime> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static bool TryToOffset(DateTime? value, out DateTimeOffset offset)
    {
        offset = default;
        if (value is null)
            return false;

        try
        {
            var time = value.Value;
            if (time == DateTime.MinValue)
            {
                offset = DateTimeOffset.MinValue;
                return true;
            }

            if (time.Kind == DateTimeKind.Unspecified)
                time = DateTime.SpecifyKind(time, DateTimeKind.Utc);
            offset = new DateTimeOffset(time);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
