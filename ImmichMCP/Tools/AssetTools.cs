using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    private static readonly string[] SaveSizes = ["original", "fullsize", "preview", "thumbnail"];
    private const int MaxSaveBatch = 500;
    private const int SaveParallelism = 4;

    [McpServerTool(Name = "immich_assets_save_to_path", Idempotent = true)]
    [Description(
        "Save assets from Immich into a local directory on the machine running this MCP server and return the " +
        "file names, so they can be opened or processed directly. size='original' (default) writes the uploaded " +
        "file under its original name and verifies it against the Immich checksum; 'fullsize', 'preview' and " +
        "'thumbnail' write server-rendered images named <name>_<id8>_<size>.<ext>. Files already present with the " +
        "same content are skipped, so re-running is safe. Available in stdio mode; over HTTP only when " +
        "SAVE_ROOT_DIR is set. If SAVE_ROOT_DIR is set, the directory must be inside it.")]
    public static async Task<string> SaveToPath(
        ImmichClient client,
        [Description("Asset IDs (comma-separated UUIDs, at most 500 per call)")] string assetIds,
        [Description("Absolute target directory (~/ allowed); created if missing")] string directory,
        [Description("Rendition to save: original (default), fullsize, preview or thumbnail")] string size = "original",
        [Description("Replace files that already exist under the target name (default: false)")] bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        if (!client.SaveToPathEnabled)
        {
            return ValidationError(client, "Saving files is disabled when the server runs over HTTP. Set SAVE_ROOT_DIR to enable it.");
        }

        if (RequireIds(assetIds, client.BaseUrl, "asset IDs", out var parsedIds) is { } idsError)
        {
            return idsError;
        }

        var ids = parsedIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length > MaxSaveBatch)
        {
            return ValidationError(client, $"At most {MaxSaveBatch} asset IDs per call, got {ids.Length}. Split the list into batches.");
        }

        size = (size ?? "original").Trim().ToLowerInvariant();
        if (!SaveSizes.Contains(size))
        {
            return ValidationError(client, $"Unknown size '{size}'. Use one of: {string.Join(", ", SaveSizes)}.");
        }

        if (ResolveSaveDirectory(directory, client.SaveRootDirectory, out var targetDir) is { } directoryError)
        {
            return ValidationError(client, directoryError);
        }

        try
        {
            Directory.CreateDirectory(targetDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ValidationError(client, $"Cannot create directory {targetDir}: {ex.Message}");
        }

        var parallel = new ParallelOptions { MaxDegreeOfParallelism = SaveParallelism, CancellationToken = cancellationToken };
        var assets = new Asset?[ids.Length];
        var files = new SavedFile[ids.Length];

        await Parallel.ForEachAsync(Enumerable.Range(0, ids.Length), parallel, async (i, ct) =>
        {
            try
            {
                assets[i] = await client.GetAssetAsync(ids[i], ct).ConfigureAwait(false);
                if (assets[i] == null)
                {
                    files[i] = SavedFile.Failed(ids[i], "not_found", "Asset not found");
                }
            }
            catch (Exception ex) when (ex is ImmichApiException or HttpRequestException)
            {
                files[i] = SavedFile.Failed(ids[i], "failed", ex.Message);
            }
        }).ConfigureAwait(false);

        // Target names are chosen up front, one asset after the other, so two assets that
        // share a file name within one batch never race for the same path.
        var plans = new List<SavePlan>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ids.Length; i++)
        {
            if (assets[i] is not { } asset)
            {
                continue;
            }

            var plan = PlanSave(asset, i, size, targetDir, overwrite, claimed);
            if (plan.Done is { } done)
            {
                files[i] = done;
            }
            else
            {
                plans.Add(plan);
            }
        }

        await Parallel.ForEachAsync(plans, parallel, async (plan, ct) =>
        {
            try
            {
                var download = await client.DownloadAssetToFileAsync(
                    plan.Asset.Id,
                    size,
                    mimeType => plan.TargetPath ?? Path.Combine(targetDir, plan.NamePrefix + ExtensionFor(mimeType)),
                    overwrite,
                    ct).ConfigureAwait(false);

                bool? verified = size == "original" && !string.IsNullOrEmpty(plan.Asset.Checksum)
                    ? download.Sha1Base64 == plan.Asset.Checksum
                    : null;
                files[plan.Index] = SavedFile.Saved(plan.Asset.Id, Path.GetFileName(download.Path), download.Bytes, verified);
            }
            catch (Exception ex) when (ex is ImmichApiException or HttpRequestException or IOException or UnauthorizedAccessException)
            {
                files[plan.Index] = SavedFile.Failed(plan.Asset.Id, "failed", ex.Message);
            }
        }).ConfigureAwait(false);

        var saved = files.Count(f => f.Status == "saved");
        var existing = files.Count(f => f.Status == "exists");
        var payload = new
        {
            directory = targetDir,
            size,
            saved,
            existing,
            failed = files.Length - saved - existing,
            files
        };

        if (saved + existing == 0)
        {
            return JsonSerializer.Serialize(McpErrorResponse.Create(
                files.All(f => f.Status == "not_found") ? ErrorCodes.NotFound : ErrorCodes.UpstreamError,
                "No asset could be saved.",
                details: payload,
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }));
        }

        return JsonSerializer.Serialize(McpResponse<object>.Success(
            payload,
            new McpMeta { Total = files.Length, ImmichBaseUrl = client.BaseUrl }));
    }

    private sealed record SavePlan(int Index, Asset Asset, string? TargetPath, string? NamePrefix, SavedFile? Done);

    private sealed record SavedFile(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("file"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? File,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("bytes"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Bytes,
        [property: JsonPropertyName("checksum_verified"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ChecksumVerified,
        [property: JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error)
    {
        public static SavedFile Saved(string id, string file, long bytes, bool? verified) => new(id, file, "saved", bytes, verified, null);

        public static SavedFile Existing(string id, string file, long bytes) => new(id, file, "exists", bytes, null, null);

        public static SavedFile Failed(string id, string status, string error, string? file = null) => new(id, file, status, null, null, error);
    }

    private static SavePlan PlanSave(Asset asset, int index, string size, string directory, bool overwrite, HashSet<string> claimed)
    {
        var id8 = asset.Id.Length > 8 ? asset.Id[..8] : asset.Id;
        var fileName = SafeFileName(asset.OriginalFileName, asset.Id);
        var stem = Path.GetFileNameWithoutExtension(fileName);

        if (size != "original")
        {
            // Rendered images carry the asset ID in their name, so they cannot collide. The
            // extension follows the served media type, so look for any earlier copy.
            var prefix = $"{stem}_{id8}_{size}";
            var earlier = overwrite
                ? null
                : Directory.EnumerateFiles(directory).FirstOrDefault(f =>
                    Path.GetFileName(f).StartsWith(prefix + ".", StringComparison.Ordinal) &&
                    !f.EndsWith(".part", StringComparison.Ordinal));
            return earlier == null
                ? new SavePlan(index, asset, null, prefix, null)
                : new SavePlan(index, asset, null, prefix, SavedFile.Existing(asset.Id, Path.GetFileName(earlier), new FileInfo(earlier).Length));
        }

        foreach (var candidate in new[] { fileName, $"{stem}_{id8}{Path.GetExtension(fileName)}" })
        {
            var path = Path.Combine(directory, candidate);
            if (!claimed.Add(path))
            {
                continue; // an earlier asset in this batch already uses the name
            }

            if (overwrite || !File.Exists(path))
            {
                return new SavePlan(index, asset, path, null, null);
            }

            if (HasChecksum(path, asset.Checksum))
            {
                return new SavePlan(index, asset, path, null, SavedFile.Existing(asset.Id, candidate, new FileInfo(path).Length));
            }
        }

        return new SavePlan(index, asset, null, null, SavedFile.Failed(asset.Id, "conflict",
            "Files with this name and its ID-suffixed variant already exist with different content; pass overwrite=true to replace them.",
            fileName));
    }

    private static bool HasChecksum(string path, string? checksum)
    {
        if (string.IsNullOrEmpty(checksum))
        {
            return false;
        }

        using var stream = File.OpenRead(path);
        return Convert.ToBase64String(SHA1.HashData(stream)) == checksum;
    }

    /// <summary>
    /// File name from Immich metadata, reduced to a single path segment so a crafted
    /// original file name can never point outside the target directory.
    /// </summary>
    private static string SafeFileName(string? originalFileName, string fallback)
    {
        var name = Path.GetFileName((originalFileName ?? string.Empty).Replace('\\', '/'));
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimStart('.');
        return name.Length == 0 ? fallback : name;
    }

    private static string ExtensionFor(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        "image/png" => ".png",
        "image/avif" => ".avif",
        "image/heic" or "image/heif" => ".heic",
        _ => ".bin"
    };

    private static string? ResolveSaveDirectory(string? directory, string? saveRoot, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return "directory is required";
        }

        var expanded = ExpandHome(directory.Trim());
        if (!Path.IsPathRooted(expanded))
        {
            return "directory must be an absolute path";
        }

        fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
        if (string.IsNullOrWhiteSpace(saveRoot))
        {
            return null;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ExpandHome(saveRoot.Trim())));
        var inside = fullPath.Equals(root, StringComparison.Ordinal) ||
                     fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        return inside ? null : $"directory must be inside SAVE_ROOT_DIR ({root})";
    }

    private static string ExpandHome(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path == "~")
        {
            return home;
        }

        return path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(home, path[2..]) : path;
    }

    private static string ValidationError(ImmichClient client, string message) =>
        JsonSerializer.Serialize(McpErrorResponse.Create(
            ErrorCodes.Validation,
            message,
            meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }));

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

    // Image formats MCP clients can display. Anything else, e.g. the HEIC originals
    // iPhones produce, is returned as an embedded resource instead of an image block.
    private static readonly HashSet<string> InlineImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp"
    };

    private static CallToolResult BinaryResult(string json, byte[] bytes, string mimeType, string? uri)
    {
        ContentBlock binaryBlock = InlineImageMimeTypes.Contains(mimeType)
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
