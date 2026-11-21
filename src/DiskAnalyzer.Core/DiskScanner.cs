using System.Collections.Concurrent;

namespace DiskAnalyzer.Core;

/// <summary>
/// Обходит выбранную папку в нескольких потоках и отдаёт только подходящие файлы.
/// Каталоги из списка исключений не открываются. Ошибки доступа пропускаются.
/// Ссылки и junction не преследуются. Содержимое файлов не читается и не меняется.
/// </summary>
public sealed class DiskScanner
{
    private readonly IDirectoryEnumerator _directories;
    private readonly int _degree;

    public DiskScanner(IDirectoryEnumerator? directories = null, int maxDegreeOfParallelism = 0)
    {
        _directories = directories ?? new SystemDirectoryEnumerator();
        _degree = maxDegreeOfParallelism > 0
            ? maxDegreeOfParallelism
            : Math.Clamp(Environment.ProcessorCount, 2, 8);
    }

    public async Task<ScanResult> ScanAsync(
        string rootPath,
        ScanCriteria criteria,
        ExclusionRules rules,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rules);
        criteria.Validate();

        var result = new ScanResult();
        string root;
        try
        {
            root = WindowsPath.NormalizeRoot(rootPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            result.Status = ScanStatus.InvalidPath;
            result.Warning = ex.Message;
            return result;
        }

        if (!_directories.DirectoryExists(root))
        {
            result.Status = ScanStatus.FolderNotFound;
            return result;
        }

        if (rules.IsExcludedPath(root))
        {
            result.Status = ScanStatus.Completed;
            result.RootExcluded = true;
            return result;
        }

        var state = new ScanState
        {
            CurrentPath = root,
            StartedUtc = DateTimeOffset.UtcNow
        };
        using var queue = new BlockingCollection<string>();

        try
        {
            Enqueue(queue, state, root, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            result.Status = ScanStatus.Cancelled;
            return result;
        }

        // Токен намеренно не передаётся в Task.Run: отмена не должна оставлять
        // счётчик «в работе» без рабочего потока. Потоки сами смотрят на токен.
        var workers = new Task[_degree];
        for (var i = 0; i < _degree; i++)
            workers[i] = Task.Run(() => Worker(queue, state, root, criteria, rules, progress, cancellationToken));

        await Task.WhenAll(workers).ConfigureAwait(false);
        Report(state, progress, force: true);

        result.FilesEnumerated = Interlocked.Read(ref state.FilesSeen);
        result.DirectoriesSkipped = Interlocked.Read(ref state.SkippedDirectories);
        lock (state.Gate)
        {
            result.Candidates.AddRange(state.Found);
            result.Warning = state.Unexpected?.Message;
        }

        if (state.RootMissing)
            result.Status = ScanStatus.FolderNotFound;
        else if (state.RootDenied)
            result.Status = ScanStatus.AccessDenied;
        else if (cancellationToken.IsCancellationRequested)
            result.Status = ScanStatus.Cancelled;
        else
            result.Status = ScanStatus.Completed;

        return result;
    }

    private void Worker(
        BlockingCollection<string> queue,
        ScanState state,
        string root,
        ScanCriteria criteria,
        ExclusionRules rules,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            string directory;
            try
            {
                directory = queue.Take(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                break;
            }

            try
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                ProcessDirectory(queue, state, root, directory, criteria, rules, progress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                break;
            }
            catch (Exception ex)
            {
                lock (state.Gate)
                    state.Unexpected ??= ex;
            }
            finally
            {
                if (Interlocked.Decrement(ref state.Pending) == 0)
                    Complete(queue);
            }
        }
    }

    private void ProcessDirectory(
        BlockingCollection<string> queue,
        ScanState state,
        string root,
        string directory,
        ScanCriteria criteria,
        ExclusionRules rules,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        state.CurrentPath = directory;
        if (!WindowsPath.SamePath(directory, root) && rules.IsExcludedPath(directory))
            return;

        using var iterator = _directories.Enumerate(directory).GetEnumerator();
        while (true)
        {
            FileEntry entry;
            try
            {
                if (!iterator.MoveNext())
                    break;
                entry = iterator.Current;
            }
            catch (UnauthorizedAccessException)
            {
                NoteFailure(state, directory, root, accessDenied: true);
                break;
            }
            catch (DirectoryNotFoundException)
            {
                NoteFailure(state, directory, root, accessDenied: false);
                break;
            }
            catch (Exception ex) when (ex is IOException or System.Security.SecurityException)
            {
                NoteFailure(state, directory, root, accessDenied: true);
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsReparsePoint)
            {
                if (entry.IsDirectory)
                    Interlocked.Increment(ref state.SkippedDirectories);
                continue;
            }

            if (entry.IsDirectory)
            {
                if (rules.IsExcludedPath(entry.FullPath))
                    continue;
                Enqueue(queue, state, entry.FullPath, cancellationToken);
                continue;
            }

            Interlocked.Increment(ref state.FilesSeen);
            if (!CandidateFilter.IsMatch(entry, criteria, rules, state.StartedUtc))
            {
                Report(state, progress, force: false);
                continue;
            }

            var candidate = FileCandidate.From(entry);
            lock (state.Gate)
            {
                state.Found.Add(candidate);
                state.Batch.Add(candidate);
            }

            Report(state, progress, force: false);
        }
    }

    private static void NoteFailure(ScanState state, string directory, string root, bool accessDenied)
    {
        if (WindowsPath.SamePath(directory, root))
        {
            if (accessDenied)
                state.RootDenied = true;
            else
                state.RootMissing = true;
            return;
        }

        Interlocked.Increment(ref state.SkippedDirectories);
    }

    /// <summary>
    /// Счётчик Pending — сколько каталогов ещё нужно обработать.
    /// Он увеличивается до добавления в очередь и уменьшается после обработки.
    /// Когда он падает до нуля, очередь закрывается и рабочие потоки заканчивают работу.
    /// Так обход не заканчивается раньше времени и не зависает.
    /// </summary>
    private static void Enqueue(
        BlockingCollection<string> queue,
        ScanState state,
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref state.Pending);
        try
        {
            queue.Add(path, cancellationToken);
        }
        catch
        {
            if (Interlocked.Decrement(ref state.Pending) == 0)
                Complete(queue);
            throw;
        }
    }

    private static void Complete(BlockingCollection<string> queue)
    {
        try
        {
            queue.CompleteAdding();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void Report(ScanState state, IProgress<ScanProgress>? progress, bool force)
    {
        if (progress is null)
            return;

        List<FileCandidate> fresh;
        long seen;
        long found;
        long skipped;
        string current;
        lock (state.Gate)
        {
            var due = force
                || state.Batch.Count >= 40
                || (DateTime.UtcNow - state.LastReportUtc).TotalMilliseconds >= 200;
            if (!due)
                return;

            fresh = state.Batch.Count == 0 ? [] : new List<FileCandidate>(state.Batch);
            state.Batch.Clear();
            state.LastReportUtc = DateTime.UtcNow;
            seen = Interlocked.Read(ref state.FilesSeen);
            found = state.Found.Count;
            skipped = Interlocked.Read(ref state.SkippedDirectories);
            current = state.CurrentPath;
        }

        progress.Report(new ScanProgress
        {
            CurrentPath = current,
            FilesEnumerated = seen,
            CandidatesFound = found,
            DirectoriesSkipped = skipped,
            NewCandidates = fresh
        });
    }

    private sealed class ScanState
    {
        public int Pending;
        public long FilesSeen;
        public long SkippedDirectories;
        public readonly List<FileCandidate> Found = new();
        public readonly List<FileCandidate> Batch = new();
        public readonly object Gate = new();
        public string CurrentPath = string.Empty;
        public DateTime LastReportUtc = DateTime.MinValue;
        public DateTimeOffset StartedUtc;
        public Exception? Unexpected;
        public bool RootMissing;
        public bool RootDenied;
    }
}
