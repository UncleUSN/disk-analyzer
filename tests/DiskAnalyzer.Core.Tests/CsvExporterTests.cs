using System.Text;

namespace DiskAnalyzer.Core.Tests;

public class CsvExporterTests
{
    [Fact]
    public void Export_writes_utf8_bom_semicolon_header_and_quotes()
    {
        var candidate = new FileCandidate
        {
            Name = "=cmd",
            Directory = @"C:\A;B",
            FullPath = @"C:\A;B\=cmd",
            SizeBytes = 123456,
            SizeText = "120,56 КБ",
            LastUsedUtc = DateTimeOffset.UnixEpoch,
            LastWriteUtc = DateTimeOffset.UnixEpoch,
            LastAccessUtc = DateTimeOffset.UnixEpoch,
            Extension = ".bin"
        };
        var path = Path.Combine(Path.GetTempPath(), "disk-analyzer-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            CsvExporter.Export(path, [candidate]);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            var text = Encoding.UTF8.GetString(bytes);
            Assert.Contains("Имя;Папка;Размер (байт);Размер;Последнее использование;Изменён;Расширение;Полный путь", text);
            Assert.Contains("123456", text);
            Assert.Contains("'=cmd", text);
            Assert.Contains("\"C:\\A;B\"", text);
            Assert.Contains(candidate.LastUsedText, text);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
