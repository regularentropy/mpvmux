using CommunityToolkit.Mvvm.ComponentModel;
using mpvmux.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace mpvmux.Models;

internal partial class HistoryModel : ObservableObject
{
    [ObservableProperty] private string? _lastDatabasePath = "";

    [ObservableProperty] private ObservableCollection<MediaRecord> _history = [];

    public HistoryModelDTO ToDto()
    {
        return new HistoryModelDTO()
        {
            LastDatabasePath = LastDatabasePath,
            History = History.ToList()
        };
    }

    public static HistoryModel FromDto(HistoryModelDTO DTO)
    {
        return new HistoryModel()
        {
            LastDatabasePath = DTO.LastDatabasePath,
            History = new ObservableCollection<MediaRecord>(DTO.History)
        };
    }
}
