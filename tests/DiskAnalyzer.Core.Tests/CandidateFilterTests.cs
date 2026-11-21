namespace DiskAnalyzer.Core.Tests;

public class CandidateFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly long FiftyMegabytes = 50L * 1024 * 1024;
    private readonly ExclusionRules _rules = ExclusionRules.LoadDefault();

    [Fact]
    public void Old_large_ordinary_file_matches_all_rules()
    {
        var file = Sample(@"D:\Data\movie.iso", FiftyMegabytes, Now.AddDays(-400));
        Assert.True(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
    }

    [Fact]
    public void File_used_exactly_n_days_ago_is_not_a_candidate()
    {
        var file = Sample(@"D:\Data\movie.iso", FiftyMegabytes, Now.AddDays(-365));
        Assert.False(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
    }

    [Fact]
    public void File_used_just_over_n_days_ago_is_a_candidate()
    {
        var file = Sample(@"D:\Data\movie.iso", FiftyMegabytes, Now.AddDays(-365).AddTicks(-1));
        Assert.True(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
    }

    [Fact]
    public void Recent_access_keeps_an_old_write_out_of_the_list()
    {
        var file = Sample(@"D:\Data\movie.iso", FiftyMegabytes, write: Now.AddDays(-400), access: Now.AddDays(-3));
        Assert.False(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
        Assert.Equal(Now.AddDays(-3), CandidateFilter.LastUsed(file.LastAccessTimeUtc, file.LastWriteTimeUtc));
    }

    [Fact]
    public void Recent_write_keeps_an_old_access_out_of_the_list()
    {
        var file = Sample(@"D:\Data\movie.iso", FiftyMegabytes, write: Now.AddDays(-2), access: Now.AddDays(-500));
        Assert.False(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
        Assert.Equal(Now.AddDays(-2), CandidateFilter.LastUsed(file.LastAccessTimeUtc, file.LastWriteTimeUtc));
    }

    [Fact]
    public void Both_dates_old_uses_the_later_one_and_matches()
    {
        var access = Now.AddDays(-400);
        var write = Now.AddDays(-800);
        var file = Sample(@"D:\Data\movie.iso", FiftyMegabytes, write, access);
        Assert.Equal(access, CandidateFilter.LastUsed(access, write));
        Assert.True(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
    }

    [Fact]
    public void Size_equal_to_the_minimum_matches()
    {
        var file = Sample(@"D:\Data\movie.iso", FiftyMegabytes, Now.AddDays(-400));
        Assert.True(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
    }

    [Fact]
    public void Size_one_byte_under_the_minimum_does_not_match()
    {
        var file = Sample(@"D:\Data\movie.iso", FiftyMegabytes - 1, Now.AddDays(-400));
        Assert.False(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
    }

    [Theory]
    [InlineData(FileAttributes.System)]
    [InlineData(FileAttributes.System | FileAttributes.Hidden)]
    [InlineData(FileAttributes.System | FileAttributes.Archive)]
    public void System_attribute_excludes_the_file(FileAttributes attributes)
    {
        var file = Sample(@"D:\Data\movie.iso", FiftyMegabytes, Now.AddDays(-400), attributes: attributes);
        Assert.False(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
    }

    [Fact]
    public void Hidden_file_without_system_attribute_still_matches()
    {
        var file = Sample(@"D:\Data\movie.iso", FiftyMegabytes, Now.AddDays(-400), attributes: FileAttributes.Hidden);
        Assert.True(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
    }

    [Theory]
    [InlineData(".exe")]
    [InlineData(".dll")]
    [InlineData(".sys")]
    [InlineData(".msi")]
    [InlineData(".drv")]
    [InlineData(".EXE")]
    [InlineData(".Dll")]
    public void Executable_extensions_are_excluded_everywhere(string extension)
    {
        var file = Sample(@"C:\Program Files\Vendor\app" + extension, FiftyMegabytes, Now.AddDays(-400));
        Assert.False(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
    }

    [Fact]
    public void Program_files_data_file_is_a_candidate()
    {
        var file = Sample(@"C:\Program Files\Vendor\cache\setup-cache.dat", FiftyMegabytes, Now.AddDays(-400));
        Assert.True(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
    }

    [Fact]
    public void Program_files_x86_archive_is_a_candidate()
    {
        var file = Sample(@"C:\Program Files (x86)\Legacy\logs\old.zip", FiftyMegabytes, Now.AddDays(-400));
        Assert.True(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
    }

    [Fact]
    public void System_file_inside_program_files_is_not_a_candidate()
    {
        var file = Sample(
            @"C:\Program Files\Vendor\marked.bin",
            FiftyMegabytes,
            Now.AddDays(-400),
            attributes: FileAttributes.System);
        Assert.False(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\drivers\old.etl")]
    [InlineData(@"C:\ProgramData\Package Cache\old.cab")]
    [InlineData(@"C:\System Volume Information\tracking.log")]
    [InlineData(@"C:\$Recycle.Bin\S-1-5-21\file.bin")]
    [InlineData(@"C:\$WinREAgent\Scratch\file.bin")]
    [InlineData(@"C:\Recovery\WindowsRE\file.bin")]
    [InlineData(@"C:\Boot\BCD")]
    [InlineData(@"C:\EFI\Microsoft\Boot\bootmgfw.efi")]
    [InlineData(@"C:\PerfLogs\Admin\trace.etl")]
    [InlineData(@"D:\Windows\notepad.dat")]
    [InlineData(@"C:\Users\Ivan\AppData\Local\Programs\App\data.bin")]
    [InlineData(@"C:\Users\Ivan\AppData\Local\Microsoft\Edge\cache.bin")]
    [InlineData(@"C:\Users\Ivan\AppData\Local\Packages\Store\data.bin")]
    public void Excluded_locations_do_not_match_even_if_old_and_large(string path)
    {
        var file = Sample(path, FiftyMegabytes, Now.AddDays(-400));
        Assert.False(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
    }

    [Theory]
    [InlineData("pagefile.sys")]
    [InlineData("hiberfil.sys")]
    [InlineData("swapfile.sys")]
    [InlineData("DumpStack.log")]
    [InlineData("dumpstack.log.tmp")]
    public void Special_system_file_names_are_excluded(string name)
    {
        var file = Sample(@"D:\Data\" + name, FiftyMegabytes, Now.AddDays(-400));
        Assert.False(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
    }

    [Fact]
    public void Windows_old_is_not_treated_as_the_windows_directory()
    {
        var file = Sample(@"C:\Windows.old\Users\movie.iso", FiftyMegabytes, Now.AddDays(-400));
        Assert.True(CandidateFilter.IsMatch(file, ScanCriteria.Default, _rules, Now));
    }

    [Fact]
    public void Reparse_point_and_directory_are_not_candidates()
    {
        var link = Sample(
            @"D:\Data\link.iso",
            FiftyMegabytes,
            Now.AddDays(-400),
            attributes: FileAttributes.ReparsePoint);
        var directory = Entries.Dir(@"D:\Data");
        Assert.False(CandidateFilter.IsMatch(link, ScanCriteria.Default, _rules, Now));
        Assert.False(CandidateFilter.IsMatch(directory, ScanCriteria.Default, _rules, Now));
    }

    [Fact]
    public void A_file_must_match_every_rule_at_once()
    {
        var rules = _rules;
        var criteria = ScanCriteria.Default;
        Assert.False(CandidateFilter.IsMatch(Sample(@"D:\Data\new.iso", FiftyMegabytes, Now.AddDays(-1)), criteria, rules, Now));
        Assert.False(CandidateFilter.IsMatch(Sample(@"D:\Data\small.iso", 1024, Now.AddDays(-400)), criteria, rules, Now));
        Assert.False(CandidateFilter.IsMatch(Sample(@"D:\Data\app.exe", FiftyMegabytes, Now.AddDays(-400)), criteria, rules, Now));
        Assert.True(CandidateFilter.IsMatch(Sample(@"D:\Data\keep.iso", FiftyMegabytes, Now.AddDays(-400)), criteria, rules, Now));
    }

    private static FileEntry Sample(
        string path,
        long size,
        DateTimeOffset write,
        DateTimeOffset? access = null,
        FileAttributes attributes = FileAttributes.Archive)
    {
        return Entries.File(path, size, write, access, attributes);
    }
}
