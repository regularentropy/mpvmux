using Avalonia.Input;
using mpvmux.Constants;
using mpvmux.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace mpvmux.Services;

internal interface IConfigService
{
    ConfigModel Config { get; }
    bool IsDirty { get; }

    /// <summary>Writes pending changes. Returns false when the file could not be written.</summary>
    bool Save();

    /// <summary>
    /// How config.json was loaded. Returns the report once and clears it, so the startup
    /// notification is shown a single time.
    /// </summary>
    SettingsRecoveryReport? TakeRecoveryReport();
}

internal sealed class ConfigService : IConfigService
{
    private const string DefaultMpvPath = "mpv";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _configFilePath;
    private readonly SettingsBackupStore _backupStore;

    private SettingsRecoveryReport? _recoveryReport;

    public ConfigModel Config { get; private set; } = new();
    public bool IsDirty { get; private set; }

    public ConfigService()
        : this(AppConstants.ConfigFilePath)
    {
    }

    /// <summary>
    /// Test seam: the real file lives under the user's application data folder, while tests
    /// point at a temporary directory and must never read or write the real config.
    /// </summary>
    internal ConfigService(string configFilePath)
        : this(configFilePath, SettingsBackupStore.ForFile(configFilePath))
    {
    }

    internal ConfigService(string configFilePath, SettingsBackupStore backupStore)
    {
        _configFilePath = configFilePath;
        _backupStore = backupStore;

        EnsureDirectoryExists(configFilePath);

        // a crash during an earlier save may have left a stale temp file behind
        AtomicFile.CleanupTemp(_configFilePath);

        if (File.Exists(_configFilePath))
            LoadConfigSync();

        Config.PropertyChanged += OnConfigChanged;

        if (!File.Exists(_configFilePath))
        {
            _recoveryReport = new SettingsRecoveryReport(FileName, SettingsLoadOutcome.Fresh);
            IsDirty = true;
            Save();
        }
    }

    private string FileName => Path.GetFileName(_configFilePath);

    private static void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
            return;

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (IOException)
        {
            // an unwritable data folder must not stop startup; the failed save is reported there
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e) => IsDirty = true;

    public SettingsRecoveryReport? TakeRecoveryReport()
    {
        var report = _recoveryReport;
        _recoveryReport = null;
        return report;
    }

    public bool Save()
    {
        if (!IsDirty)
            return true;

        try
        {
            WriteConfig();
            return true;
        }
        catch (IOException)
        {
            // losing a settings write must not crash the app on shutdown; the next save retries
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void LoadConfigSync()
    {
        var text = ReadText(_configFilePath, out var readError);
        string? parseDetail = null;

        if (text is not null && TryParseConfig(text, out var loaded, out parseDetail))
        {
            ApplyLoadedConfig(loaded!);
            _recoveryReport = new SettingsRecoveryReport(FileName, SettingsLoadOutcome.Loaded);
            return;
        }

        var detail = readError ?? parseDetail;

        // keep the damaged original before anything can overwrite it
        var quarantined = _backupStore.Quarantine(_configFilePath);

        if (TryReadBackup(out var backup))
        {
            ApplyLoadedConfig(backup!);
            IsDirty = true;
            _recoveryReport = new SettingsRecoveryReport(
                FileName, SettingsLoadOutcome.RecoveredFromBackup, quarantined, detail);
        }
        else
        {
            // a damaged config must not stop startup; defaults are written while the original
            // is safely in the corrupt folder
            Config = new ConfigModel();
            IsDirty = true;
            _recoveryReport = new SettingsRecoveryReport(
                FileName, SettingsLoadOutcome.ResetAfterCorruption, quarantined, detail);
        }

        // Only replace the file once the damaged copy is out of the way. When quarantine failed,
        // leave the original untouched instead of immediately overwriting it; a later explicit
        // save may still replace it, which is the best that can be done while it is locked.
        if (quarantined is not null)
            Save();
    }

    private void ApplyLoadedConfig(ConfigModel config)
    {
        Config = config;

        var normalized = Normalize(Config);

        // configs written before per-shortcut scope had one switch that made every
        // shortcut global; keep that behaviour for existing users
        var migratedGlobals = Config.EnableGlobalShortcuts;
        if (migratedGlobals)
        {
            Config.GlobalShortcutActions = ShortcutActions.All.Select(action => action.Id).ToList();
            Config.EnableGlobalShortcuts = false;
        }

        IsDirty = normalized || migratedGlobals;
    }

    /// <summary>
    /// Repairs values the rest of the app relies on: a usable mpv path, a recent limit inside
    /// its range, and shortcut data that only references known actions with parseable gestures.
    /// JSON can produce nulls for non-nullable members, so every collection is treated as such.
    /// </summary>
    private static bool Normalize(ConfigModel config)
    {
        var changed = false;

        var mpvPath = config.MpvPath?.Trim();
        if (string.IsNullOrEmpty(mpvPath))
        {
            config.MpvPath = DefaultMpvPath;
            changed = true;
        }
        else if (!string.Equals(mpvPath, config.MpvPath, StringComparison.Ordinal))
        {
            config.MpvPath = mpvPath;
            changed = true;
        }

        var recentLimit = Math.Clamp(config.RecentLimit, ConfigModel.MinRecentLimit, ConfigModel.MaxRecentLimit);
        if (recentLimit != config.RecentLimit)
        {
            config.RecentLimit = recentLimit;
            changed = true;
        }

        var knownActions = ShortcutActions.All.Select(action => action.Id).ToHashSet(StringComparer.Ordinal);

        var overrides = (config.ShortcutOverrides ?? new Dictionary<string, string>())
            .Where(pair => knownActions.Contains(pair.Key) && IsUsableGesture(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        if (!SameEntries(config.ShortcutOverrides, overrides))
        {
            config.ShortcutOverrides = overrides;
            changed = true;
        }

        var globals = (config.GlobalShortcutActions ?? new List<string>())
            .Where(id => id is not null && knownActions.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!globals.SequenceEqual(config.GlobalShortcutActions ?? new List<string>()))
        {
            config.GlobalShortcutActions = globals;
            changed = true;
        }

        return changed;
    }

    private static bool SameEntries(Dictionary<string, string>? left, Dictionary<string, string> right)
    {
        if (left is null || left.Count != right.Count)
            return false;

        foreach (var pair in right)
        {
            if (!left.TryGetValue(pair.Key, out var value) ||
                !string.Equals(value, pair.Value, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>
    /// A hand-edited gesture that does not parse would otherwise reach the shortcut service
    /// only to be discarded there; dropping it here keeps the persisted config honest.
    /// </summary>
    private static bool IsUsableGesture(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            KeyGesture.Parse(text);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryParseConfig(string text, out ConfigModel? config, out string? detail)
    {
        config = null;
        detail = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            detail = "The file is empty or truncated.";
            return false;
        }

        try
        {
            config = JsonSerializer.Deserialize<ConfigModel>(text, JsonOptions);
        }
        catch (JsonException ex)
        {
            detail = $"The file is not valid JSON: {ex.Message}";
            return false;
        }
        catch (NotSupportedException ex)
        {
            detail = $"The file contains unsupported data: {ex.Message}";
            return false;
        }

        if (config is null)
        {
            detail = "The file does not contain a configuration object.";
            return false;
        }

        return true;
    }

    private bool TryReadBackup(out ConfigModel? config)
    {
        config = null;

        var text = _backupStore.Read(FileName);
        return text is not null && TryParseConfig(text, out config, out _);
    }

    private static string? ReadText(string path, out string? error)
    {
        try
        {
            error = null;
            return File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            error = "The file could not be read: " + ex.Message;
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = "The file could not be read: " + ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Keeps the version still on disk as the last known good copy before it is replaced. Only
    /// a file that parses is promoted, so a damaged file never replaces a good backup — the
    /// same rule the bundle store follows.
    /// </summary>
    private void PromoteBackup()
    {
        var current = ReadText(_configFilePath, out _);
        if (current is null || !TryParseConfig(current, out _, out _))
            return;

        _backupStore.Write(FileName, current);
    }

    private void WriteConfig()
    {
        PromoteBackup();

        // serialize to text so the shared writer owns the tmp/flush/rename sequence
        var json = JsonSerializer.Serialize(Config, JsonOptions);
        AtomicFile.WriteAllText(_configFilePath, json);
        IsDirty = false;
    }
}
