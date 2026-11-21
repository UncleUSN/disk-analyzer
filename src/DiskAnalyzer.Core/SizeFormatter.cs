using System.Globalization;

namespace DiskAnalyzer.Core;

public static class SizeFormatter
{
    private static readonly string[] Units = ["Б", "КБ", "МБ", "ГБ", "ТБ", "ПБ"];

    /// <summary>Размер в двоичных единицах: 1 МБ = 1024 × 1024 байт, как в проводнике Windows.</summary>
    public static string Format(long bytes)
    {
        if (bytes < 0)
            bytes = 0;

        double value = bytes;
        var unit = 0;
        while (value >= 1024d && unit < Units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        if (unit == 0)
            return bytes.ToString(CultureInfo.InvariantCulture) + " " + Units[0];

        var text = value.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');
        return text + " " + Units[unit];
    }
}
