using System.IO;
using System.Windows.Input;
using mpvmux.Models;

namespace mpvmux.ViewModels;

/// <summary>
/// One row of the track table: a video (the row's spine) plus its optional audio and
/// subtitle cells. Rows are rebuilt when any of the three lists changes.
/// </summary>
internal sealed class TrackRow(
    TrackTableViewModel owner,
    int index,
    MediaRecord? video,
    MediaRecord? audio,
    MediaRecord? subtitle)
{
    public int Index { get; } = index;

    public MediaRecord? Video { get; } = video;

    public MediaRecord? Audio { get; } = audio;

    public MediaRecord? Subtitle { get; } = subtitle;

    public string VideoName => Video?.Name ?? "—";

    public string AudioName => Audio?.Name ?? "—";

    public string SubtitleName => Subtitle?.Name ?? "—";

    public string? VideoToolTip => ToolTipFor(Video);

    public string? AudioToolTip => ToolTipFor(Audio);

    public string? SubtitleToolTip => ToolTipFor(Subtitle);

    public bool HasVideo => Video is not null;

    public bool HasAudio => Audio is not null;

    public bool HasSubtitle => Subtitle is not null;

    public bool IsPlayable => HasVideo;

    public ICommand RemoveVideoCommand => owner.RemoveVideoCommand;

    public ICommand RemoveAudioCommand => owner.RemoveAudioCommand;

    public ICommand RemoveSubtitleCommand => owner.RemoveSubtitleCommand;

    public ICommand ReplaceVideoCommand => owner.ReplaceVideoCommand;

    public ICommand ReplaceAudioCommand => owner.ReplaceAudioCommand;

    public ICommand ReplaceSubtitleCommand => owner.ReplaceSubtitleCommand;

    public bool HasSameData(TrackRow other) =>
        Index == other.Index &&
        Equals(Video, other.Video) &&
        Equals(Audio, other.Audio) &&
        Equals(Subtitle, other.Subtitle);

    // the cell text is trimmed, so the tooltip carries the full file name and path
    private static string? ToolTipFor(MediaRecord? record) =>
        record is null ? null : $"{Path.GetFileName(record.Path)}\n{record.Path}";
}
