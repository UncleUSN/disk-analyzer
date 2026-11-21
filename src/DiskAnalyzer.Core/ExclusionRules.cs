using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiskAnalyzer.Core;

/// <summary>
/// Что не показывать. Список читается из exclusions.json, чтобы его можно было поправить без пересборки.
/// Атрибут «Системный» здесь не настраивается: такие файлы пропускаются всегда.
/// </summary>
public sealed class ExclusionRules
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HashSet<string> _rootDirectories;
    private readonly List<string[]> _relativePaths;
    private readonly HashSet<string> _fileNames;
    private readonly List<string> _fileNamePrefixes;
    private readonly HashSet<string> _extensions;

    private ExclusionRules(
        IEnumerable<string> rootDirectories,
        IEnumerable<string> relativePaths,
        IEnumerable<string> fileNames,
        IEnumerable<string> fileNamePrefixes,
        IEnumerable<string> extensions)
    {
        _rootDirectories = new HashSet<string>(Clean(rootDirectories), StringComparer.OrdinalIgnoreCase);
        _fileNames = new HashSet<string>(Clean(fileNames), StringComparer.OrdinalIgnoreCase);
        _fileNamePrefixes = Clean(fileNamePrefixes).ToList();
        _extensions = new HashSet<string>(Clean(extensions).Select(NormalizeExtension), StringComparer.OrdinalIgnoreCase);
        _relativePaths = Clean(relativePaths)
            .Select(path => path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length > 0)
            .ToList();
        ExcludedRelativePaths = Clean(relativePaths).ToArray();
    }

    public IReadOnlyCollection<string> ExcludedRootDirectories => _rootDirectories;
    public IReadOnlyCollection<string> ExcludedRelativePaths { get; }
    public IReadOnlyCollection<string> ExcludedFileNames => _fileNames;
    public IReadOnlyCollection<string> ExcludedFileNamePrefixes => _fileNamePrefixes;
    public IReadOnlyCollection<string> ExcludedExtensions => _extensions;

    public static ExclusionRules Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return Parse(json);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidDataException("Не удалось прочитать файл исключений: " + ex.Message, ex);
        }
    }

    public static ExclusionRules LoadDefault()
    {
        return Parse(DefaultJson());
    }

    public static string DefaultJson()
    {
        using var stream = OpenDefaultStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static ExclusionRules Parse(string json)
    {
        ExclusionConfigDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<ExclusionConfigDto>(json, JsonOptions) ?? new ExclusionConfigDto();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Файл исключений повреждён: " + ex.Message, ex);
        }

        return new ExclusionRules(
            dto.ExcludedRootDirectories ?? [],
            dto.ExcludedRelativePaths ?? [],
            dto.ExcludedFileNames ?? [],
            dto.ExcludedFileNamePrefixes ?? [],
            dto.ExcludedExtensions ?? []);
    }

    /// <summary>
    /// Каталог верхнего уровня диска (например C:\Windows) или известный вложенный путь
    /// (например AppData\Local\Packages). Program Files сюда не входит.
    /// </summary>
    public bool IsExcludedPath(string fullPath)
    {
        var segments = WindowsPath.GetSegments(fullPath);
        if (segments.Count == 0)
            return false;

        if (_rootDirectories.Contains(segments[0]))
            return true;

        foreach (var relative in _relativePaths)
        {
            if (ContainsSequence(segments, relative))
                return true;
        }

        return false;
    }

    public bool IsExcludedFileName(string fileName)
    {
        if (_fileNames.Contains(fileName))
            return true;

        foreach (var prefix in _fileNamePrefixes)
        {
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public bool IsExcludedExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return false;
        return _extensions.Contains(NormalizeExtension(extension));
    }

    private static bool ContainsSequence(IReadOnlyList<string> segments, IReadOnlyList<string> sequence)
    {
        if (sequence.Count == 0 || segments.Count < sequence.Count)
            return false;

        for (var start = 0; start <= segments.Count - sequence.Count; start++)
        {
            var matches = true;
            for (var offset = 0; offset < sequence.Count; offset++)
            {
                if (!segments[start + offset].Equals(sequence[offset], StringComparison.OrdinalIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return true;
        }

        return false;
    }

    private static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim();
        if (trimmed.Length == 0)
            return trimmed;
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }

    private static IEnumerable<string> Clean(IEnumerable<string> values)
    {
        return values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim());
    }

    private static Stream OpenDefaultStream()
    {
        var assembly = typeof(ExclusionRules).Assembly;
        var stream = assembly.GetManifestResourceStream("DiskAnalyzer.Core.exclusions.json");
        if (stream is not null)
            return stream;

        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(resource => resource.EndsWith("exclusions.json", StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            var known = string.Join(", ", assembly.GetManifestResourceNames());
            throw new InvalidOperationException("Не найден встроенный exclusions.json. Ресурсы: " + known);
        }

        return assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Не удалось открыть ресурс " + name);
    }

    private sealed class ExclusionConfigDto
    {
        public string? Description { get; set; }
        public List<string>? ExcludedRootDirectories { get; set; }
        public List<string>? ExcludedRelativePaths { get; set; }
        public List<string>? ExcludedFileNames { get; set; }
        public List<string>? ExcludedFileNamePrefixes { get; set; }
        public List<string>? ExcludedExtensions { get; set; }
    }
}
