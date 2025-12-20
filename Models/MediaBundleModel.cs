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

    public MediaRecord SelectedRecord => new(VideoList[SelectedVideoIndex].Path, AudioList[SelectedAudioIndex].Path);

    public bool TryIncrement()
    {
        if (SelectedAudioIndex < AudioList.Count - 1 && SelectedVideoIndex < VideoList.Count - 1)
        {
            SelectedAudioIndex++;
            SelectedVideoIndex++;
            return true;
        }
        return false;
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
            SelectedAudioIndex = SelectedAudioIndex
        };
    }

    public static MediaBundleModel FromDto(MediaModelBundleDTO dto)
    {
        return new MediaBundleModel
        {
            VideoList = new ObservableCollection<MediaRecord>(dto.VideoList),
            AudioList = new ObservableCollection<MediaRecord>(dto.AudioList),
            SelectedVideoIndex = dto.SelectedVideoIndex,
            SelectedAudioIndex = dto.SelectedAudioIndex
        };
    }

}
