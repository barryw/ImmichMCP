using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ImmichMCP.Client;
using ImmichMCP.Models.Common;
using ImmichMCP.Models.Assets;
using ImmichMCP.Models.Search;
using static ImmichMCP.Utils.ParsingHelpers;

namespace ImmichMCP.Tools;

/// <summary>
/// MCP tools for search operations.
/// </summary>
[McpServerToolType]
public static class SearchTools
{
    [McpServerTool(Name = "immich.search.metadata")]
    [Description("Search assets by metadata filters (dates, type, location, camera, person, etc.).")]
    public static async Task<string> MetadataSearch(
        ImmichClient client,
        [Description("Page number (default: 1)")] int page = 1,
        [Description("Page size (default: 25, max: 100)")] int size = 25,
        [Description("Asset type: IMAGE, VIDEO, or ALL")] string? type = null,
        [Description("Filter by favorite status")] bool? isFavorite = null,
        [Description("Filter by archived status")] bool? isArchived = null,
        [Description("Filter by trashed status")] bool? isTrashed = null,
        [Description("Filter assets taken after this date (YYYY-MM-DD)")] string? takenAfter = null,
        [Description("Filter assets taken before this date (YYYY-MM-DD)")] string? takenBefore = null,
        [Description("Filter by city")] string? city = null,
        [Description("Filter by state/province")] string? state = null,
        [Description("Filter by country")] string? country = null,
        [Description("Filter by camera make")] string? make = null,
        [Description("Filter by camera model")] string? model = null,
        [Description("Filter by lens model")] string? lensModel = null,
        [Description("Filter by person IDs (comma-separated UUIDs)")] string? personIds = null,
        [Description("Filter by original file name (partial match)")] string? originalFileName = null,
        [Description("Sort order: asc or desc")] string? order = null)
    {
        var request = new MetadataSearchRequest
        {
            Page = page,
            Size = Math.Min(size, 100),
            Type = type?.ToUpperInvariant(),
            IsFavorite = isFavorite,
            IsArchived = isArchived,
            IsTrashed = isTrashed,
            TakenAfter = ParseDate(takenAfter),
            TakenBefore = ParseDate(takenBefore),
            City = city,
            State = state,
            Country = country,
            Make = make,
            Model = model,
            LensModel = lensModel,
            PersonIds = ParseStringArray(personIds),
            OriginalFileName = originalFileName,
            Order = order
        };

        var result = await client.SearchMetadataAsync(request).ConfigureAwait(false);

        var summaries = result.Items.Select(AssetSummary.FromAsset).ToList();

        var response = McpResponse<object>.Success(
            summaries,
            new McpMeta
            {
                Page = page,
                PageSize = size,
                Total = result.Total,
                Next = result.NextPage,
                ImmichBaseUrl = client.BaseUrl
            }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.search.smart")]
    [Description("ML-based semantic search using CLIP. Search using natural language queries like 'sunset at the beach' or 'birthday cake'.")]
    public static async Task<string> SmartSearch(
        ImmichClient client,
        [Description("Natural language search query")] string query,
        [Description("Page number (default: 1)")] int page = 1,
        [Description("Page size (default: 25, max: 100)")] int size = 25,
        [Description("Asset type: IMAGE, VIDEO, or ALL")] string? type = null,
        [Description("Filter by favorite status")] bool? isFavorite = null,
        [Description("Filter by archived status")] bool? isArchived = null,
        [Description("Filter assets taken after this date (YYYY-MM-DD)")] string? takenAfter = null,
        [Description("Filter assets taken before this date (YYYY-MM-DD)")] string? takenBefore = null,
        [Description("Filter by city")] string? city = null,
        [Description("Filter by state/province")] string? state = null,
        [Description("Filter by country")] string? country = null,
        [Description("Filter by camera make")] string? make = null,
        [Description("Filter by camera model")] string? model = null,
        [Description("Filter by person IDs (comma-separated UUIDs)")] string? personIds = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "Search query is required",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var request = new SmartSearchRequest
        {
            Query = query,
            Page = page,
            Size = Math.Min(size, 100),
            Type = type?.ToUpperInvariant(),
            IsFavorite = isFavorite,
            IsArchived = isArchived,
            TakenAfter = ParseDate(takenAfter),
            TakenBefore = ParseDate(takenBefore),
            City = city,
            State = state,
            Country = country,
            Make = make,
            Model = model,
            PersonIds = ParseStringArray(personIds)
        };

        var result = await client.SearchSmartAsync(request).ConfigureAwait(false);

        var summaries = result.Items.Select(AssetSummary.FromAsset).ToList();

        var response = McpResponse<object>.Success(
            summaries,
            new McpMeta
            {
                Page = page,
                PageSize = size,
                Total = result.Total,
                Next = result.NextPage,
                ImmichBaseUrl = client.BaseUrl
            }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.search.explore")]
    [Description("Get explore/discovery data showing popular places, things, and people from your library.")]
    public static async Task<string> Explore(ImmichClient client)
    {
        var exploreData = await client.SearchExploreAsync().ConfigureAwait(false);

        if (exploreData == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to retrieve explore data",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<List<ExploreData>>.Success(
            exploreData,
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }
}
