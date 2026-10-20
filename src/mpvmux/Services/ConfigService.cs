using mpvmux.Constants;
using mpvmux.Models.DTO;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal interface IConfigService
{
    ConfigModelDTO Config { get; }
    bool IsDirty { get; }
    Task LoadAsync();
    void Save();
    Task UpdateConfig(ConfigModelDTO config);
}

internal sealed class ConfigService : IConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ConfigModelDTO Config { get; private set; } = new();
    public bool IsDirty { get; private set; }

    public ConfigService()
    {
        if (!File.Exists(AppConstants.ConfigFilePath))
        {
            WriteConfig();
            return;
        }

        LoadConfigSync();
    }

    public async Task LoadAsync()
    {
        await using var fs = File.OpenRead(AppConstants.ConfigFilePath);
        Config = await JsonSerializer.DeserializeAsync<ConfigModelDTO>(fs) ?? new ConfigModelDTO();
        IsDirty = false;
    }

    public async Task UpdateConfig(ConfigModelDTO config)
    {
        Config = config;
        IsDirty = true;
    }

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
            Config = JsonSerializer.Deserialize<ConfigModelDTO>(fs) ?? new ConfigModelDTO();
            IsDirty = false;
        }
        catch (JsonException)
        {
            // a corrupt config must not stop startup; rewrite defaults on the next save
            Config = new ConfigModelDTO();
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