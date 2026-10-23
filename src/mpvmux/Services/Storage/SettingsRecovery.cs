namespace mpvmux.Services;

/// <summary>How one of mpvmux's own JSON files was loaded during startup.</summary>
internal enum SettingsLoadOutcome
{
    /// <summary>No file existed yet; defaults were written.</summary>
    Fresh,

    /// <summary>The file was read, normalized and used as-is.</summary>
    Loaded,

    /// <summary>The file was damaged; a validated backup was restored in its place.</summary>
    RecoveredFromBackup,

    /// <summary>The file was damaged and no usable backup existed; defaults are in use.</summary>
    ResetAfterCorruption
}

/// <summary>
/// What happened to one settings file, kept until the startup report consumes it. The reason
/// carries the parser or reader failure so a bug report can be matched to a quarantined file.
/// </summary>
internal sealed record SettingsRecoveryReport(
    string FileName,
    SettingsLoadOutcome Outcome,
    string? QuarantinedPath = null,
    string? Detail = null)
{
    /// <summary>True when the user lost settings and should be told what happened.</summary>
    public bool NeedsUserAttention =>
        Outcome is SettingsLoadOutcome.RecoveredFromBackup or SettingsLoadOutcome.ResetAfterCorruption;
}
