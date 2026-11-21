namespace DiskAnalyzer.Core.Tests;

public class RealScanTests
{
    [Fact]
    public async Task Real_scan_keeps_program_files_data_and_does_not_modify_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "disk-analyzer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var old = DateTime.UtcNow.AddDays(-400);
            var recent = DateTime.UtcNow.AddDays(-2);
            var cache = Path.Combine(root, "Program Files", "Vendor", "cache.dat");
            var exe = Path.Combine(root, "Program Files", "Vendor", "app.exe");
            var dll = Path.Combine(root, "Program Files (x86)", "Legacy", "lib.dll");
            var zip = Path.Combine(root, "Program Files (x86)", "Legacy", "archive.zip");
            var windowsNested = Path.Combine(root, "Windows", "note.bin");
            var packages = Path.Combine(root, "Users", "Ivan", "AppData", "Local", "Packages", "App", "data.bin");
            var temp = Path.Combine(root, "Users", "Ivan", "AppData", "Local", "Temp", "old.bin");
            var small = Path.Combine(root, "keep", "small.bin");
            var fresh = Path.Combine(root, "keep", "fresh.bin");
            var hidden = Path.Combine(root, "keep", "hidden.bin");

            WriteOld(cache, 80, old);
            WriteOld(exe, 80, old);
            WriteOld(dll, 80, old);
            WriteOld(zip, 80, old);
            WriteOld(windowsNested, 80, old);
            WriteOld(packages, 80, old);
            WriteOld(temp, 80, old);
            WriteOld(small, 10, old);
            WriteOld(fresh, 80, recent);
            WriteOld(hidden, 80, old);
            TrySet(hidden, FileAttributes.Hidden);

            var before = new byte[80];
            var writeBefore = File.GetLastWriteTimeUtc(cache);

            var scanner = new DiskScanner(new SystemDirectoryEnumerator(), maxDegreeOfParallelism: 4);
            var result = await scanner.ScanAsync(root, new ScanCriteria(30, 32), ExclusionRules.LoadDefault(), null, CancellationToken.None);

            Assert.Equal(ScanStatus.Completed, result.Status);
            var found = result.Candidates.Select(item => item.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Full(cache), found);
            Assert.Contains(Full(zip), found);
            Assert.Contains(Full(windowsNested), found);
            Assert.Contains(Full(temp), found);
            Assert.DoesNotContain(Full(exe), found);
            Assert.DoesNotContain(Full(dll), found);
            Assert.DoesNotContain(Full(packages), found);
            Assert.DoesNotContain(Full(small), found);
            Assert.DoesNotContain(Full(fresh), found);
            if ((File.GetAttributes(hidden) & FileAttributes.Hidden) != 0)
                Assert.Contains(Full(hidden), found);

            Assert.Equal(before, File.ReadAllBytes(cache));
            Assert.Equal(writeBefore, File.GetLastWriteTimeUtc(cache));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Real_scan_does_not_follow_directory_symlinks()
    {
        var root = Path.Combine(Path.GetTempPath(), "disk-analyzer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "real");
            var link = Path.Combine(root, "link");
            Directory.CreateDirectory(target);
            var file = Path.Combine(target, "movie.iso");
            WriteOld(file, 64, DateTime.UtcNow.AddDays(-400));
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception)
            {
                return;
            }

            var scanner = new DiskScanner(new SystemDirectoryEnumerator(), maxDegreeOfParallelism: 2);
            var result = await scanner.ScanAsync(root, new ScanCriteria(30, 32), ExclusionRules.LoadDefault(), null, CancellationToken.None);
            Assert.Equal(1, result.Candidates.Count(item => item.Name.Equals("movie.iso", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteOld(string path, int size, DateTime whenUtc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, whenUtc);
        File.SetLastAccessTimeUtc(path, whenUtc);
    }

    private static void TrySet(string path, FileAttributes attributes)
    {
        try
        {
            File.SetAttributes(path, attributes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private static string Full(string path) => Path.GetFullPath(path);
}
