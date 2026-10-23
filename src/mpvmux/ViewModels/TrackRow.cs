using System.IO;
using System.Windows.Input;
using mpvmux.Models;

namespace mpvmux.ViewModels;

/// <summary>
/// Detection state of one cell: whether the referenced file is gone, and the file name of the
/// best replacement when one was found.
/// </summary>
internal sealed record TrackCellState(bool IsMissing, string? SuggestedName)
{
    public static TrackCellState Available { get; } = new(false, null);
}

/// <summary>
/// One row of the track table: a video (the row's spine) plus its optional audio and
/// subtitle cells. Rows are rebuilt when any of the three lists changes.
/// </summary>
internal sealed class TrackRow(
    TrackTableViewModel owner,
    int index,
    MediaRecord? video,
    MediaRecord? audio,
    MediaRecord? subtitle,
    TrackCellState videoState,
    TrackCellState audioState,
    TrackCellState subtitleState)
{
    public int Index { get; } = index;

    public MediaRecord? Video { get; } = video;

    public MediaRecord? Audio { get; } = audio;

    public MediaRecord? Subtitle { get; } = subtitle;

    public TrackCellState VideoState { get; } = videoState;

    public TrackCellState AudioState { get; } = audioState;

    public TrackCellState SubtitleState { get; } = subtitleState;

    public string VideoName => Video?.Name ?? "—";

    public string AudioName => Audio?.Name ?? "—";

    public string SubtitleName => Subtitle?.Name ?? "—";

    public string? VideoToolTip => ToolTipFor(Video, VideoState);

    public string? AudioToolTip => ToolTipFor(Audio, AudioState);

    public string? SubtitleToolTip => ToolTipFor(Subtitle, SubtitleState);

    public bool IsVideoMissing => VideoState.IsMissing;

    public bool IsAudioMissing => AudioState.IsMissing;

    public bool IsSubtitleMissing => SubtitleState.IsMissing;

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
        Equals(Subtitle, other.Subtitle) &&
        VideoState == other.VideoState &&
        AudioState == other.AudioState &&
        SubtitleState == other.SubtitleState;

    // the cell text is trimmed, so the tooltip carries the full file name and path
    private static string? ToolTipFor(MediaRecord? record, TrackCellState state)
    {
        if (record is null)
            return null;

        if (!state.IsMissing)
            return $"{Path.GetFileName(record.Path)}\n{record.Path}";

        var tip = $"File not found\n{record.Path}";
        return state.SuggestedName is { } suggested
            ? $"{tip}\nPossible match: {suggested}"
            : tip;
    }
}
