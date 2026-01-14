using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ImmichMCP.Client;
using ImmichMCP.Models.Common;

namespace ImmichMCP.Tools;

/// <summary>
/// MCP tools for health checks and capability discovery.
/// </summary>
[McpServerToolType]
public static class HealthTools
{
    [McpServerTool(Name = "immich.ping")]
    [Description("Verify connectivity and authentication with the Immich instance. Returns server version if available.")]
    public static async Task<string> Ping(ImmichClient client)
    {
        var (success, info, error) = await client.PingAsync().ConfigureAwait(false);

        if (success && info != null)
        {
            var response = McpResponse<object>.Success(
                new
                {
                    connected = true,
                    version = info.Version,
                    build = info.Build,
                    nodejs = info.Nodejs,
                    ffmpeg = info.Ffmpeg,
                    exiftool = info.Exiftool
                },
                new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(response);
        }

        var errorResponse = McpErrorResponse.Create(
            ErrorCodes.UpstreamError,
            error ?? "Failed to connect to Immich instance",
            meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(errorResponse);
    }

    [McpServerTool(Name = "immich.capabilities")]
    [Description("Return supported API features and detected Immich server capabilities.")]
    public static async Task<string> GetCapabilities(ImmichClient client)
    {
        var (pingSuccess, serverInfo, _) = await client.PingAsync().ConfigureAwait(false);
        var (featuresSuccess, features, _) = await client.GetFeaturesAsync().ConfigureAwait(false);

        var capabilities = new
        {
            connected = pingSuccess,
            version = serverInfo?.Version,
            features = featuresSuccess ? new
            {
                features!.Trash,
                features.Map,
                features.ReverseGeocoding,
                features.Import,
                features.Sidecar,
                features.Search,
                features.FacialRecognition,
                features.Oauth,
                features.PasswordLogin,
                features.ConfigFile,
                features.DuplicateDetection,
                features.Email,
                features.SmartSearch
            } : null,
            endpoints = new
            {
                assets = new
                {
                    list = "/api/assets",
                    get = "/api/assets/{id}",
                    upload = "/api/assets",
                    update = "/api/assets/{id}",
                    bulk_update = "/api/assets",
                    delete = "/api/assets",
                    statistics = "/api/assets/statistics",
                    original = "/api/assets/{id}/original",
                    thumbnail = "/api/assets/{id}/thumbnail"
                },
                search = new
                {
                    metadata = "/api/search/metadata",
                    smart = "/api/search/smart",
                    explore = "/api/search/explore"
                },
                albums = new
                {
                    list = "/api/albums",
                    get = "/api/albums/{id}",
                    create = "/api/albums",
                    update = "/api/albums/{id}",
                    delete = "/api/albums/{id}",
                    add_assets = "/api/albums/{id}/assets",
                    remove_assets = "/api/albums/{id}/assets",
                    statistics = "/api/albums/statistics"
                },
                people = new
                {
                    list = "/api/people",
                    get = "/api/people/{id}",
                    update = "/api/people/{id}",
                    merge = "/api/people/{id}/merge",
                    assets = "/api/people/{id}/assets"
                },
                tags = new
                {
                    list = "/api/tags",
                    get = "/api/tags/{id}",
                    create = "/api/tags",
                    update = "/api/tags/{id}",
                    delete = "/api/tags/{id}",
                    tag_assets = "/api/tags/{id}/assets",
                    untag_assets = "/api/tags/{id}/assets"
                },
                shared_links = new
                {
                    list = "/api/shared-links",
                    get = "/api/shared-links/{id}",
                    create = "/api/shared-links",
                    update = "/api/shared-links/{id}",
                    delete = "/api/shared-links/{id}"
                },
                activities = new
                {
                    list = "/api/activities",
                    create = "/api/activities",
                    delete = "/api/activities/{id}",
                    statistics = "/api/activities/statistics"
                }
            }
        };

        var response = McpResponse<object>.Success(
            capabilities,
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }
}
