using CommunityToolkit.Mvvm.ComponentModel;
using mpvmux.Models.DTO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

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

    public MediaRecord SelectedRecord => new(VideoList[SelectedVideoIndex].Path, AudioList[SelectedAudioIndex].Path);

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


    public MediaModelBundleDTO ToDto()
    {
        return new MediaModelBundleDTO
        {
            VideoList = VideoList.ToList(),
            AudioList = AudioList.ToList(),
            SelectedVideoIndex = SelectedVideoIndex,
            SelectedAudioIndex = SelectedAudioIndex,
            Volume = Volume
        };
    }

    public static MediaBundleModel FromDto(MediaModelBundleDTO dto)
    {
        return new MediaBundleModel
        {
            VideoList = new ObservableCollection<MediaRecord>(dto.VideoList),
            AudioList = new ObservableCollection<MediaRecord>(dto.AudioList),
            SelectedVideoIndex = dto.SelectedVideoIndex,
            SelectedAudioIndex = dto.SelectedAudioIndex,
            Volume = dto.Volume
        };
    }

}
