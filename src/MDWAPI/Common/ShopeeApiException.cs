namespace MDWAPI.Common;

public class ShopeeApiException(string message, System.Net.HttpStatusCode? status, string requestUrl)
    : System.Net.Http.HttpRequestException(message, null, status)
{
    public string RequestUrl { get; } = requestUrl;
}
