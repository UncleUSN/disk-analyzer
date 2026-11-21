namespace DiskAnalyzer.Core.Tests;

public class SizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 Б")]
    [InlineData(1023, "1023 Б")]
    [InlineData(1024, "1 КБ")]
    [InlineData(1536, "1,5 КБ")]
    [InlineData(50L * 1024 * 1024, "50 МБ")]
    [InlineData(1536L * 1024 * 1024, "1,5 ГБ")]
    public void Formats_binary_units(long bytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.Format(bytes));
    }
}
