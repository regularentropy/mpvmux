using System;
using System.IO;

namespace mpvmux.Constants;

internal static class AppConstants
{
    public static readonly string BaseFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mpvmux");
    public static readonly string ConfigFilePath = Path.Combine(BaseFolderPath, "config.json");
    public static readonly string HistoryFilePath = Path.Combine(BaseFolderPath, "history.json");
    public static readonly string FileExtension = ".mpvm";
}
