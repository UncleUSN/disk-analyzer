namespace DiskAnalyzer.Core;

public interface IDirectoryEnumerator
{
    bool DirectoryExists(string path);

    /// <summary>Только непосредственные дети каталога, без рекурсии.</summary>
    IEnumerable<FileEntry> Enumerate(string directoryPath);
}
