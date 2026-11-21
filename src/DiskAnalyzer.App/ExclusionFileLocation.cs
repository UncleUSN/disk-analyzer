using System.IO;
using System.Text;
using DiskAnalyzer.Core;

namespace DiskAnalyzer;

public static class ExclusionFileLocation
{
    public const string FileName = "exclusions.json";

    public static string? FindExisting()
    {
        foreach (var directory in CandidateDirectories())
        {
            var path = Path.Combine(directory, FileName);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    public static string? TryWriteDefaultBesideExe()
    {
        var directory = ProcessDirectory();
        if (directory is null)
            return null;

        var path = Path.Combine(directory, FileName);
        if (File.Exists(path))
            return path;

        try
        {
            File.WriteAllText(path, ExclusionRules.DefaultJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var processDirectory = ProcessDirectory();
        if (processDirectory is not null)
            yield return processDirectory;

        var baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(baseDirectory))
            yield return baseDirectory;

        yield return Environment.CurrentDirectory;
    }

    private static string? ProcessDirectory()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            return null;
        return Path.GetDirectoryName(processPath);
    }
}
