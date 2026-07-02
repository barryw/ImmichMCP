using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ImmichMCP.Client;
using ImmichMCP.Configuration;

namespace ImmichMCP.Tests.Integration;

internal sealed class IntegrationTestSettings
{
    private IntegrationTestSettings(string baseUrl, string apiKey, int? expectedMajorVersion)
    {
        BaseUrl = baseUrl;
        ApiKey = apiKey;
        ExpectedMajorVersion = expectedMajorVersion;
    }

    public string BaseUrl { get; }

    public string ApiKey { get; }

    public int? ExpectedMajorVersion { get; }

    public static IntegrationTestSettings Load()
    {
        var baseUrl = FirstNonEmpty(
            Environment.GetEnvironmentVariable("IMMICH_INTEGRATION_BASE_URL"),
            Environment.GetEnvironmentVariable("IMMICH_BASE_URL"));
        var apiKey = FirstNonEmpty(
            Environment.GetEnvironmentVariable("IMMICH_INTEGRATION_API_KEY"),
            Environment.GetEnvironmentVariable("IMMICH_API_KEY"));

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Set IMMICH_BASE_URL and IMMICH_API_KEY, or IMMICH_INTEGRATION_BASE_URL and IMMICH_INTEGRATION_API_KEY.");
        }

        var expectedMajorVersionText = Environment.GetEnvironmentVariable("IMMICH_INTEGRATION_EXPECTED_MAJOR") ?? "3";
        var expectedMajorVersion = int.TryParse(expectedMajorVersionText, out var major) ? major : (int?)null;

        return new IntegrationTestSettings(baseUrl, apiKey, expectedMajorVersion);
    }

    public ImmichClient CreateClient()
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(120)
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        httpClient.DefaultRequestHeaders.Add("x-api-key", ApiKey);

        var options = Options.Create(new ImmichOptions
        {
            BaseUrl = BaseUrl,
            ApiKey = ApiKey,
            MaxPageSize = 100,
            DownloadMode = "url"
        });

        var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ImmichClient>();
        return new ImmichClient(httpClient, options, logger);
    }

    public static bool IsEnabled(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}

public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (!IntegrationTestSettings.IsEnabled("IMMICH_INTEGRATION_TESTS"))
        {
            Skip = "Set IMMICH_INTEGRATION_TESTS=true to run Immich integration tests.";
        }
    }
}

public sealed class MutationIntegrationFactAttribute : FactAttribute
{
    public MutationIntegrationFactAttribute()
    {
        if (!IntegrationTestSettings.IsEnabled("IMMICH_INTEGRATION_TESTS"))
        {
            Skip = "Set IMMICH_INTEGRATION_TESTS=true to run Immich integration tests.";
        }
        else if (!IntegrationTestSettings.IsEnabled("IMMICH_INTEGRATION_MUTATION_TESTS"))
        {
            Skip = "Set IMMICH_INTEGRATION_MUTATION_TESTS=true to run mutation integration tests.";
        }
    }
}
