namespace mpvmux.Models.DTO;

public sealed record ConfigModelDTO
{
    public string MPVPath { get; set; } = "mpv";
    public bool EnableAutomaticPlayback { get; set; } = false;

    public override string ToString()
    {
        return $"MPVPath: {MPVPath} , EnableAutomaticPlayback: {EnableAutomaticPlayback}";
    }
}