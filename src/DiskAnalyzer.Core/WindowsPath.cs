namespace DiskAnalyzer.Core;

/// <summary>
/// Разбор путей Windows без опоры на правила текущей ОС.
/// Так правила исключений одинаково работают в программе и в тестах.
/// </summary>
public static class WindowsPath
{
    public static string NormalizeRoot(string userInput)
    {
        if (string.IsNullOrWhiteSpace(userInput))
            throw new ArgumentException("Путь не задан.", nameof(userInput));

        var trimmed = userInput.Trim().Trim('"');
        if (trimmed.Length >= 2 && trimmed[1] == ':')
            trimmed = char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
        if (trimmed.Length == 2 && trimmed[1] == ':')
            trimmed += "\\";

        // На Linux Path.GetFullPath испортил бы «C:\». Для таких строк нормализуем сами.
        if (!OperatingSystem.IsWindows() && LooksLikeWindowsPath(trimmed))
            return trimmed.Replace('/', '\\');

        return Path.GetFullPath(trimmed);
    }

    public static bool LooksLikeWindowsPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return true;
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            return true;
        return path.Length >= 2 && path[1] == ':';
    }

    public static string ToDisplayPath(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return path[4..];
        return path;
    }

    /// <summary>Префикс \\?\ позволяет читать пути длиннее 260 символов.</summary>
    public static string ToExtendedSyntax(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return path;

        var normalized = path.Replace('/', '\\');
        if (normalized.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + normalized[2..];
        if (normalized.Length >= 2 && normalized[1] == ':')
            return @"\\?\" + normalized;
        return normalized;
    }

    public static string ToExtendedPath(string path)
    {
        if (!OperatingSystem.IsWindows())
            return path;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return path;

        var absolute = path;
        try
        {
            if (!Path.IsPathRooted(path))
                absolute = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            absolute = path;
        }

        return ToExtendedSyntax(absolute);
    }

    public static bool SamePath(string left, string right)
    {
        return string.Equals(Canonical(left), Canonical(right), StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> GetSegments(string path)
    {
        var display = ToDisplayPath(path).Replace('/', '\\');
        string rest;
        if (IsUnc(display))
        {
            var parts = display.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 2)
                return Array.Empty<string>();
            return parts[2..];
        }

        if (display.Length >= 2 && display[1] == ':')
            rest = display.Length > 2 ? display[2..] : string.Empty;
        else
            rest = display;

        if (rest.Length == 0)
            return Array.Empty<string>();

        return rest.Split('\\', StringSplitOptions.RemoveEmptyEntries);
    }

    public static string GetFileName(string path)
    {
        var display = ToDisplayPath(path).Replace('/', '\\').TrimEnd('\\');
        var index = display.LastIndexOf('\\');
        return index < 0 ? display : display[(index + 1)..];
    }

    public static string GetExtension(string path)
    {
        var name = GetFileName(path);
        var index = name.LastIndexOf('.');
        if (index <= 0 || index == name.Length - 1)
            return string.Empty;
        return name[index..];
    }

    public static string GetDirectory(string path)
    {
        var display = ToDisplayPath(path).Replace('/', '\\').TrimEnd('\\');
        var index = display.LastIndexOf('\\');
        if (index < 0)
            return string.Empty;
        if (index == 2 && display[1] == ':')
            return display[..3];
        if (IsUnc(display))
        {
            var dir = display[..index];
            return dir.Length < 2 ? display : dir;
        }

        return display[..index];
    }

    private static string Canonical(string path)
    {
        var display = ToDisplayPath(path).Replace('/', '\\').TrimEnd('\\');
        if (display.Length == 2 && display[1] == ':')
            display += "\\";
        return display;
    }

    private static bool IsUnc(string display)
    {
        return display.StartsWith(@"\\", StringComparison.Ordinal) && !display.StartsWith(@"\\?\", StringComparison.Ordinal);
    }
}
