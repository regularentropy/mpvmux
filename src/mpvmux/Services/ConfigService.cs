using mpvmux.Constants;
using mpvmux.Models;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace mpvmux.Services;

internal interface IConfigService
{
    ConfigModel Config { get; }
    bool IsDirty { get; }
    void Save();
}

internal sealed class ConfigService : IConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ConfigModel Config { get; private set; } = new();
    public bool IsDirty { get; private set; }

    public ConfigService()
    {
        if (File.Exists(AppConstants.ConfigFilePath))
            LoadConfigSync();

        Config.PropertyChanged += OnConfigChanged;

        if (!File.Exists(AppConstants.ConfigFilePath))
            WriteConfig();
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e) => IsDirty = true;

    public void Save()
    {
        if (!IsDirty)
            return;

        WriteConfig();
    }

    private void LoadConfigSync()
    {
        try
        {
            using var fs = File.OpenRead(AppConstants.ConfigFilePath);
            Config = JsonSerializer.Deserialize<ConfigModel>(fs) ?? new ConfigModel();
            Config.ShortcutOverrides ??= new();
            Config.GlobalShortcutActions ??= new();

            // configs written before per-shortcut scope had one switch that made every
            // shortcut global; keep that behaviour for existing users
            var migratedGlobals = Config.EnableGlobalShortcuts;
            if (migratedGlobals)
            {
                Config.GlobalShortcutActions = ShortcutActions.All.Select(action => action.Id).ToList();
                Config.EnableGlobalShortcuts = false;
            }

            IsDirty = migratedGlobals;
        }
        catch (JsonException)
        {
            // a corrupt config must not stop startup; rewrite defaults on the next save
            Config = new ConfigModel();
            IsDirty = true;
        }
    }

    private void WriteConfig()
    {
        var tmpPath = AppConstants.ConfigFilePath + ".tmp";
        using (var fs = File.Create(tmpPath))
            JsonSerializer.Serialize(fs, Config, JsonOptions);

        File.Move(tmpPath, AppConstants.ConfigFilePath, overwrite: true);
        IsDirty = false;
    }
}
