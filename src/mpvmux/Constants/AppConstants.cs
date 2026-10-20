using System;
using System.IO;

namespace mpvmux.Constants;

internal static class AppConstants
{
    public static readonly string BaseFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mpvmux");
    public static readonly string ConfigFilePath = Path.Combine(BaseFolderPath, "config.json");
    public static readonly string HistoryFilePath = Path.Combine(BaseFolderPath, "history.json");
    public static readonly string FileExtension = ".mpvm";

    public static readonly string[] VideoExtensions =
        [".mkv", ".mp4", ".m4v", ".avi", ".mov", ".webm", ".wmv", ".ts", ".m2ts", ".mpg", ".mpeg", ".flv", ".ogv"];

    public static readonly string[] AudioExtensions =
        [".flac", ".mp3", ".m4a", ".aac", ".ogg", ".opus", ".wav", ".wma", ".ac3", ".dts", ".mka", ".aiff"];
}
