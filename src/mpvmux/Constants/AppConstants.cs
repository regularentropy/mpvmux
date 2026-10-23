using System;
using System.IO;

namespace mpvmux.Constants;

internal static class AppConstants
{
    public static readonly string BaseFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mpvmux");
    public static readonly string ConfigFilePath = Path.Combine(BaseFolderPath, "config.json");
    public static readonly string HistoryFilePath = Path.Combine(BaseFolderPath, "history.json");

    /// <summary>
    /// Last-known-good copies: bundles are keyed by the hash of their path, while mpvmux's own
    /// settings files keep one fixed slot each (<c>config.json.bak</c>, <c>history.json.bak</c>).
    /// </summary>
    public static readonly string BackupFolderPath = Path.Combine(BaseFolderPath, "backups");

    /// <summary>
    /// Files that failed validation: bundles moved out of their user directory, plus timestamped
    /// copies of damaged <c>config.json</c>/<c>history.json</c>.
    /// </summary>
    public static readonly string CorruptFolderPath = Path.Combine(BaseFolderPath, "corrupt");
    public static readonly string FileExtension = ".mpvm";

    public static readonly string[] VideoExtensions =
        [".mkv", ".mp4", ".m4v", ".avi", ".mov", ".webm", ".wmv", ".ts", ".m2ts", ".mpg", ".mpeg", ".flv", ".ogv"];

    public static readonly string[] AudioExtensions =
        [".flac", ".mp3", ".m4a", ".aac", ".ogg", ".opus", ".wav", ".wma", ".ac3", ".dts", ".mka", ".aiff"];

    public static readonly string[] SubtitleExtensions =
        [".srt", ".ass", ".ssa", ".vtt", ".sub", ".sup", ".idx", ".smi", ".ttml"];
}
