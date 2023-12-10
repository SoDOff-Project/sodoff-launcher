using System.Net;
namespace dragonrescue.Util;

public static class HttpClientExtensions {
    public static async Task<string> PostAndGetReplayOrThrow(this HttpClient client, string url, HttpContent content) {
        var response = await client.PostAsync(url, content);
        
        if (! response.IsSuccessStatusCode) {
            throw new HttpRequestException("HTTP status code " + response.StatusCode);
        }
        
        return await response.Content.ReadAsStringAsync();
    }
}
