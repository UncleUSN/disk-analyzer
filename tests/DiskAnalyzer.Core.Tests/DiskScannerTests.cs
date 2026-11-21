namespace DiskAnalyzer.Core.Tests;

public class DiskScannerTests
{
    private static readonly long Large = 60L * 1024 * 1024;
    private static readonly DateTimeOffset Old = DateTimeOffset.UtcNow.AddDays(-400);
    private static readonly DateTimeOffset Recent = DateTimeOffset.UtcNow.AddDays(-10);

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Scan_applies_every_rule_and_keeps_program_files(int degree)
    {
        var fake = BuildDisk();
        var progress = new CollectProgress();
        var scanner = new DiskScanner(fake, degree);
        var result = await scanner.ScanAsync(@"C:\", ScanCriteria.Default, ExclusionRules.LoadDefault(), progress, CancellationToken.None);

        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.False(result.RootExcluded);
        Assert.Equal(ExpectedHits().OrderBy(path => path), result.Candidates.Select(item => item.FullPath).OrderBy(path => path));
        Assert.Equal(result.Candidates.Count, progress.Items.Count);
        Assert.Contains(fake.Visited, path => path.Equals(@"C:\Program Files", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fake.Visited, path => path.Equals(@"C:\Program Files (x86)", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fake.Visited, path => path.Equals(@"C:\Windows.old", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fake.Visited, path => path.Equals(@"C:\Windows", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fake.Visited, path => path.Equals(@"C:\ProgramData", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fake.Visited, path => path.Equals(@"C:\Users\Ivan\AppData\Local\Programs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fake.Visited, path => path.Equals(@"C:\Users\Ivan\AppData\Local\Microsoft", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fake.Visited, path => path.Equals(@"C:\Users\Ivan\AppData\Local\Packages", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fake.Visited, path => path.Equals(@"C:\linkdir", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fake.Visited, path => path.Equals(@"C:\Secret", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.DirectoriesSkipped >= 1);
    }

    [Fact]
    public async Task Scanning_program_files_itself_returns_data_files_only()
    {
        var fake = BuildDisk();
        var scanner = new DiskScanner(fake, maxDegreeOfParallelism: 2);
        var result = await scanner.ScanAsync(@"C:\Program Files", ScanCriteria.Default, ExclusionRules.LoadDefault(), null, CancellationToken.None);

        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Equal(new[] { @"C:\Program Files\Vendor\cache.dat" }, result.Candidates.Select(item => item.FullPath).ToArray());
    }

    [Fact]
    public async Task Scanning_windows_root_is_refused_by_the_exclusion_list()
    {
        var fake = BuildDisk();
        var scanner = new DiskScanner(fake, maxDegreeOfParallelism: 2);
        var result = await scanner.ScanAsync(@"C:\Windows", ScanCriteria.Default, ExclusionRules.LoadDefault(), null, CancellationToken.None);

        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.True(result.RootExcluded);
        Assert.Empty(result.Candidates);
        Assert.Empty(fake.Visited);
    }

    [Fact]
    public async Task Missing_folder_is_reported()
    {
        var scanner = new DiskScanner(new FakeDirectoryEnumerator(), maxDegreeOfParallelism: 2);
        var result = await scanner.ScanAsync(@"C:\missing", ScanCriteria.Default, ExclusionRules.LoadDefault(), null, CancellationToken.None);
        Assert.Equal(ScanStatus.FolderNotFound, result.Status);
    }

    [Fact]
    public async Task Wide_tree_does_not_drop_files()
    {
        var fake = new FakeDirectoryEnumerator();
        var children = new FileEntry[200];
        for (var i = 0; i < children.Length; i++)
        {
            var dir = @"C:\wide\d" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            children[i] = Entries.Dir(dir);
            fake.Set(dir, Entries.File(dir + @"\f.bin", Large, Old));
        }

        fake.Set(@"C:\wide", children);
        var scanner = new DiskScanner(fake, maxDegreeOfParallelism: 8);
        var result = await scanner.ScanAsync(@"C:\wide", ScanCriteria.Default, ExclusionRules.LoadDefault(), null, CancellationToken.None);
        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Equal(200, result.Candidates.Count);
    }

    [Fact]
    public async Task Deep_chain_is_fully_walked()
    {
        var fake = new FakeDirectoryEnumerator();
        var current = @"C:\chain";
        for (var i = 0; i < 100; i++)
        {
            var next = current + @"\d" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            fake.Set(current, Entries.Dir(next));
            current = next;
        }

        fake.Set(current, Entries.File(current + @"\leaf.bin", Large, Old));
        var scanner = new DiskScanner(fake, maxDegreeOfParallelism: 4);
        var result = await scanner.ScanAsync(@"C:\chain", ScanCriteria.Default, ExclusionRules.LoadDefault(), null, CancellationToken.None);
        Assert.Equal(ScanStatus.Completed, result.Status);
        Assert.Single(result.Candidates);
        Assert.EndsWith("leaf.bin", result.Candidates[0].Name);
    }

    [Fact]
    public async Task Cancellation_stops_the_scan()
    {
        var fake = new FakeDirectoryEnumerator();
        fake.Set(@"C:\", Entries.Dir(@"C:\Data"));
        fake.Set(@"C:\Data", Entries.File(@"C:\Data\movie.iso", Large, Old));

        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        fake.OnEnumerate = _ =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(8));
        };

        using var cts = new CancellationTokenSource();
        var scanner = new DiskScanner(fake, maxDegreeOfParallelism: 2);
        var task = scanner.ScanAsync(@"C:\", ScanCriteria.Default, ExclusionRules.LoadDefault(), null, cts.Token);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        cts.Cancel();
        release.Set();

        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(8)));
        Assert.Same(task, finished);
        var result = await task;
        Assert.Equal(ScanStatus.Cancelled, result.Status);
    }

    private static FakeDirectoryEnumerator BuildDisk()
    {
        var fake = new FakeDirectoryEnumerator();
        fake.Denied.Add(@"C:\Secret");
        fake.Set(@"C:\Secret");

        fake.Set(
            @"C:\",
            Entries.File(@"C:\pagefile.sys", Large, Old, attributes: FileAttributes.System | FileAttributes.Hidden),
            Entries.File(@"C:\hiberfil.sys", Large, Old),
            Entries.File(@"C:\DumpStack.log", Large, Old),
            Entries.File(@"C:\big-old.iso", Large, Old),
            Entries.File(@"C:\big-new.iso", Large, Recent),
            Entries.File(@"C:\small-old.iso", 1024 * 1024, Old),
            Entries.File(@"C:\hidden-old.iso", Large, Old, attributes: FileAttributes.Hidden),
            Entries.File(@"C:\accessed-recently.iso", Large, Old, Recent),
            Entries.Dir(@"C:\Windows"),
            Entries.Dir(@"C:\Program Files"),
            Entries.Dir(@"C:\Program Files (x86)"),
            Entries.Dir(@"C:\ProgramData"),
            Entries.Dir(@"C:\Users"),
            Entries.Dir(@"C:\Windows.old"),
            Entries.Dir(@"C:\linkdir", FileAttributes.ReparsePoint),
            Entries.Dir(@"C:\Secret"));

        fake.Set(@"C:\Windows", Entries.File(@"C:\Windows\big.iso", Large, Old));
        fake.Set(@"C:\ProgramData", Entries.File(@"C:\ProgramData\cache.bin", Large, Old));
        fake.Set(@"C:\linkdir", Entries.File(@"C:\linkdir\secret-large.bin", Large, Old));
        fake.Set(@"C:\Windows.old", Entries.File(@"C:\Windows.old\hugefile.iso", Large, Old));

        fake.Set(@"C:\Program Files", Entries.Dir(@"C:\Program Files\Vendor"));
        fake.Set(
            @"C:\Program Files\Vendor",
            Entries.File(@"C:\Program Files\Vendor\cache.dat", Large, Old),
            Entries.File(@"C:\Program Files\Vendor\app.exe", Large, Old),
            Entries.File(@"C:\Program Files\Vendor\core.dll", Large, Old),
            Entries.File(@"C:\Program Files\Vendor\driver.sys", Large, Old),
            Entries.File(@"C:\Program Files\Vendor\setup.msi", Large, Old),
            Entries.File(@"C:\Program Files\Vendor\video.drv", Large, Old),
            Entries.File(@"C:\Program Files\Vendor\marked-system.bin", Large, Old, attributes: FileAttributes.System));

        fake.Set(@"C:\Program Files (x86)", Entries.Dir(@"C:\Program Files (x86)\Legacy"));
        fake.Set(@"C:\Program Files (x86)\Legacy", Entries.File(@"C:\Program Files (x86)\Legacy\archive.zip", Large, Old));

        fake.Set(@"C:\Users", Entries.Dir(@"C:\Users\Ivan"));
        fake.Set(@"C:\Users\Ivan", Entries.Dir(@"C:\Users\Ivan\Documents"), Entries.Dir(@"C:\Users\Ivan\AppData"));
        fake.Set(
            @"C:\Users\Ivan\Documents",
            Entries.File(@"C:\Users\Ivan\Documents\movie.mkv", Large, Old),
            Entries.File(@"C:\Users\Ivan\Documents\recent.mkv", Large, Recent));
        fake.Set(@"C:\Users\Ivan\AppData", Entries.Dir(@"C:\Users\Ivan\AppData\Local"), Entries.Dir(@"C:\Users\Ivan\AppData\Roaming"));
        fake.Set(
            @"C:\Users\Ivan\AppData\Local",
            Entries.Dir(@"C:\Users\Ivan\AppData\Local\Temp"),
            Entries.Dir(@"C:\Users\Ivan\AppData\Local\Programs"),
            Entries.Dir(@"C:\Users\Ivan\AppData\Local\Microsoft"),
            Entries.Dir(@"C:\Users\Ivan\AppData\Local\Packages"));
        fake.Set(@"C:\Users\Ivan\AppData\Local\Temp", Entries.File(@"C:\Users\Ivan\AppData\Local\Temp\dump.bin", Large, Old));
        fake.Set(@"C:\Users\Ivan\AppData\Local\Programs", Entries.File(@"C:\Users\Ivan\AppData\Local\Programs\data.bin", Large, Old));
        fake.Set(@"C:\Users\Ivan\AppData\Local\Microsoft", Entries.File(@"C:\Users\Ivan\AppData\Local\Microsoft\cache.bin", Large, Old));
        fake.Set(@"C:\Users\Ivan\AppData\Local\Packages", Entries.File(@"C:\Users\Ivan\AppData\Local\Packages\data.bin", Large, Old));
        fake.Set(@"C:\Users\Ivan\AppData\Roaming", Entries.Dir(@"C:\Users\Ivan\AppData\Roaming\Mail"));
        fake.Set(@"C:\Users\Ivan\AppData\Roaming\Mail", Entries.File(@"C:\Users\Ivan\AppData\Roaming\Mail\archive.pst", Large, Old));
        return fake;
    }

    private static string[] ExpectedHits()
    {
        return
        [
            @"C:\big-old.iso",
            @"C:\hidden-old.iso",
            @"C:\Windows.old\hugefile.iso",
            @"C:\Program Files\Vendor\cache.dat",
            @"C:\Program Files (x86)\Legacy\archive.zip",
            @"C:\Users\Ivan\Documents\movie.mkv",
            @"C:\Users\Ivan\AppData\Local\Temp\dump.bin",
            @"C:\Users\Ivan\AppData\Roaming\Mail\archive.pst"
        ];
    }

    private sealed class CollectProgress : IProgress<ScanProgress>
    {
        public List<FileCandidate> Items { get; } = new();

        public void Report(ScanProgress value) => Items.AddRange(value.NewCandidates);
    }
}
