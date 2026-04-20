// Placeholder HTTP client services
using CHMBAPI.Data;
using CHMBAPI.Entities;

namespace CHMBAPI.Services;

public class LineLoginService
{
    private readonly HttpClient _httpClient;

    public LineLoginService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    // Implementation for LINE Login API calls
}

public class LineWebhookService
{
    private readonly HttpClient _httpClient;

    public LineWebhookService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    // Implementation for LINE Webhook handling
}