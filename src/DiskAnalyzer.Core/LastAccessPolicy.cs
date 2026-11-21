using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DiskAnalyzer.Core;

public enum LastAccessReliability
{
    UpdatesEnabled,
    UpdatesDisabled,
    Unknown
}

public sealed record LastAccessProbeResult(LastAccessReliability Reliability, string? Source, long? RawValue)
{
    public static LastAccessProbeResult Unknown { get; } = new(LastAccessReliability.Unknown, null, null);
}

/// <summary>
/// Windows часто не обновляет LastAccessTime. Тогда «последнее использование»
/// почти равно дате изменения, и недавно открытый файл выглядит старым.
/// </summary>
public static partial class LastAccessPolicy
{
    public static LastAccessProbeResult Detect()
    {
        try
        {
            var fromRegistry = TryReadRegistry();
            if (fromRegistry is not null)
                return fromRegistry;
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Ниже попробуем fsutil, а если и он недоступен — честно скажем «неизвестно».
        }

        try
        {
            var fromFsutil = TryQueryFsutil();
            if (fromFsutil is not null)
                return fromFsutil;
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }

        return LastAccessProbeResult.Unknown;
    }

    /// <summary>
    /// Младшие два бита NtfsDisableLastAccessUpdate:
    /// 0 — обновления включены (настройка пользователя),
    /// 1 — выключены (пользователь),
    /// 2 — выключены (система, обычное состояние Windows 10/11),
    /// 3 — включены (система).
    /// Старший бит 0x80000000 на смысл младших битов не влияет.
    /// </summary>
    public static LastAccessReliability InterpretRawValue(long raw)
    {
        var mode = (int)(raw & 0x3);
        return mode is 1 or 2
            ? LastAccessReliability.UpdatesDisabled
            : LastAccessReliability.UpdatesEnabled;
    }

    public static bool TryParseFsutilOutput(string? output, out LastAccessReliability reliability, out long rawValue)
    {
        reliability = LastAccessReliability.Unknown;
        rawValue = 0;
        if (string.IsNullOrWhiteSpace(output))
            return false;

        var match = FsutilPattern().Match(output);
        if (!match.Success)
            return false;
        if (!long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out rawValue))
            return false;

        reliability = InterpretRawValue(rawValue);
        return true;
    }

    private static LastAccessProbeResult? TryReadRegistry()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem", writable: false);
        var value = key?.GetValue("NtfsDisableLastAccessUpdate");
        if (value is null)
            return null;

        var raw = value switch
        {
            int signed => unchecked((uint)signed),
            uint unsigned => unsigned,
            long wide => wide,
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
        };

        return new LastAccessProbeResult(InterpretRawValue(raw), "registry", raw);
    }

    private static LastAccessProbeResult? TryQueryFsutil()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var exe = Path.Combine(Environment.SystemDirectory, "fsutil.exe");
        if (!File.Exists(exe))
            return null;

        var start = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = "behavior query disablelastaccess",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = start };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
                output.AppendLine(eventArgs.Data);
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
                output.AppendLine(eventArgs.Data);
        };

        if (!process.Start())
            return null;

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(4000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            return null;
        }

        process.WaitForExit();
        if (!TryParseFsutilOutput(output.ToString(), out var reliability, out var raw))
            return null;

        return new LastAccessProbeResult(reliability, "fsutil", raw);
    }

    [GeneratedRegex(@"disablelastaccess\s*=\s*(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FsutilPattern();
}
