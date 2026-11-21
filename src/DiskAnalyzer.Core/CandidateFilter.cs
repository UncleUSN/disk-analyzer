namespace DiskAnalyzer.Core;

/// <summary>
/// Файл попадает в список, только если выполнены все условия сразу:
/// он достаточно большой, последнее использование старше N дней,
/// у него нет атрибута «Системный» и путь не в списке исключений.
/// </summary>
public static class CandidateFilter
{
    public static bool IsMatch(FileEntry file, ScanCriteria criteria, ExclusionRules rules, DateTimeOffset utcNow)
    {
        if (file.IsDirectory || file.IsReparsePoint)
            return false;
        if (file.Length < criteria.MinimumSizeBytes)
            return false;
        if (file.IsSystem)
            return false;
        if (rules.IsExcludedExtension(file.Extension) || rules.IsExcludedFileName(file.Name))
            return false;
        if (!IsOlderThan(LastUsed(file.LastAccessTimeUtc, file.LastWriteTimeUtc), criteria.OlderThanDays, utcNow))
            return false;
        if (rules.IsExcludedPath(file.FullPath))
            return false;
        return true;
    }

    /// <summary>Более поздняя из даты доступа и даты изменения.</summary>
    public static DateTimeOffset LastUsed(DateTimeOffset lastAccessUtc, DateTimeOffset lastWriteUtc)
    {
        return lastAccessUtc > lastWriteUtc ? lastAccessUtc : lastWriteUtc;
    }

    /// <summary>«Больше чем N дней назад» — строго старше, ровно N дней не считается.</summary>
    public static bool IsOlderThan(DateTimeOffset lastUsedUtc, int olderThanDays, DateTimeOffset utcNow)
    {
        var threshold = utcNow.AddDays(-olderThanDays);
        return lastUsedUtc < threshold;
    }
}
