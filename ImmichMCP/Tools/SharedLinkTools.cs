using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ImmichMCP.Client;
using ImmichMCP.Models.Common;
using ImmichMCP.Models.SharedLinks;
using static ImmichMCP.Utils.ParsingHelpers;

namespace ImmichMCP.Tools;

/// <summary>
/// MCP tools for shared link operations.
/// </summary>
[McpServerToolType]
public static class SharedLinkTools
{
    [McpServerTool(Name = "immich.shared_links.list")]
    [Description("List all shared links.")]
    public static async Task<string> List(ImmichClient client)
    {
        var links = await client.GetSharedLinksAsync().ConfigureAwait(false);

        var summaries = links.Select(l => SharedLinkSummary.FromSharedLink(l, client.BaseUrl)).ToList();

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

    [McpServerTool(Name = "immich.shared_links.get")]
    [Description("Get shared link by ID.")]
    public static async Task<string> Get(
        ImmichClient client,
        [Description("Shared link ID (UUID)")] string id)
    {
        var link = await client.GetSharedLinkAsync(id).ConfigureAwait(false);

        if (link == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Shared link with ID {id} not found",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var baseUrl = client.BaseUrl.TrimEnd('/');
        var shareUrl = $"{baseUrl}/share/{link.Key}";

        var response = McpResponse<object>.Success(
            new
            {
                id = link.Id,
                key = link.Key,
                share_url = shareUrl,
                type = link.Type,
                created_at = link.CreatedAt,
                expires_at = link.ExpiresAt,
                allow_upload = link.AllowUpload,
                allow_download = link.AllowDownload,
                show_metadata = link.ShowMetadata,
                description = link.Description,
                album_name = link.Album?.AlbumName,
                asset_count = link.Assets?.Count ?? link.Album?.AssetCount ?? 0
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.shared_links.create")]
    [Description("Create a new shared link for an album or assets.")]
    public static async Task<string> Create(
        ImmichClient client,
        [Description("Link type: ALBUM or INDIVIDUAL")] string type = "INDIVIDUAL",
        [Description("Album ID (required if type is ALBUM)")] string? albumId = null,
        [Description("Asset IDs (comma-separated, required if type is INDIVIDUAL)")] string? assetIds = null,
        [Description("Expiration date (ISO format, optional)")] string? expiresAt = null,
        [Description("Allow upload (default: false)")] bool? allowUpload = null,
        [Description("Allow download (default: true)")] bool? allowDownload = null,
        [Description("Show metadata (default: true)")] bool? showMetadata = null,
        [Description("Password protection (optional)")] string? password = null,
        [Description("Description (optional)")] string? description = null)
    {
        var upperType = type.ToUpperInvariant();

        if (upperType == "ALBUM" && string.IsNullOrEmpty(albumId))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "Album ID is required for ALBUM type shared links",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        if (upperType == "INDIVIDUAL" && string.IsNullOrEmpty(assetIds))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "Asset IDs are required for INDIVIDUAL type shared links",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var request = new SharedLinkCreateRequest
        {
            Type = upperType,
            AlbumId = albumId,
            AssetIds = ParseStringArray(assetIds),
            ExpiresAt = ParseDate(expiresAt),
            AllowUpload = allowUpload,
            AllowDownload = allowDownload,
            ShowMetadata = showMetadata,
            Password = password,
            Description = description
        };

        var link = await client.CreateSharedLinkAsync(request).ConfigureAwait(false);

        if (link == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to create shared link",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var baseUrl = client.BaseUrl.TrimEnd('/');
        var shareUrl = $"{baseUrl}/share/{link.Key}";

        var response = McpResponse<object>.Success(
            new
            {
                id = link.Id,
                key = link.Key,
                share_url = shareUrl,
                type = link.Type,
                created_at = link.CreatedAt,
                expires_at = link.ExpiresAt,
                allow_upload = link.AllowUpload,
                allow_download = link.AllowDownload,
                show_metadata = link.ShowMetadata
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.shared_links.update")]
    [Description("Update shared link settings (expiry, permissions, etc.).")]
    public static async Task<string> Update(
        ImmichClient client,
        [Description("Shared link ID (UUID)")] string id,
        [Description("New expiration date (ISO format)")] string? expiresAt = null,
        [Description("Allow upload")] bool? allowUpload = null,
        [Description("Allow download")] bool? allowDownload = null,
        [Description("Show metadata")] bool? showMetadata = null,
        [Description("Password (set empty string to remove)")] string? password = null,
        [Description("Description")] string? description = null,
        [Description("Set to true to update expiry time")] bool? changeExpiryTime = null)
    {
        var request = new SharedLinkUpdateRequest
        {
            ExpiresAt = ParseDate(expiresAt),
            AllowUpload = allowUpload,
            AllowDownload = allowDownload,
            ShowMetadata = showMetadata,
            Password = password,
            Description = description,
            ChangeExpiryTime = changeExpiryTime
        };

        var link = await client.UpdateSharedLinkAsync(id, request).ConfigureAwait(false);

        if (link == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Shared link with ID {id} not found or update failed",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var baseUrl = client.BaseUrl.TrimEnd('/');
        var shareUrl = $"{baseUrl}/share/{link.Key}";

        var response = McpResponse<object>.Success(
            new
            {
                id = link.Id,
                key = link.Key,
                share_url = shareUrl,
                type = link.Type,
                expires_at = link.ExpiresAt,
                allow_upload = link.AllowUpload,
                allow_download = link.AllowDownload,
                show_metadata = link.ShowMetadata
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.shared_links.delete")]
    [Description("Delete a shared link. Requires explicit confirmation.")]
    public static async Task<string> Delete(
        ImmichClient client,
        [Description("Shared link ID (UUID)")] string id,
        [Description("Must be true to confirm deletion")] bool confirm = false)
    {
        if (!confirm)
        {
            var link = await client.GetSharedLinkAsync(id).ConfigureAwait(false);

            if (link == null)
            {
                var notFoundResponse = McpErrorResponse.Create(
                    ErrorCodes.NotFound,
                    $"Shared link with ID {id} not found",
                    meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
                );
                return JsonSerializer.Serialize(notFoundResponse);
            }

            var dryRunResponse = McpErrorResponse.Create(
                ErrorCodes.ConfirmationRequired,
                "Deletion requires confirm=true. This is a dry run showing what would be deleted.",
                new
                {
                    link_id = id,
                    key = link.Key,
                    type = link.Type,
                    album_name = link.Album?.AlbumName,
                    asset_count = link.Assets?.Count ?? link.Album?.AssetCount ?? 0
                },
                new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(dryRunResponse);
        }

        var success = await client.DeleteSharedLinkAsync(id).ConfigureAwait(false);

        if (!success)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                $"Failed to delete shared link with ID {id}",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new { deleted = true, link_id = id },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }
}
