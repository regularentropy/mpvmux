using mpvmux.Models;
using mpvmux.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace mpvmux.Tests;

/// <summary>
/// config.json and history.json are the only place user settings and history live, so a damaged
/// file must never crash startup, never be overwritten in place, and never lose the last good
/// copy. Every test works in its own temporary folder and never touches %APPDATA%\mpvmux.
/// </summary>
public sealed class StorageCorruptionTests : IDisposable
{
    private readonly string _directory;

    public StorageCorruptionTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "mpvmux-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string ConfigPath => Path.Combine(_directory, "config.json");
    private string HistoryPath => Path.Combine(_directory, "history.json");
    private string BackupFolder => Path.Combine(_directory, "backups");
    private string CorruptFolder => Path.Combine(_directory, "corrupt");

    private string ConfigBackupPath => Path.Combine(BackupFolder, "config.json.bak");
    private string HistoryBackupPath => Path.Combine(BackupFolder, "history.json.bak");

    private string[] QuarantinedFiles() =>
        Directory.Exists(CorruptFolder)
            ? Directory.GetFiles(CorruptFolder, "*.corrupt")
            : [];

    private void WriteConfigFile(string text) => File.WriteAllText(ConfigPath, text);
    private void WriteHistoryFile(string text) => File.WriteAllText(HistoryPath, text);

    private void WriteBackup(string path, string text)
    {
        Directory.CreateDirectory(BackupFolder);
        File.WriteAllText(path, text);
    }

    // ---- config.json -------------------------------------------------------------

    [Fact]
    public void Missing_config_is_created_with_defaults_and_reported_as_fresh()
    {
        var service = new ConfigService(ConfigPath);

        Assert.Equal(SettingsLoadOutcome.Fresh, service.TakeRecoveryReport()?.Outcome);
        Assert.True(File.Exists(ConfigPath));
        Assert.Equal(ConfigModel.DefaultRecentLimit, service.Config.RecentLimit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("not json at all")]
    [InlineData("null")]
    [InlineData("""{"RecentLimit":"many"}""")]
    public void Damaged_config_is_quarantined_instead_of_crashing(string text)
    {
        WriteConfigFile(text);

        var service = new ConfigService(ConfigPath);
        var report = service.TakeRecoveryReport();

        Assert.Equal(SettingsLoadOutcome.ResetAfterCorruption, report?.Outcome);
        Assert.Equal("config.json", report?.FileName);
        Assert.NotNull(report?.QuarantinedPath);

        // the evidence survives, byte for byte
        var quarantined = QuarantinedFiles();
        Assert.Single(quarantined);
        Assert.Equal(text, File.ReadAllText(quarantined[0]));

        // and the app is left with a usable file, not the damaged one
        Assert.Equal(ConfigModel.DefaultRecentLimit, service.Config.RecentLimit);
        Assert.NotEqual(text, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void Damaged_config_restores_the_last_good_backup()
    {
        WriteBackup(ConfigBackupPath, """{"RecentLimit":37,"MPVPath":"/opt/mpv"}""");
        WriteConfigFile("{ this is not json");

        var service = new ConfigService(ConfigPath);
        var report = service.TakeRecoveryReport();

        Assert.Equal(SettingsLoadOutcome.RecoveredFromBackup, report?.Outcome);
        Assert.Equal(37, service.Config.RecentLimit);
        Assert.Equal("/opt/mpv", service.Config.MpvPath);

        // the restored copy is written back, so the next launch is clean
        Assert.Equal(37, JsonSerializer.Deserialize<ConfigModel>(File.ReadAllText(ConfigPath))!.RecentLimit);
    }

    [Fact]
    public void A_backup_that_does_not_validate_is_not_used_and_is_left_alone()
    {
        const string brokenBackup = "{ also broken";
        WriteBackup(ConfigBackupPath, brokenBackup);
        WriteConfigFile("garbage");

        var service = new ConfigService(ConfigPath);

        Assert.Equal(SettingsLoadOutcome.ResetAfterCorruption, service.TakeRecoveryReport()?.Outcome);
        Assert.Equal(ConfigModel.DefaultRecentLimit, service.Config.RecentLimit);
        Assert.Equal(brokenBackup, File.ReadAllText(ConfigBackupPath));
    }

    [Fact]
    public void Saving_promotes_the_parseable_file_into_the_backup_slot()
    {
        const string first = """{"RecentLimit":21,"MPVPath":"mpv"}""";
        WriteConfigFile(first);

        var service = new ConfigService(ConfigPath);
        service.Config.RecentLimit = 22;
        Assert.True(service.Save());

        Assert.Equal(first, File.ReadAllText(ConfigBackupPath));
        Assert.Contains("\"RecentLimit\": 22", File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void A_damaged_file_never_replaces_a_good_backup()
    {
        const string good = """{"RecentLimit":21,"MPVPath":"mpv"}""";
        WriteConfigFile(good);

        var first = new ConfigService(ConfigPath);
        first.Config.RecentLimit = 22;
        first.Save();
        Assert.Equal(good, File.ReadAllText(ConfigBackupPath));

        WriteConfigFile("{ damaged on disk");

        var second = new ConfigService(ConfigPath);

        Assert.Equal(SettingsLoadOutcome.RecoveredFromBackup, second.TakeRecoveryReport()?.Outcome);
        Assert.Equal(21, second.Config.RecentLimit);
        Assert.Equal(good, File.ReadAllText(ConfigBackupPath));
    }

    [Fact]
    public void A_stale_temp_file_is_cleaned_up_on_load()
    {
        WriteConfigFile("""{"RecentLimit":12}""");
        File.WriteAllText(ConfigPath + ".tmp", "{ half written");

        var service = new ConfigService(ConfigPath);

        Assert.Equal(12, service.Config.RecentLimit);
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public void Saving_leaves_no_temp_file_behind()
    {
        var service = new ConfigService(ConfigPath);
        service.Config.RecentLimit = 13;

        Assert.True(service.Save());
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public void A_directory_where_the_config_belongs_does_not_crash_startup()
    {
        Directory.CreateDirectory(ConfigPath);

        var service = new ConfigService(ConfigPath);

        Assert.NotNull(service.TakeRecoveryReport());
        Assert.Equal(ConfigModel.DefaultRecentLimit, service.Config.RecentLimit);
    }

    [Fact]
    public void Parsed_config_is_normalized_to_values_the_app_relies_on()
    {
        WriteConfigFile("""
            {
              "MPVPath": "   ",
              "RecentLimit": 999,
              "ShortcutOverrides": {
                "File.New": "Ctrl+N",
                "File.Open": "not a gesture",
                "Unknown.Action": "Ctrl+U"
              },
              "GlobalShortcutActions": ["File.New", "No.Such.Action", "File.New"]
            }
            """);

        var service = new ConfigService(ConfigPath);

        Assert.Equal(SettingsLoadOutcome.Loaded, service.TakeRecoveryReport()?.Outcome);
        Assert.Equal("mpv", service.Config.MpvPath);
        Assert.Equal(ConfigModel.MaxRecentLimit, service.Config.RecentLimit);
        Assert.Equal(["File.New"], service.Config.ShortcutOverrides.Keys);
        Assert.Equal("Ctrl+N", service.Config.ShortcutOverrides["File.New"]);
        Assert.Equal(["File.New"], service.Config.GlobalShortcutActions);
    }

    [Fact]
    public void The_recovery_report_is_consumed_once()
    {
        WriteConfigFile("garbage");

        var service = new ConfigService(ConfigPath);

        Assert.NotNull(service.TakeRecoveryReport());
        Assert.Null(service.TakeRecoveryReport());
    }

    // ---- history.json ------------------------------------------------------------

    [Fact]
    public void Null_history_collections_do_not_crash_and_stay_empty()
    {
        WriteHistoryFile("""{"LastDatabasePath":null,"History":null,"PinnedPaths":null,"CorruptPaths":null}""");

        var service = new HistoryService(HistoryPath);
        var report = service.TakeRecoveryReport();

        Assert.Equal(SettingsLoadOutcome.Loaded, report?.Outcome);
        Assert.Empty(service.HistoryModel.History);
        Assert.Empty(service.HistoryModel.PinnedPaths);
        Assert.Empty(service.HistoryModel.CorruptPaths);
    }

    [Fact]
    public void Null_and_duplicate_records_are_dropped_and_pins_are_rebuilt()
    {
        var bundlePath = Path.Combine(_directory, "a.mpvm");
        File.WriteAllText(bundlePath, "{}");

        WriteHistoryFile($$"""
            {
              "History": [
                null,
                {"Name":"A","Path":{{JsonSerializer.Serialize(bundlePath)}}},
                {"Name":"duplicate","Path":{{JsonSerializer.Serialize(bundlePath)}}},
                {"Name":"blank","Path":"   "}
              ],
              "PinnedPaths": [{{JsonSerializer.Serialize(bundlePath)}}, {{JsonSerializer.Serialize(bundlePath)}}, "", null],
              "CorruptPaths": ["/nowhere/missing.mpvm"]
            }
            """);

        var service = new HistoryService(HistoryPath);

        var record = Assert.Single(service.HistoryModel.History);
        Assert.Equal(bundlePath, record.Path);
        Assert.True(record.IsPinned);
        Assert.False(record.IsMissing);
        Assert.Equal([bundlePath], service.HistoryModel.PinnedPaths);

        // a marker without a record is stale and must not survive the load
        Assert.Empty(service.HistoryModel.CorruptPaths);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("""{"History":"not a list"}""")]
    public void Damaged_history_is_quarantined_and_starts_empty(string text)
    {
        WriteHistoryFile(text);

        var service = new HistoryService(HistoryPath);
        var report = service.TakeRecoveryReport();

        Assert.Equal(SettingsLoadOutcome.ResetAfterCorruption, report?.Outcome);
        Assert.NotNull(report?.QuarantinedPath);
        Assert.Empty(service.HistoryModel.History);

        var quarantined = QuarantinedFiles();
        Assert.Single(quarantined);
        Assert.Equal(text, File.ReadAllText(quarantined[0]));
    }

    [Fact]
    public void Damaged_history_restores_the_last_good_backup()
    {
        var bundlePath = Path.Combine(_directory, "restored.mpvm");
        File.WriteAllText(bundlePath, "{}");

        WriteBackup(HistoryBackupPath, $$"""
            {
              "LastDatabasePath": {{JsonSerializer.Serialize(bundlePath)}},
              "History": [{"Name":"Restored","Path":{{JsonSerializer.Serialize(bundlePath)}}}],
              "PinnedPaths": [{{JsonSerializer.Serialize(bundlePath)}}]
            }
            """);
        WriteHistoryFile("{ damaged");

        var service = new HistoryService(HistoryPath);

        Assert.Equal(SettingsLoadOutcome.RecoveredFromBackup, service.TakeRecoveryReport()?.Outcome);
        var record = Assert.Single(service.HistoryModel.History);
        Assert.Equal("Restored", record.Name);
        Assert.True(record.IsPinned);
        Assert.Equal(bundlePath, service.HistoryModel.LastDatabasePath);
    }
}
