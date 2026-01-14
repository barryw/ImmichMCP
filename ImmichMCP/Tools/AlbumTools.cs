using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ImmichMCP.Client;
using ImmichMCP.Models.Common;
using ImmichMCP.Models.Albums;
using static ImmichMCP.Utils.ParsingHelpers;

namespace ImmichMCP.Tools;

/// <summary>
/// MCP tools for album operations.
/// </summary>
[McpServerToolType]
public static class AlbumTools
{
    [McpServerTool(Name = "immich.albums.list")]
    [Description("List all albums with optional filters.")]
    public static async Task<string> List(
        ImmichClient client,
        [Description("Filter to shared albums only")] bool? shared = null,
        [Description("Filter to albums containing this asset ID")] string? assetId = null)
    {
        var albums = await client.GetAlbumsAsync(shared, assetId).ConfigureAwait(false);

        var summaries = albums.Select(AlbumSummary.FromAlbum).ToList();

        var response = McpResponse<object>.Success(
            summaries,
            new McpMeta
            {
                Total = summaries.Count,
                ImmichBaseUrl = client.BaseUrl
            }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.albums.get")]
    [Description("Get album details by ID.")]
    public static async Task<string> Get(
        ImmichClient client,
        [Description("Album ID (UUID)")] string id,
        [Description("Exclude assets from response (for faster loading)")] bool withoutAssets = false)
    {
        var album = await client.GetAlbumAsync(id, withoutAssets).ConfigureAwait(false);

        if (album == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Album with ID {id} not found",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Album>.Success(
            album,
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.albums.create")]
    [Description("Create a new album.")]
    public static async Task<string> Create(
        ImmichClient client,
        [Description("Album name")] string albumName,
        [Description("Album description")] string? description = null,
        [Description("Initial asset IDs to add (comma-separated UUIDs)")] string? assetIds = null,
        [Description("User IDs to share with (comma-separated UUIDs)")] string? sharedWithUserIds = null)
    {
        if (string.IsNullOrWhiteSpace(albumName))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "Album name is required",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var request = new AlbumCreateRequest
        {
            AlbumName = albumName,
            Description = description,
            AssetIds = ParseStringArray(assetIds),
            SharedWithUserIds = ParseStringArray(sharedWithUserIds)
        };

        var album = await client.CreateAlbumAsync(request).ConfigureAwait(false);

        if (album == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to create album",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Album>.Success(
            album,
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.albums.update")]
    [Description("Update album metadata (name, description).")]
    public static async Task<string> Update(
        ImmichClient client,
        [Description("Album ID (UUID)")] string id,
        [Description("New album name")] string? albumName = null,
        [Description("New description")] string? description = null,
        [Description("Enable/disable activity (comments/likes)")] bool? isActivityEnabled = null,
        [Description("Sort order: asc or desc")] string? order = null)
    {
        var request = new AlbumUpdateRequest
        {
            AlbumName = albumName,
            Description = description,
            IsActivityEnabled = isActivityEnabled,
            Order = order
        };

        var album = await client.UpdateAlbumAsync(id, request).ConfigureAwait(false);

        if (album == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Album with ID {id} not found or update failed",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Album>.Success(
            album,
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.albums.assets.add")]
    [Description("Add assets to an album.")]
    public static async Task<string> AddAssets(
        ImmichClient client,
        [Description("Album ID (UUID)")] string albumId,
        [Description("Asset IDs to add (comma-separated UUIDs)")] string assetIds)
    {
        var ids = ParseStringArray(assetIds);

        if (ids == null || ids.Length == 0)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "No valid asset IDs provided",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var result = await client.AddAssetsToAlbumAsync(albumId, ids).ConfigureAwait(false);

        if (result == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to add assets to album",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var successCount = result.Count(r => r.Success);
        var failedCount = result.Count(r => !r.Success);

        var response = McpResponse<object>.Success(
            new
            {
                album_id = albumId,
                added = successCount,
                failed = failedCount,
                results = result
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.albums.assets.remove")]
    [Description("Remove assets from an album.")]
    public static async Task<string> RemoveAssets(
        ImmichClient client,
        [Description("Album ID (UUID)")] string albumId,
        [Description("Asset IDs to remove (comma-separated UUIDs)")] string assetIds)
    {
        var ids = ParseStringArray(assetIds);

        if (ids == null || ids.Length == 0)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "No valid asset IDs provided",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var result = await client.RemoveAssetsFromAlbumAsync(albumId, ids).ConfigureAwait(false);

        if (result == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to remove assets from album",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var successCount = result.Count(r => r.Success);
        var failedCount = result.Count(r => !r.Success);

        var response = McpResponse<object>.Success(
            new
            {
                album_id = albumId,
                removed = successCount,
                failed = failedCount,
                results = result
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.albums.delete")]
    [Description("Delete an album. Requires explicit confirmation.")]
    public static async Task<string> Delete(
        ImmichClient client,
        [Description("Album ID (UUID)")] string id,
        [Description("Must be true to confirm deletion")] bool confirm = false)
    {
        if (!confirm)
        {
            var album = await client.GetAlbumAsync(id, withoutAssets: true).ConfigureAwait(false);

            if (album == null)
            {
                var notFoundResponse = McpErrorResponse.Create(
                    ErrorCodes.NotFound,
                    $"Album with ID {id} not found",
                    meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
                );
                return JsonSerializer.Serialize(notFoundResponse);
            }

            var dryRunResponse = McpErrorResponse.Create(
                ErrorCodes.ConfirmationRequired,
                "Deletion requires confirm=true. This is a dry run showing what would be deleted.",
                new
                {
                    album_id = id,
                    album_name = album.AlbumName,
                    asset_count = album.AssetCount,
                    shared = album.Shared
                },
                new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(dryRunResponse);
        }

        var success = await client.DeleteAlbumAsync(id).ConfigureAwait(false);

        if (!success)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                $"Failed to delete album with ID {id}",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new { deleted = true, album_id = id },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.albums.statistics")]
    [Description("Get album statistics (owned, shared, not shared counts).")]
    public static async Task<string> Statistics(ImmichClient client)
    {
        var stats = await client.GetAlbumStatisticsAsync().ConfigureAwait(false);

        if (stats == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to retrieve album statistics",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<AlbumStatistics>.Success(
            stats,
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }
}
