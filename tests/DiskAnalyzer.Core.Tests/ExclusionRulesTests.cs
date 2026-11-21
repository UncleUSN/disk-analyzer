namespace DiskAnalyzer.Core.Tests;

public class ExclusionRulesTests
{
    [Fact]
    public void Default_root_directories_are_the_system_folders_without_program_files()
    {
        var rules = ExclusionRules.LoadDefault();
        var expected = new[]
        {
            "Windows",
            "ProgramData",
            "System Volume Information",
            "$Recycle.Bin",
            "$WinREAgent",
            "Recovery",
            "Boot",
            "EFI",
            "PerfLogs"
        };

        Assert.Equal(
            expected.OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
            rules.ExcludedRootDirectories.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain(rules.ExcludedRootDirectories, name => name.Contains("Program Files", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(rules.ExcludedRelativePaths, path => path.Contains("Program Files", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Default_extensions_names_and_profile_paths()
    {
        var rules = ExclusionRules.LoadDefault();
        Assert.Equal(
            new[] { ".dll", ".drv", ".exe", ".msi", ".sys" },
            rules.ExcludedExtensions.Select(item => item.ToLowerInvariant()).OrderBy(item => item).ToArray());
        Assert.Contains("pagefile.sys", rules.ExcludedFileNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("hiberfil.sys", rules.ExcludedFileNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("swapfile.sys", rules.ExcludedFileNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("DumpStack", rules.ExcludedFileNamePrefixes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("AppData/Local/Programs", rules.ExcludedRelativePaths);
        Assert.Contains("AppData/Local/Microsoft", rules.ExcludedRelativePaths);
        Assert.Contains("AppData/Local/Packages", rules.ExcludedRelativePaths);
    }

    [Fact]
    public void Embedded_default_matches_the_config_file_on_disk()
    {
        var disk = ExclusionRules.Load(FindConfig());
        var embedded = ExclusionRules.LoadDefault();
        Assert.Equal(Sorted(disk.ExcludedRootDirectories), Sorted(embedded.ExcludedRootDirectories));
        Assert.Equal(Sorted(disk.ExcludedExtensions), Sorted(embedded.ExcludedExtensions));
        Assert.Equal(Sorted(disk.ExcludedRelativePaths), Sorted(embedded.ExcludedRelativePaths));
        Assert.Equal(Sorted(disk.ExcludedFileNames), Sorted(embedded.ExcludedFileNames));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\config")]
    [InlineData(@"\\?\C:\Windows\System32")]
    [InlineData(@"D:\ProgramData\Microsoft\cache.dat")]
    [InlineData(@"\\server\share\PerfLogs\a.etl")]
    [InlineData(@"\\?\UNC\server\share\EFI\Boot\bootx64.efi")]
    [InlineData(@"C:\Users\Ivan\AppData\Local\Microsoft\Windows\INetCache\a.dat")]
    public void Default_rules_exclude_system_paths(string path)
    {
        Assert.True(ExclusionRules.LoadDefault().IsExcludedPath(path));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Vendor\cache.dat")]
    [InlineData(@"C:\Program Files (x86)\Vendor\archive.zip")]
    [InlineData(@"D:\Program Files\App\logs\setup.log")]
    [InlineData(@"C:\Windows.old\Users\movie.iso")]
    [InlineData(@"C:\Users\Ivan\AppData\Local\Temp\old.bin")]
    [InlineData(@"C:\Users\Ivan\AppData\Roaming\Mail\archive.pst")]
    [InlineData(@"C:\Users\Ivan\Documents\video.mkv")]
    public void Default_rules_keep_user_and_program_files_paths(string path)
    {
        Assert.False(ExclusionRules.LoadDefault().IsExcludedPath(path));
    }

    [Fact]
    public void Rules_come_from_the_json_file()
    {
        var path = WriteTemp("""
            {
              // комментарий можно оставить
              "excludedRootDirectories": ["CustomSystem",],
              "excludedRelativePaths": ["Cache/Blob"],
              "excludedFileNames": ["keep-me.bin"],
              "excludedFileNamePrefixes": ["DumpStack"],
              "excludedExtensions": [".tmp"]
            }
            """);

        var rules = ExclusionRules.Load(path);
        Assert.True(rules.IsExcludedPath(@"D:\CustomSystem\a.bin"));
        Assert.False(rules.IsExcludedPath(@"D:\Windows\a.bin"));
        Assert.True(rules.IsExcludedPath(@"D:\Work\Cache\Blob\a.bin"));
        Assert.True(rules.IsExcludedFileName("keep-me.bin"));
        Assert.True(rules.IsExcludedFileName("DumpStack.log"));
        Assert.True(rules.IsExcludedExtension(".TMP"));
        Assert.False(rules.IsExcludedExtension(".exe"));
    }

    [Fact]
    public void Empty_object_excludes_nothing()
    {
        var rules = ExclusionRules.Parse("{}");
        Assert.False(rules.IsExcludedPath(@"C:\Windows\a.bin"));
        Assert.False(rules.IsExcludedExtension(".exe"));
        Assert.False(rules.IsExcludedFileName("pagefile.sys"));
    }

    [Fact]
    public void Invalid_json_explains_the_problem()
    {
        var path = WriteTemp("{");
        var error = Assert.Throws<InvalidDataException>(() => ExclusionRules.Load(path));
        Assert.Contains("повреждён", error.Message);
    }

    private static string FindConfig()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "config", "exclusions.json");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("config/exclusions.json");
    }

    private static string WriteTemp(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "disk-analyzer-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string[] Sorted(IEnumerable<string> values)
    {
        return values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
