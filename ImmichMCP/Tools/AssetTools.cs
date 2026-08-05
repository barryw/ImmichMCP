using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ImmichMCP.Client;
using ImmichMCP.Models.Common;
using ImmichMCP.Models.Assets;
using ImmichMCP.Models.Albums;
using ImmichMCP.Models.SharedLinks;
using static ImmichMCP.Utils.ParsingHelpers;

namespace ImmichMCP.Tools;

/// <summary>
/// MCP tools for asset operations.
/// </summary>
[McpServerToolType]
public static class AssetTools
{
    [McpServerTool(Name = "immich_assets_upload_authorize")]
    [Description(
        "Authorize a client-side bulk upload WITHOUT exposing the API key. Creates (or reuses) an album " +
        "and returns a short-lived, upload-only shared-link URL. The client then POSTs files DIRECTLY to " +
        "upload_url (multipart/form-data, field 'assetData', plus fileCreatedAt and fileModifiedAt as ISO-8601 " +
        "datetimes WITH a 'Z' suffix) — no API key, no local install beyond curl. Every uploaded asset lands in " +
        "the album. Re-running is safe and resumable: Immich deduplicates by file checksum, so already-uploaded " +
        "files return status 'duplicate' and are not re-added.")]
    public static async Task<string> AuthorizeUpload(
        ImmichClient client,
        [Description("Name of a NEW album to create for these uploads. Provide this OR album_id, not both.")] string? albumName = null,
        [Description("Existing album ID (UUID) to upload into. Provide this OR album_name, not both.")] string? albumId = null,
        [Description("Minutes until the upload URL expires (default 60, clamped to 1..1440).")] int ttlMinutes = 60)
    {
        var hasName = !string.IsNullOrWhiteSpace(albumName);
        var hasId = !string.IsNullOrWhiteSpace(albumId);
        if (hasName == hasId)
        {
            return JsonSerializer.Serialize(McpErrorResponse.Create(
                ErrorCodes.Validation,
                "Provide exactly one of album_name (to create a new album) or album_id (to use an existing one).",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }));
        }

        string resolvedAlbumId;
        string resolvedAlbumName;
        if (hasName)
        {
            var album = await client.CreateAlbumAsync(new AlbumCreateRequest { AlbumName = albumName! }).ConfigureAwait(false);
            if (album == null)
            {
                return JsonSerializer.Serialize(McpErrorResponse.Create(
                    ErrorCodes.UpstreamError,
                    "Failed to create album for upload.",
                    meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }));
            }
            resolvedAlbumId = album.Id;
            resolvedAlbumName = album.AlbumName;
        }
        else
        {
            var album = await client.GetAlbumAsync(albumId!).ConfigureAwait(false);
            if (album == null)
            {
                return JsonSerializer.Serialize(McpErrorResponse.Create(
                    ErrorCodes.NotFound,
                    $"Album with ID {albumId} not found.",
                    meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }));
            }
            resolvedAlbumId = album.Id;
            resolvedAlbumName = album.AlbumName;
        }

        var ttl = Math.Clamp(ttlMinutes, 1, 1440);
        var expiresAt = DateTime.UtcNow.AddMinutes(ttl);

        var link = await client.CreateSharedLinkAsync(new SharedLinkCreateRequest
        {
            Type = "ALBUM",
            AlbumId = resolvedAlbumId,
            AllowUpload = true,
            AllowDownload = false,
            ExpiresAt = expiresAt
        }).ConfigureAwait(false);

        if (link == null || string.IsNullOrEmpty(link.Key))
        {
            return JsonSerializer.Serialize(McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to create upload authorization link.",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }));
        }

        var baseUrl = client.BaseUrl.TrimEnd('/');
        var uploadUrl = $"{baseUrl}/api/assets?key={link.Key}";

        var response = McpResponse<object>.Success(
            new
            {
                upload_url = uploadUrl,
                album_id = resolvedAlbumId,
                album_name = resolvedAlbumName,
                shared_link_id = link.Id,
                expires_at = expiresAt.ToString("O"),
                required_fields = new[] { "assetData", "fileCreatedAt", "fileModifiedAt" },
                notes = "POST each file to upload_url with multipart/form-data. No API key needed. " +
                        "Timestamps must be ISO-8601 with a 'Z'. Re-running skips existing files (status 'duplicate').",
                curl_example =
                    "TS=$(date -u +%Y-%m-%dT%H:%M:%S.000Z); " +
                    $"for f in /path/to/dir/*; do curl --retry 3 -sf -X POST \"{uploadUrl}\" " +
                    "-F \"assetData=@$f\" -F \"deviceId=mcp-client\" -F \"deviceAssetId=$f\" " +
                    "-F \"fileCreatedAt=$TS\" -F \"fileModifiedAt=$TS\"; done"
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl });
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich_assets_list")]
    [Description("List recent assets with optional filters and pagination.")]
    public static async Task<string> List(
        ImmichClient client,
        [Description("Number of assets to return (default: 25, max: 1000)")] int size = 25,
        [Description("Filter by favorite status")] bool? isFavorite = null,
        [Description("Filter by archived status")] bool? isArchived = null,
        [Description("Filter by trashed status")] bool? isTrashed = null,
        [Description("Filter by assets updated after this date (ISO format)")] string? updatedAfter = null,
        [Description("Filter by assets updated before this date (ISO format)")] string? updatedBefore = null)
    {
        var assets = await client.GetAssetsAsync(
            size: ClampPageSize(size, 1000),
            isFavorite: isFavorite,
            isArchived: isArchived,
            isTrashed: isTrashed,
            updatedAfter: ParseDate(updatedAfter),
            updatedBefore: ParseDate(updatedBefore)
        ).ConfigureAwait(false);

        var summaries = assets.Select(AssetSummary.FromAsset).ToList();

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

    [McpServerTool(Name = "immich_assets_get")]
    [Description("Get full asset metadata by ID.")]
    public static async Task<string> Get(
        ImmichClient client,
        [Description("Asset ID (UUID)")] string id)
    {
        var asset = await client.GetAssetAsync(id).ConfigureAwait(false);

        if (asset == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Asset with ID {id} not found",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Asset>.Success(
            asset,
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich_assets_exif")]
    [Description("Get EXIF metadata for an asset.")]
    public static async Task<string> GetExif(
        ImmichClient client,
        [Description("Asset ID (UUID)")] string id)
    {
        var asset = await client.GetAssetAsync(id).ConfigureAwait(false);

        if (asset == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Asset with ID {id} not found",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        if (asset.ExifInfo == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"No EXIF data available for asset {id}",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<ExifInfo>.Success(
            asset.ExifInfo,
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich_assets_download_original")]
    [Description("Get the original asset file. Returns a download URL, or the file content inline when DOWNLOAD_MODE=base64.")]
    public static async Task<CallToolResult> DownloadOriginal(
        ImmichClient client,
        [Description("Asset ID (UUID)")] string id,
        CancellationToken cancellationToken = default)
    {
        var asset = await client.GetAssetAsync(id, cancellationToken).ConfigureAwait(false);

        if (asset == null)
        {
            return AssetNotFoundResult(id, client);
        }

        var downloadInfo = client.GetAssetDownloadInfo(id, asset.OriginalFileName);

        if (!IsBase64Mode(client))
        {
            var urlResponse = McpResponse<object>.Success(
                new
                {
                    id,
                    original_file_name = asset.OriginalFileName,
                    original_url = downloadInfo.OriginalUrl,
                    mime_type = asset.OriginalMimeType,
                    file_size = asset.ExifInfo?.FileSizeInByte
                },
                new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return TextResult(JsonSerializer.Serialize(urlResponse));
        }

        byte[] bytes;
        string mimeType;
        try
        {
            (bytes, mimeType) = await client.DownloadAssetOriginalAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (InlineDownloadTooLargeException ex)
        {
            return TooLargeResult(id, ex, downloadInfo.OriginalUrl, client);
        }

        var response = McpResponse<object>.Success(
            new
            {
                id,
                original_file_name = asset.OriginalFileName,
                mime_type = mimeType,
                file_size = bytes.Length,
                encoding = "base64"
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return BinaryResult(JsonSerializer.Serialize(response), bytes, mimeType, downloadInfo.OriginalUrl);
    }

    [McpServerTool(Name = "immich_assets_download_thumbnail")]
    [Description("Get thumbnail and preview URLs for an asset. When DOWNLOAD_MODE=base64, returns the preview image content inline instead.")]
    public static async Task<CallToolResult> DownloadThumbnail(
        ImmichClient client,
        [Description("Asset ID (UUID)")] string id,
        CancellationToken cancellationToken = default)
    {
        var asset = await client.GetAssetAsync(id, cancellationToken).ConfigureAwait(false);

        if (asset == null)
        {
            return AssetNotFoundResult(id, client);
        }

        var downloadInfo = client.GetAssetDownloadInfo(id, asset.OriginalFileName);

        if (!IsBase64Mode(client))
        {
            var urlResponse = McpResponse<object>.Success(
                new
                {
                    id,
                    original_file_name = asset.OriginalFileName,
                    thumbnail_url = downloadInfo.ThumbnailUrl,
                    preview_url = downloadInfo.PreviewUrl,
                    thumbhash = asset.Thumbhash
                },
                new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return TextResult(JsonSerializer.Serialize(urlResponse));
        }

        byte[] bytes;
        string mimeType;
        try
        {
            (bytes, mimeType) = await client.DownloadAssetThumbnailAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (InlineDownloadTooLargeException ex)
        {
            return TooLargeResult(id, ex, downloadInfo.PreviewUrl, client);
        }

        var response = McpResponse<object>.Success(
            new
            {
                id,
                original_file_name = asset.OriginalFileName,
                mime_type = mimeType,
                file_size = bytes.Length,
                encoding = "base64",
                thumbhash = asset.Thumbhash
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return BinaryResult(JsonSerializer.Serialize(response), bytes, mimeType, downloadInfo.PreviewUrl);
    }

    private static bool IsBase64Mode(ImmichClient client) =>
        string.Equals(client.DownloadMode, "base64", StringComparison.OrdinalIgnoreCase);

    private static CallToolResult AssetNotFoundResult(string id, ImmichClient client) =>
        TextResult(JsonSerializer.Serialize(McpErrorResponse.Create(
            ErrorCodes.NotFound,
            $"Asset with ID {id} not found",
            meta: new McpMeta { ImmichBaseUrl = client.BaseUrl })));

    private static CallToolResult TooLargeResult(string id, InlineDownloadTooLargeException ex, string? downloadUrl, ImmichClient client) =>
        TextResult(JsonSerializer.Serialize(McpErrorResponse.Create(
            ErrorCodes.PayloadTooLarge,
            ex.Message,
            details: new
            {
                id,
                file_size = ex.ContentLength,
                max_inline_download_bytes = ex.MaxInlineDownloadBytes,
                download_url = downloadUrl
            },
            meta: new McpMeta { ImmichBaseUrl = client.BaseUrl })));

    private static CallToolResult TextResult(string json)
    {
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = json
                }
            ]
        };
    }

    private static CallToolResult BinaryResult(string json, byte[] bytes, string mimeType, string? uri)
    {
        ContentBlock binaryBlock = mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? ImageContentBlock.FromBytes(bytes, mimeType)
            : new EmbeddedResourceBlock
            {
                Resource = BlobResourceContents.FromBytes(bytes, uri ?? string.Empty, mimeType)
            };
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock
                {
                    Text = json
                },
                binaryBlock
            ]
        };
    }

    [McpServerTool(Name = "immich_assets_upload")]
    [Description("Upload a new asset from base64-encoded content. For large files, use immich_assets_upload_from_path instead.")]
    public static async Task<string> Upload(
        ImmichClient client,
        [Description("Base64-encoded file content")] string fileContent,
        [Description("Original filename with extension")] string fileName,
        [Description("Mark as favorite (default: false)")] bool? isFavorite = null,
        [Description("Mark as archived (default: false)")] bool? isArchived = null)
    {
        byte[] fileBytes;
        try
        {
            fileBytes = Convert.FromBase64String(fileContent);
        }
        catch (FormatException)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "Invalid base64 file content",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var asset = await client.UploadAssetAsync(
            fileBytes,
            fileName,
            DateTime.UtcNow,
            isFavorite,
            isArchived
        ).ConfigureAwait(false);

        if (asset == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to upload asset",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new
            {
                asset_id = asset.Id,
                type = asset.Type,
                original_file_name = asset.OriginalFileName,
                status = "uploaded",
                message = "Asset uploaded successfully"
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich_assets_upload_from_path")]
    [Description("Upload an asset from a file path accessible to the MCP server. NOTE: Only works when the MCP server can access the path (e.g., stdio mode or shared filesystem). For remote HTTP mode, use immich_assets_upload with base64 content instead.")]
    public static async Task<string> UploadFromPath(
        ImmichClient client,
        [Description("Absolute path to the file to upload")] string filePath,
        [Description("Mark as favorite (default: false)")] bool? isFavorite = null,
        [Description("Mark as archived (default: false)")] bool? isArchived = null)
    {
        // Expand ~ to home directory
        if (filePath.StartsWith("~/"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            filePath = Path.Combine(home, filePath[2..]);
        }

        // Validate path
        if (!Path.IsPathRooted(filePath))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "File path must be absolute",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        if (!File.Exists(filePath))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"File not found: {filePath}",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var fileInfo = new FileInfo(filePath);
        var (asset, error) = await client.UploadAssetFromPathAsync(
            filePath,
            isFavorite,
            isArchived
        ).ConfigureAwait(false);

        if (asset == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                error ?? "Failed to upload asset",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new
            {
                asset_id = asset.Id,
                type = asset.Type,
                original_file_name = asset.OriginalFileName,
                file_size = fileInfo.Length,
                status = "uploaded",
                message = "Asset uploaded successfully"
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich_assets_update")]
    [Description("Update asset metadata (favorite status, description, date, location, etc.).")]
    public static async Task<string> Update(
        ImmichClient client,
        [Description("Asset ID (UUID)")] string id,
        [Description("Set favorite status")] bool? isFavorite = null,
        [Description("Set archived status")] bool? isArchived = null,
        [Description("Set description")] string? description = null,
        [Description("Set date/time original (ISO format)")] string? dateTimeOriginal = null,
        [Description("Set latitude")] double? latitude = null,
        [Description("Set longitude")] double? longitude = null,
        [Description("Set rating (0-5)")] int? rating = null)
    {
        var request = new AssetUpdateRequest
        {
            IsFavorite = isFavorite,
            Visibility = VisibilityFromArchived(isArchived),
            Description = description,
            DateTimeOriginal = ParseDate(dateTimeOriginal),
            Latitude = latitude,
            Longitude = longitude,
            Rating = rating
        };

        var asset = await client.UpdateAssetAsync(id, request).ConfigureAwait(false);

        if (asset == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Asset with ID {id} not found or update failed",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Asset>.Success(
            asset,
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich_assets_bulk_update")]
    [Description("Perform bulk operations on multiple assets. Supports dry run mode.")]
    public static async Task<string> BulkUpdate(
        ImmichClient client,
        [Description("Asset IDs (comma-separated UUIDs)")] string assetIds,
        [Description("Set favorite status for all")] bool? isFavorite = null,
        [Description("Set archived status for all")] bool? isArchived = null,
        [Description("Set rating for all (0-5)")] int? rating = null,
        [Description("Dry run mode - shows what would change without applying")] bool dryRun = true,
        [Description("Must be true to execute the operation")] bool confirm = false)
    {
        if (RequireIds(assetIds, client.BaseUrl, "asset IDs", out var ids) is { } idsError)
        {
            return idsError;
        }

        if (dryRun || !confirm)
        {
            var dryRunResult = new BulkOperationResult
            {
                AffectedIds = ids,
                Warnings = new List<string>
                {
                    dryRun ? "This is a dry run. Set dry_run=false and confirm=true to execute." : "Set confirm=true to execute the operation."
                },
                Executed = false
            };

            var dryRunResponse = McpResponse<BulkOperationResult>.Success(
                dryRunResult,
                new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(dryRunResponse);
        }

        var request = new AssetBulkUpdateRequest
        {
            Ids = ids,
            IsFavorite = isFavorite,
            Visibility = VisibilityFromArchived(isArchived),
            Rating = rating
        };

        var success = await client.BulkUpdateAssetsAsync(request).ConfigureAwait(false);

        if (!success)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Bulk update failed",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var result = new BulkOperationResult
        {
            AffectedIds = ids,
            Executed = true
        };

        var response = McpResponse<BulkOperationResult>.Success(
            result,
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [Obsolete("Use DeleteAssets instead.")]
    public static Task<string> Delete(
        ImmichClient client,
        string assetIds,
        bool force = false,
        bool dryRun = true,
        bool confirm = false)
        => DeleteAssets(client, assetIds, force, confirm, dryRun);

    [McpServerTool(Name = "immich_assets_delete")]
    [Description("Delete asset(s). Returns a preview unless confirm=true.")]
    public static async Task<string> DeleteAssets(
        ImmichClient client,
        [Description("Asset IDs (comma-separated UUIDs)")] string assetIds,
        [Description("Force delete (bypass trash)")] bool force = false,
        [Description("Must be true to confirm deletion")] bool confirm = false,
        [Description("Deprecated - omit. When true, forces a preview even if confirm=true.")] bool? dryRun = null)
    {
        if (RequireIds(assetIds, client.BaseUrl, "asset IDs", out var ids) is { } idsError)
        {
            return idsError;
        }

        // dryRun is retained, deprecated, only so callers written against the old
        // two-switch contract keep getting a preview instead of a surprise deletion.
        if (!confirm || dryRun == true)
        {
            // Get asset info for dry run
            var assetInfos = new List<object>();
            foreach (var id in ids.Take(10)) // Limit to 10 for dry run info
            {
                var asset = await client.GetAssetAsync(id).ConfigureAwait(false);
                if (asset != null)
                {
                    assetInfos.Add(new
                    {
                        id = asset.Id,
                        original_file_name = asset.OriginalFileName,
                        type = asset.Type,
                        created = asset.FileCreatedAt
                    });
                }
            }

            var dryRunResponse = McpErrorResponse.Create(
                ErrorCodes.ConfirmationRequired,
                $"Deletion requires confirm=true. This is a dry run showing what would be deleted ({ids.Length} asset(s)).",
                new
                {
                    asset_count = ids.Length,
                    force,
                    preview = assetInfos
                },
                new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(dryRunResponse);
        }

        var success = await client.DeleteAssetsAsync(ids, force).ConfigureAwait(false);

        if (!success)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to delete assets",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new
            {
                deleted = true,
                asset_count = ids.Length,
                asset_ids = ids,
                force
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich_assets_statistics")]
    [Description("Get asset statistics (count of images, videos, total).")]
    public static async Task<string> Statistics(ImmichClient client)
    {
        var stats = await client.GetAssetStatisticsAsync().ConfigureAwait(false);

        if (stats == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to retrieve asset statistics",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<AssetStatistics>.Success(
            stats,
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }
}
