using System.Collections.Generic;

namespace mpvmux.Models.DTO;

internal class MediaModelBundleDTO
{
    public List<MediaRecord> VideoList { get; set; } = [];
    public List<MediaRecord> AudioList { get; set; } = [];
    public int SelectedVideoIndex { get; set; } = -1;
    public int SelectedAudioIndex { get; set; } = -1;

    public override string ToString()
    {
        return $"Indexes:  Audio - {SelectedVideoIndex}, Video {SelectedAudioIndex}";
    }
}
