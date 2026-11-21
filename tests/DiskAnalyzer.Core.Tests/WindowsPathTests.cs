namespace DiskAnalyzer.Core.Tests;

public class WindowsPathTests
{
    [Theory]
    [InlineData(@"C:\Windows\System32", new[] { "Windows", "System32" })]
    [InlineData(@"C:\", new string[0])]
    [InlineData(@"\\?\C:\Program Files\A", new[] { "Program Files", "A" })]
    [InlineData(@"\\server\share\Windows\a", new[] { "Windows", "a" })]
    [InlineData(@"\\?\UNC\server\share\PerfLogs\a", new[] { "PerfLogs", "a" })]
    [InlineData(@"C:/Program Files (x86)/Vendor/a.zip", new[] { "Program Files (x86)", "Vendor", "a.zip" })]
    public void Segments_skip_the_drive_or_share(string path, string[] expected)
    {
        Assert.Equal(expected, WindowsPath.GetSegments(path));
    }

    [Theory]
    [InlineData(@"C:\Program Files\a.bin", @"C:\Program Files")]
    [InlineData(@"C:\a.bin", @"C:\")]
    [InlineData(@"\\server\share\a.bin", @"\\server\share")]
    public void Directory_of_a_file(string path, string expected)
    {
        Assert.Equal(expected, WindowsPath.GetDirectory(path));
    }

    [Theory]
    [InlineData(@"\\?\C:\Windows", @"C:\Windows")]
    [InlineData(@"\\?\UNC\server\share\a", @"\\server\share\a")]
    public void Extended_prefix_is_removed_for_display(string path, string expected)
    {
        Assert.Equal(expected, WindowsPath.ToDisplayPath(path));
    }

    [Theory]
    [InlineData(@"C:\Windows", @"\\?\C:\Windows")]
    [InlineData(@"\\?\C:\Windows", @"\\?\C:\Windows")]
    [InlineData(@"\\server\share\a", @"\\?\UNC\server\share\a")]
    [InlineData(@"\\?\UNC\server\share\a", @"\\?\UNC\server\share\a")]
    public void Extended_syntax_adds_the_long_path_prefix(string path, string expected)
    {
        Assert.Equal(expected, WindowsPath.ToExtendedSyntax(path));
    }

    [Fact]
    public void Drive_letter_without_slash_means_the_root()
    {
        Assert.Equal(@"C:\", WindowsPath.NormalizeRoot("C:"));
        Assert.Equal(@"D:\", WindowsPath.NormalizeRoot("d:"));
    }

    [Fact]
    public void Extension_and_file_name()
    {
        Assert.Equal("lib.DLL", WindowsPath.GetFileName(@"C:\Program Files\lib.DLL"));
        Assert.Equal(".DLL", WindowsPath.GetExtension(@"C:\Program Files\lib.DLL"));
        Assert.Equal(string.Empty, WindowsPath.GetExtension("Makefile"));
        Assert.Equal(string.Empty, WindowsPath.GetExtension(".gitignore"));
    }
}
