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
    Task SaveAsync();
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
            SaveConfigSync();
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

    public async Task SaveAsync()
    {
        if (!IsDirty)
            return;

        await using (var fs = File.Create(AppConstants.ConfigFilePath))
            await JsonSerializer.SerializeAsync(fs, Config, JsonOptions);

        IsDirty = false;
    }

    private void LoadConfigSync()
    {
        using var fs = File.OpenRead(AppConstants.ConfigFilePath);
        Config = JsonSerializer.Deserialize<ConfigModelDTO>(fs) ?? new ConfigModelDTO();
        IsDirty = false;
    }

    private void SaveConfigSync()
    {
        using var fs = File.Create(AppConstants.ConfigFilePath);
        JsonSerializer.Serialize(fs, Config, JsonOptions);
        IsDirty = false;
    }
}