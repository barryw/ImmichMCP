using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ImmichMCP.Client;
using ImmichMCP.Models.Common;
using ImmichMCP.Models.Assets;
using static ImmichMCP.Utils.ParsingHelpers;

namespace ImmichMCP.Tools;

/// <summary>
/// MCP tools for asset operations.
/// </summary>
[McpServerToolType]
public static class AssetTools
{
    [McpServerTool(Name = "immich.assets.list")]
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
            size: Math.Min(size, 1000),
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

    [McpServerTool(Name = "immich.assets.get")]
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

    [McpServerTool(Name = "immich.assets.exif")]
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

    [McpServerTool(Name = "immich.assets.download.original")]
    [Description("Get download URL for the original asset file.")]
    public static async Task<string> DownloadOriginal(
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

        var downloadInfo = client.GetAssetDownloadInfo(id, asset.OriginalFileName);

        var response = McpResponse<object>.Success(
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
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.assets.download.thumbnail")]
    [Description("Get thumbnail and preview URLs for an asset.")]
    public static async Task<string> DownloadThumbnail(
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

        var downloadInfo = client.GetAssetDownloadInfo(id, asset.OriginalFileName);

        var response = McpResponse<object>.Success(
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
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.assets.upload")]
    [Description("Upload a new asset from base64-encoded content. For large files, use immich.assets.upload_from_path instead.")]
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

        var deviceAssetId = $"{fileName}-{fileBytes.Length}-{DateTime.UtcNow.Ticks}";

        var asset = await client.UploadAssetAsync(
            fileBytes,
            fileName,
            deviceAssetId,
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

    [McpServerTool(Name = "immich.assets.upload_from_path")]
    [Description("Upload an asset from a local file path. More reliable for large files. Includes automatic retries.")]
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

    [McpServerTool(Name = "immich.assets.update")]
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
            IsArchived = isArchived,
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

    [McpServerTool(Name = "immich.assets.bulk_update")]
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
            IsArchived = isArchived,
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

    [McpServerTool(Name = "immich.assets.delete")]
    [Description("Delete asset(s). Requires explicit confirmation.")]
    public static async Task<string> Delete(
        ImmichClient client,
        [Description("Asset IDs (comma-separated UUIDs)")] string assetIds,
        [Description("Force delete (bypass trash)")] bool force = false,
        [Description("Dry run mode - shows what would be deleted without applying")] bool dryRun = true,
        [Description("Must be true to confirm deletion")] bool confirm = false)
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

        if (dryRun || !confirm)
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

    [McpServerTool(Name = "immich.assets.statistics")]
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
