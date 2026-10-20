using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace mpvmux.Services;

internal record UpdateResponse(string Tag, string Url, string ReleaseNotes);

internal interface IUpdateService
{
    Task<UpdateResponse?> CheckForUpdates();
}

internal class UpdateService : IUpdateService
{
    private readonly MetadataRepository _metadata;
    private readonly HttpClient _http;

    public UpdateService(MetadataRepository metadata, HttpClient http)
    {
        _metadata = metadata;
        _http = http;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(metadata.Title);
    }

    public async Task<UpdateResponse?> CheckForUpdates()
    {
        var response = await _http.GetAsync(_metadata.APILink).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Request failed: {response.ReasonPhrase}",
                null,
                response.StatusCode);
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var tag = doc.RootElement.GetProperty("tag_name").GetString()!.TrimStart('v', 'V');

        if (_metadata.Version >= new Version(tag))
        {
            return null;
        }
        
        var description = doc.RootElement.GetProperty("body").GetString()!;
        var url = doc.RootElement.GetProperty("html_url").GetString()!;

        return new UpdateResponse(tag, url, description);
    }
}
