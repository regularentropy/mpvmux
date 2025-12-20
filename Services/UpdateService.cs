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

internal class UpdateService(MetadataRepository sms) : IUpdateService
{
    public async Task<UpdateResponse?> CheckForUpdates()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(sms.Title);

        var response = await http.GetAsync(sms.APILink).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Request failed: {response.ReasonPhrase}",
                null,
                response.StatusCode);
        }

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var tag = doc.RootElement.GetProperty("tag_name").GetString()!;

        if (sms.Version >= new Version(tag))
        {
            return null;
        }
        
        var description = doc.RootElement.GetProperty("body").GetString()!;
        var url = doc.RootElement.GetProperty("html_url").GetString()!;

        return new UpdateResponse(tag, url, description);
    }
}
