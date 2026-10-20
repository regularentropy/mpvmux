using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace mpvmux.Models;

internal partial class MediaBundleModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<MediaRecord> _videoList = [];

    [ObservableProperty]
    private ObservableCollection<MediaRecord> _audioList = [];

    [ObservableProperty]
    private int _selectedVideoIndex = -1;

    [ObservableProperty]
    private int _selectedAudioIndex = -1;

    [ObservableProperty] private int _volume = 100;

    public bool TryGetSelectedPaths(out string videoPath, out string audioPath)
    {
        videoPath = "";
        audioPath = "";

        if (SelectedVideoIndex < 0 || SelectedVideoIndex >= VideoList.Count ||
            SelectedAudioIndex < 0 || SelectedAudioIndex >= AudioList.Count)
            return false;

        videoPath = VideoList[SelectedVideoIndex].Path;
        audioPath = AudioList[SelectedAudioIndex].Path;

        return !string.IsNullOrWhiteSpace(videoPath) && !string.IsNullOrWhiteSpace(audioPath);
    }

    public bool TryIncrement()
    {
        // video drives the session length; audio clamps at its last item so a shorter
        // audio list keeps playing instead of ending auto-play early
        if (SelectedVideoIndex >= VideoList.Count - 1)
            return false;

        SelectedVideoIndex++;
        if (SelectedAudioIndex < AudioList.Count - 1)
            SelectedAudioIndex++;

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

        SelectedAudioIndex = 0;
    }

    public void Reset()
    {
        VideoList.Clear();
        AudioList.Clear();
        SelectedVideoIndex = -1;
        SelectedAudioIndex = -1;
    }
}
