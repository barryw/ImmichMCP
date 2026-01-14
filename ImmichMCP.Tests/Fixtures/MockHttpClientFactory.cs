using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;
using ImmichMCP.Client;
using ImmichMCP.Configuration;

namespace ImmichMCP.Tests.Fixtures;

public static class MockHttpClientFactory
{
    public static (ImmichClient Client, MockHttpMessageHandler Handler) CreateMockClient(
        string baseUrl = "https://photos.example.com",
        string apiKey = "test-api-key")
    {
        var mockHandler = new MockHttpMessageHandler();
        var httpClient = mockHandler.ToHttpClient();
        httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        var options = Options.Create(new ImmichOptions
        {
            BaseUrl = baseUrl,
            ApiKey = apiKey,
            MaxPageSize = 100,
            DownloadMode = "url"
        });

        var logger = new LoggerFactory().CreateLogger<ImmichClient>();

        var client = new ImmichClient(httpClient, options, logger);

        return (client, mockHandler);
    }
}
