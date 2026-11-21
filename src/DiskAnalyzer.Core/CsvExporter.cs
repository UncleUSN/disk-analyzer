using System.Globalization;
using System.Text;

namespace DiskAnalyzer.Core;

public static class CsvExporter
{
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// CSV с разделителем «;» и меткой UTF-8, чтобы Excel на русской Windows открыл кириллицу.
    /// Пишется только этот новый файл. Файлы из таблицы не изменяются.
    /// </summary>
    public static void Export(string path, IEnumerable<FileCandidate> rows)
    {
        using var writer = new StreamWriter(path, append: false, Utf8Bom);
        writer.WriteLine("Имя;Папка;Размер (байт);Размер;Последнее использование;Изменён;Расширение;Полный путь");
        foreach (var row in rows)
        {
            writer.Write(Cell(row.Name));
            writer.Write(';');
            writer.Write(Cell(row.Directory));
            writer.Write(';');
            writer.Write(row.SizeBytes.ToString(CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(Cell(row.SizeText));
            writer.Write(';');
            writer.Write(Cell(row.LastUsedText));
            writer.Write(';');
            writer.Write(Cell(row.LastWriteText));
            writer.Write(';');
            writer.Write(Cell(row.Extension));
            writer.Write(';');
            writer.Write(Cell(row.FullPath));
            writer.WriteLine();
        }
    }

    private static string Cell(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && IsFormulaPrefix(text[0]))
            text = "'" + text;

        var mustQuote = text.Contains(';') || text.Contains('"') || text.Contains('\n') || text.Contains('\r');
        if (!mustQuote)
            return text;

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    private static bool IsFormulaPrefix(char value)
    {
        return value is '=' or '+' or '-' or '@';
    }
}
