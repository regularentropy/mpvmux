using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace mpvmux.Models;

internal partial class MediaBundleModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<MediaRecord> _videoList = [];

    [ObservableProperty]
    private ObservableCollection<MediaRecord> _audioList = [];

    [ObservableProperty]
    private ObservableCollection<MediaRecord> _subtitleList = [];

    [ObservableProperty]
    private int _selectedVideoIndex = -1;

    [ObservableProperty] private int _volume = 100;

    [JsonIgnore]
    public bool IsMuted => Volume == 0;

    partial void OnVolumeChanged(int value) => OnPropertyChanged(nameof(IsMuted));

    public bool TryGetSelectedTracks(out string videoPath, out string? audioPath, out string? subtitlePath)
    {
        videoPath = "";
        audioPath = null;
        subtitlePath = null;

        if (SelectedVideoIndex < 0 || SelectedVideoIndex >= VideoList.Count)
            return false;

        videoPath = VideoList[SelectedVideoIndex].Path;
        if (string.IsNullOrWhiteSpace(videoPath))
            return false;

        if (SelectedVideoIndex < AudioList.Count)
            audioPath = UsablePath(AudioList[SelectedVideoIndex].Path);

        if (SelectedVideoIndex < SubtitleList.Count)
            subtitlePath = UsablePath(SubtitleList[SelectedVideoIndex].Path);

        return true;
    }

    private static string? UsablePath(string path) =>
        string.IsNullOrWhiteSpace(path) ? null : path;

    public bool TryIncrement()
    {
        // video rows drive the session length; audio and subtitles follow the row index
        // and are simply absent when their lists run out
        if (SelectedVideoIndex < 0 || SelectedVideoIndex >= VideoList.Count - 1)
            return false;

        SelectedVideoIndex++;
        return true;
    }


    public void SetVideoList(List<MediaRecord> list)
    {
        VideoList.Clear();
        foreach (var record in list)
        {
            VideoList.Add(record);
        }
        
        SelectedVideoIndex = 0;
    }

    public void SetAudioList(List<MediaRecord> list)
    {
        AudioList.Clear();
        foreach (var record in list)
        {
            AudioList.Add(record);
        }
    }

    public void SetSubtitleList(List<MediaRecord> list)
    {
        SubtitleList.Clear();
        foreach (var record in list)
        {
            SubtitleList.Add(record);
        }
    }
}
