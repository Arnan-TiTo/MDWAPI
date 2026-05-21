using System.Text.Json;

namespace MDWAPI.Helpers;

public static class HttpJson
{
    public static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static Task<HttpResponseMessage> PostJsonAsync<T>(
        this HttpClient http, string url, T body, CancellationToken ct = default)
        => http.PostAsJsonAsync(url, body, DefaultJsonOptions, ct);

    public static async Task<T?> ReadJsonAsync<T>(this HttpContent content, CancellationToken ct = default)
        => await content.ReadFromJsonAsync<T>(DefaultJsonOptions, ct);
}
