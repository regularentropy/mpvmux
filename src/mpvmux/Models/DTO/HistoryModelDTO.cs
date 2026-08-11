using mpvmux.Models;
using System.Collections.Generic;

namespace mpvmux.Services;

internal class HistoryModelDTO
{
    public string? LastDatabasePath { get; set; } = "";
    public List<MediaRecord> History { get; set; } = [];

    public override string ToString()
    {
        return $"LastDatabasePath: {LastDatabasePath}, History: [{string.Join(", ", History)}]";
    }
}