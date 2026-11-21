using System.Diagnostics;

namespace DiskAnalyzer;

public static class ExplorerSelector
{
    public static void OpenContainingFolder(string fullPath)
    {
        var start = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "/select,\"" + fullPath.Replace("\"", string.Empty) + "\"",
            UseShellExecute = true
        };
        Process.Start(start);
    }
}
