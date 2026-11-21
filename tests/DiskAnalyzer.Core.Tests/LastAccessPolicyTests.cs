namespace DiskAnalyzer.Core.Tests;

public class LastAccessPolicyTests
{
    [Theory]
    [InlineData(0, LastAccessReliability.UpdatesEnabled)]
    [InlineData(1, LastAccessReliability.UpdatesDisabled)]
    [InlineData(2, LastAccessReliability.UpdatesDisabled)]
    [InlineData(3, LastAccessReliability.UpdatesEnabled)]
    [InlineData(0x80000000, LastAccessReliability.UpdatesEnabled)]
    [InlineData(0x80000002, LastAccessReliability.UpdatesDisabled)]
    [InlineData(0x80000003, LastAccessReliability.UpdatesEnabled)]
    public void Registry_value_uses_the_low_two_bits(long raw, LastAccessReliability expected)
    {
        Assert.Equal(expected, LastAccessPolicy.InterpretRawValue(raw));
    }

    [Theory]
    [InlineData("DisableLastAccess = 2", LastAccessReliability.UpdatesDisabled, 2)]
    [InlineData("DisableLastAccess = 2  (System Managed, Last Access Updates Disabled)", LastAccessReliability.UpdatesDisabled, 2)]
    [InlineData("disablelastaccess=0", LastAccessReliability.UpdatesEnabled, 0)]
    [InlineData("NTFS DisableLastAccess = 3\r\n", LastAccessReliability.UpdatesEnabled, 3)]
    public void Fsutil_output_is_parsed(string output, LastAccessReliability expected, long raw)
    {
        Assert.True(LastAccessPolicy.TryParseFsutilOutput(output, out var reliability, out var value));
        Assert.Equal(expected, reliability);
        Assert.Equal(raw, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("command failed")]
    [InlineData("Error 5: Access is denied.")]
    public void Unusable_fsutil_output_is_rejected(string output)
    {
        Assert.False(LastAccessPolicy.TryParseFsutilOutput(output, out _, out _));
    }

    [Fact]
    public void Detect_does_not_throw()
    {
        var result = LastAccessPolicy.Detect();
        Assert.True(Enum.IsDefined(result.Reliability));
    }
}
