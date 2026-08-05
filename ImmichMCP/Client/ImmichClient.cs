using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ImmichMCP.Configuration;
using ImmichMCP.Models.Common;
using ImmichMCP.Models.Assets;
using ImmichMCP.Models.Albums;
using ImmichMCP.Models.People;
using ImmichMCP.Models.Tags;
using ImmichMCP.Models.SharedLinks;
using ImmichMCP.Models.Activities;
using ImmichMCP.Models.Search;
using static ImmichMCP.Utils.ParsingHelpers;

namespace ImmichMCP.Client;

/// <summary>
/// Central client for all Immich API operations.
/// </summary>
public class ImmichClient
{
    private readonly HttpClient _httpClient;
    private readonly ImmichOptions _options;
    private readonly ILogger<ImmichClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new Utils.UtcIsoDateTimeConverter() }
    };

    public ImmichClient(HttpClient httpClient, IOptions<ImmichOptions> options, ILogger<ImmichClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string BaseUrl => _options.BaseUrl;

    public string DownloadMode => _options.DownloadMode;

    #region Health & Status

    /// <summary>
    /// Checks connectivity and returns API status information.
    /// </summary>
    public async Task<(bool Success, ServerInfo? Info, string? Error)> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/server/about", cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var info = await response.Content.ReadFromJsonAsync<ServerInfo>(JsonOptions, cancellationToken).ConfigureAwait(false);
                return (true, info, null);
            }

            return (false, null, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        }
        catch (OperationCanceledException)
        {
            // Let cancellation (caller abort or timeout) propagate; callers distinguish the two
            // via their own token rather than have it folded into a generic failure result here.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ping Immich API");
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// Gets server features/config.
    /// </summary>
    public async Task<(bool Success, ServerFeatures? Features, string? Error)> GetFeaturesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("api/server/features", cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var features = await response.Content.ReadFromJsonAsync<ServerFeatures>(JsonOptions, cancellationToken).ConfigureAwait(false);
                return (true, features, null);
            }

            return (false, null, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Immich features");
            return (false, null, ex.Message);
        }
    }

    #endregion

    #region Assets

    /// <summary>
    /// Gets all assets with optional filters.
    /// </summary>
    public async Task<List<Asset>> GetAssetsAsync(
        int? size = null,
        DateTime? updatedAfter = null,
        DateTime? updatedBefore = null,
        bool? isFavorite = null,
        bool? isArchived = null,
        bool? isTrashed = null,
        CancellationToken cancellationToken = default)
    {
        var request = new MetadataSearchRequest
        {
            Size = size,
            UpdatedAfter = updatedAfter,
            UpdatedBefore = updatedBefore,
            IsFavorite = isFavorite,
            Visibility = VisibilityFromArchived(isArchived),
            WithDeleted = isTrashed == true ? true : null,
            TrashedAfter = isTrashed == true ? DateTime.UnixEpoch : null,
            WithExif = true
        };

        var result = await SearchMetadataAsync(request, cancellationToken).ConfigureAwait(false);
        return result.Items;
    }

    /// <summary>
    /// Gets an asset by ID.
    /// </summary>
    public async Task<Asset?> GetAssetAsync(string id, CancellationToken cancellationToken = default)
    {
        return await GetAsync<Asset>($"api/assets/{id}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates an asset.
    /// </summary>
    public async Task<Asset?> UpdateAssetAsync(string id, AssetUpdateRequest request, CancellationToken cancellationToken = default)
    {
        return await PutAsync<Asset>($"api/assets/{id}", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Bulk updates multiple assets.
    /// </summary>
    public async Task<bool> BulkUpdateAssetsAsync(AssetBulkUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync("api/assets", request, JsonOptions, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        throw await ImmichApiException.FromResponseAsync(response, "PUT", "api/assets", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes assets.
    /// </summary>
    public async Task<bool> DeleteAssetsAsync(string[] ids, bool force = false, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "api/assets")
        {
            Content = JsonContent.Create(new { ids, force }, options: JsonOptions)
        };
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent)
        {
            return true;
        }

        throw await ImmichApiException.FromResponseAsync(response, "DELETE", "api/assets", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets asset statistics.
    /// </summary>
    public async Task<AssetStatistics?> GetAssetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync<AssetStatistics>("api/assets/statistics", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Uploads an asset from bytes.
    /// </summary>
    public async Task<Asset?> UploadAssetAsync(
        byte[] fileContent,
        string fileName,
        DateTime deviceModifiedAt,
        bool? isFavorite = null,
        bool? isArchived = null,
        bool? isVisible = null,
        int? duration = null,
        CancellationToken cancellationToken = default)
    {
        using var formContent = new MultipartFormDataContent();
        var fileStreamContent = new ByteArrayContent(fileContent);
        fileStreamContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(fileName));
        formContent.Add(fileStreamContent, "assetData", fileName);
        formContent.Add(new StringContent(fileName), "filename");
        formContent.Add(new StringContent(deviceModifiedAt.ToString("O")), "fileCreatedAt");
        formContent.Add(new StringContent(deviceModifiedAt.ToString("O")), "fileModifiedAt");

        if (isFavorite.HasValue)
            formContent.Add(new StringContent(isFavorite.Value.ToString().ToLower()), "isFavorite");

        var visibility = VisibilityFromUploadFlags(isArchived, isVisible);
        if (!string.IsNullOrEmpty(visibility))
            formContent.Add(new StringContent(visibility), "visibility");

        if (duration.HasValue)
            formContent.Add(new StringContent(duration.Value.ToString()), "duration");

        try
        {
            var response = await _httpClient.PostAsync("api/assets", formContent, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var uploadResult = await response.Content
                    .ReadFromJsonAsync<AssetMediaResponse>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(uploadResult?.Id))
                {
                    _logger.LogError("Immich returned a successful upload response without an asset ID");
                    return null;
                }

                return await GetAssetAsync(uploadResult.Id, cancellationToken).ConfigureAwait(false);
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("Failed to upload asset: {StatusCode} - {Error}", response.StatusCode, error);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload asset");
            return null;
        }
    }

    /// <summary>
    /// Uploads an asset from a file path.
    /// </summary>
    public async Task<(Asset? Asset, string? Error)> UploadAssetFromPathAsync(
        string filePath,
        bool? isFavorite = null,
        bool? isArchived = null,
        bool? isVisible = null,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return (null, $"File not found: {filePath}");
        }

        var fileName = Path.GetFileName(filePath);
        var fileInfo = new FileInfo(filePath);

        _logger.LogInformation("Starting upload of {FileName} ({Size:N0} bytes)", fileName, fileInfo.Length);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
                var asset = await UploadAssetAsync(
                    fileBytes,
                    fileName,
                    fileInfo.LastWriteTimeUtc,
                    isFavorite,
                    isArchived,
                    isVisible,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (asset != null)
                {
                    _logger.LogInformation("Successfully uploaded {FileName}, asset ID: {AssetId}", fileName, asset.Id);
                    return (asset, null);
                }

                _logger.LogWarning("Upload attempt {Attempt}/{MaxRetries} failed for {FileName}", attempt, maxRetries, fileName);

                if (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogInformation("Retrying in {Delay}...", delay);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.LogWarning(ex, "Error on attempt {Attempt}/{MaxRetries}, retrying...", attempt, maxRetries);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error uploading {FileName}", fileName);
                return (null, $"Upload failed: {ex.Message}");
            }
        }

        return (null, $"Upload failed after {maxRetries} attempts");
    }

    /// <summary>
    /// Gets asset download info.
    /// </summary>
    public AssetDownloadInfo GetAssetDownloadInfo(string id, string? originalFileName)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        return new AssetDownloadInfo
        {
            Id = id,
            OriginalFileName = originalFileName,
            OriginalUrl = $"{baseUrl}/api/assets/{id}/original",
            ThumbnailUrl = $"{baseUrl}/api/assets/{id}/thumbnail",
            PreviewUrl = $"{baseUrl}/api/assets/{id}/thumbnail?size=preview"
        };
    }

    /// <summary>
    /// Downloads the preview-size thumbnail bytes for an asset.
    /// </summary>
    public async Task<(byte[] Bytes, string MimeType)> DownloadAssetThumbnailAsync(string id, CancellationToken cancellationToken = default)
        => await DownloadBytesAsync($"api/assets/{id}/thumbnail?size=preview", cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Downloads the original file bytes for an asset.
    /// </summary>
    public async Task<(byte[] Bytes, string MimeType)> DownloadAssetOriginalAsync(string id, CancellationToken cancellationToken = default)
        => await DownloadBytesAsync($"api/assets/{id}/original", cancellationToken).ConfigureAwait(false);

    #endregion

    #region Search

    /// <summary>
    /// Performs a metadata search.
    /// </summary>
    public async Task<SearchAssetResult<Asset>> SearchMetadataAsync(
        MetadataSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/search/metadata", request, JsonOptions, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<SearchResult<Asset>>(JsonOptions, cancellationToken).ConfigureAwait(false);
            return result?.Assets ?? new SearchAssetResult<Asset>();
        }

        throw await ImmichApiException.FromResponseAsync(response, "POST", "api/search/metadata", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Performs a smart (ML/CLIP) search.
    /// </summary>
    public async Task<SearchAssetResult<Asset>> SearchSmartAsync(
        SmartSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/search/smart", request, JsonOptions, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<SearchResult<Asset>>(JsonOptions, cancellationToken).ConfigureAwait(false);
            return result?.Assets ?? new SearchAssetResult<Asset>();
        }

        throw await ImmichApiException.FromResponseAsync(response, "POST", "api/search/smart", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets search explore data.
    /// </summary>
    public async Task<List<ExploreData>?> SearchExploreAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync<List<ExploreData>>("api/search/explore", cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Albums

    /// <summary>
    /// Gets all albums.
    /// </summary>
    public async Task<List<Album>> GetAlbumsAsync(
        bool? shared = null,
        bool? isOwned = null,
        string? assetId = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();

        if (shared.HasValue) queryParams.Add($"isShared={shared.Value.ToString().ToLower()}");
        if (isOwned.HasValue) queryParams.Add($"isOwned={isOwned.Value.ToString().ToLower()}");
        if (!string.IsNullOrEmpty(assetId)) queryParams.Add($"assetId={assetId}");

        var url = queryParams.Count > 0
            ? $"api/albums?{string.Join("&", queryParams)}"
            : "api/albums";

        return await GetAsync<List<Album>>(url, cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>
    /// Gets an album by ID.
    /// </summary>
    public async Task<Album?> GetAlbumAsync(string id, CancellationToken cancellationToken = default)
    {
        return await GetAsync<Album>($"api/albums/{id}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new album.
    /// </summary>
    public async Task<Album?> CreateAlbumAsync(AlbumCreateRequest request, CancellationToken cancellationToken = default)
    {
        return await PostAsync<Album>("api/albums", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates an album.
    /// </summary>
    public async Task<Album?> UpdateAlbumAsync(string id, AlbumUpdateRequest request, CancellationToken cancellationToken = default)
    {
        return await PatchAsync<Album>($"api/albums/{id}", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes an album.
    /// </summary>
    public async Task<bool> DeleteAlbumAsync(string id, CancellationToken cancellationToken = default)
    {
        return await DeleteAsync($"api/albums/{id}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds assets to an album.
    /// </summary>
    public async Task<List<BulkIdResponse>?> AddAssetsToAlbumAsync(string albumId, string[] assetIds, CancellationToken cancellationToken = default)
    {
        var request = new { ids = assetIds };
        return await PutAsync<List<BulkIdResponse>>($"api/albums/{albumId}/assets", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes assets from an album.
    /// </summary>
    public async Task<List<BulkIdResponse>?> RemoveAssetsFromAlbumAsync(string albumId, string[] assetIds, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"api/albums/{albumId}/assets")
        {
            Content = JsonContent.Create(new { ids = assetIds }, options: JsonOptions)
        };
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<List<BulkIdResponse>>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        throw await ImmichApiException.FromResponseAsync(response, "DELETE", $"api/albums/{albumId}/assets", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets album statistics.
    /// </summary>
    public async Task<AlbumStatistics?> GetAlbumStatisticsAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync<AlbumStatistics>("api/albums/statistics", cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region People

    /// <summary>
    /// Gets all people.
    /// </summary>
    public Task<PeopleResponse?> GetPeopleAsync(
        bool? withHidden = null,
        CancellationToken cancellationToken = default)
    {
        return GetPeopleAsync(withHidden, page: null, size: null, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Gets a page of recognized people.
    /// </summary>
    public async Task<PeopleResponse?> GetPeopleAsync(
        bool? withHidden,
        int? page,
        int? size,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();
        if (withHidden.HasValue) queryParams.Add($"withHidden={withHidden.Value.ToString().ToLower()}");
        if (page.HasValue) queryParams.Add($"page={page.Value}");
        if (size.HasValue) queryParams.Add($"size={size.Value}");

        var url = queryParams.Count > 0
            ? $"api/people?{string.Join("&", queryParams)}"
            : "api/people";

        return await GetAsync<PeopleResponse>(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a person by ID.
    /// </summary>
    public async Task<Person?> GetPersonAsync(string id, CancellationToken cancellationToken = default)
    {
        return await GetAsync<Person>($"api/people/{id}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates a person.
    /// </summary>
    public async Task<Person?> UpdatePersonAsync(string id, PersonUpdateRequest request, CancellationToken cancellationToken = default)
    {
        return await PutAsync<Person>($"api/people/{id}", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Merges people.
    /// </summary>
    public async Task<List<BulkIdResponse>?> MergePeopleAsync(string targetId, string[] sourceIds, CancellationToken cancellationToken = default)
    {
        var request = new { ids = sourceIds };
        return await PostAsync<List<BulkIdResponse>>($"api/people/{targetId}/merge", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets all assets for a person.
    /// </summary>
    /// <remarks>
    /// Immich removed <c>GET /api/people/{id}/assets</c>. Follow metadata-search
    /// pagination tokens so this compatibility method retains its complete-list contract.
    /// </remarks>
    public async Task<List<Asset>> GetPersonAssetsAsync(string personId, CancellationToken cancellationToken = default)
    {
        const int pageSize = 1000;
        var assets = new List<Asset>();
        var visitedPages = new HashSet<int>();
        var page = 1;

        while (true)
        {
            if (!visitedPages.Add(page))
            {
                throw new InvalidOperationException($"Immich returned a repeated metadata search page token '{page}'.");
            }

            var result = await SearchPersonAssetsAsync(
                personId,
                page,
                pageSize,
                cancellationToken).ConfigureAwait(false);
            assets.AddRange(result.Items);

            if (result.NextPage is null)
            {
                return assets;
            }

            if (!int.TryParse(result.NextPage, out page) || page < 1)
            {
                throw new InvalidOperationException(
                    $"Immich returned an invalid metadata search nextPage token '{result.NextPage}'.");
            }
        }
    }

    /// <summary>
    /// Searches assets associated with a person using Immich's metadata search endpoint.
    /// </summary>
    public async Task<SearchAssetResult<Asset>> SearchPersonAssetsAsync(
        string personId,
        int? page = null,
        int? size = null,
        CancellationToken cancellationToken = default)
    {
        return await SearchMetadataAsync(
            new MetadataSearchRequest
            {
                PersonIds = [personId],
                Page = page,
                Size = size,
                WithExif = true
            },
            cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Tags

    /// <summary>
    /// Gets all tags.
    /// </summary>
    public async Task<List<Tag>> GetTagsAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync<List<Tag>>("api/tags", cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>
    /// Gets a tag by ID.
    /// </summary>
    public async Task<Tag?> GetTagAsync(string id, CancellationToken cancellationToken = default)
    {
        return await GetAsync<Tag>($"api/tags/{id}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new tag.
    /// </summary>
    public async Task<Tag?> CreateTagAsync(TagCreateRequest request, CancellationToken cancellationToken = default)
    {
        return await PostAsync<Tag>("api/tags", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates a tag.
    /// </summary>
    public async Task<Tag?> UpdateTagAsync(string id, TagUpdateRequest request, CancellationToken cancellationToken = default)
    {
        return await PutAsync<Tag>($"api/tags/{id}", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a tag.
    /// </summary>
    public async Task<bool> DeleteTagAsync(string id, CancellationToken cancellationToken = default)
    {
        return await DeleteAsync($"api/tags/{id}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tags assets.
    /// </summary>
    public async Task<List<BulkIdResponse>?> TagAssetsAsync(string tagId, string[] assetIds, CancellationToken cancellationToken = default)
    {
        var request = new { ids = assetIds };
        return await PutAsync<List<BulkIdResponse>>($"api/tags/{tagId}/assets", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Untags assets.
    /// </summary>
    public async Task<List<BulkIdResponse>?> UntagAssetsAsync(string tagId, string[] assetIds, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"api/tags/{tagId}/assets")
        {
            Content = JsonContent.Create(new { ids = assetIds }, options: JsonOptions)
        };
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<List<BulkIdResponse>>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        throw await ImmichApiException.FromResponseAsync(response, "DELETE", $"api/tags/{tagId}/assets", cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Shared Links

    /// <summary>
    /// Gets all shared links.
    /// </summary>
    public async Task<List<SharedLink>> GetSharedLinksAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync<List<SharedLink>>("api/shared-links", cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>
    /// Gets a shared link by ID.
    /// </summary>
    public async Task<SharedLink?> GetSharedLinkAsync(string id, CancellationToken cancellationToken = default)
    {
        return await GetAsync<SharedLink>($"api/shared-links/{id}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a shared link.
    /// </summary>
    public async Task<SharedLink?> CreateSharedLinkAsync(SharedLinkCreateRequest request, CancellationToken cancellationToken = default)
    {
        return await PostAsync<SharedLink>("api/shared-links", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates a shared link.
    /// </summary>
    public async Task<SharedLink?> UpdateSharedLinkAsync(string id, SharedLinkUpdateRequest request, CancellationToken cancellationToken = default)
    {
        return await PatchAsync<SharedLink>($"api/shared-links/{id}", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a shared link.
    /// </summary>
    public async Task<bool> DeleteSharedLinkAsync(string id, CancellationToken cancellationToken = default)
    {
        return await DeleteAsync($"api/shared-links/{id}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds assets to a shared link.
    /// </summary>
    public async Task<List<AssetIdResponse>?> AddAssetsToSharedLinkAsync(string linkId, string[] assetIds, CancellationToken cancellationToken = default)
    {
        var request = new { ids = assetIds };
        return await PutAsync<List<AssetIdResponse>>($"api/shared-links/{linkId}/assets", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes assets from a shared link.
    /// </summary>
    public async Task<List<AssetIdResponse>?> RemoveAssetsFromSharedLinkAsync(string linkId, string[] assetIds, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"api/shared-links/{linkId}/assets")
        {
            Content = JsonContent.Create(new { ids = assetIds }, options: JsonOptions)
        };
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<List<AssetIdResponse>>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        throw await ImmichApiException.FromResponseAsync(response, "DELETE", $"api/shared-links/{linkId}/assets", cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region Activities

    /// <summary>
    /// Gets activities for an album or asset.
    /// </summary>
    public async Task<List<Activity>> GetActivitiesAsync(
        string albumId,
        string? assetId = null,
        string? type = null,
        string? level = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string> { $"albumId={albumId}" };

        if (!string.IsNullOrEmpty(assetId)) queryParams.Add($"assetId={assetId}");
        if (!string.IsNullOrEmpty(type)) queryParams.Add($"type={type}");
        if (!string.IsNullOrEmpty(level)) queryParams.Add($"level={level}");

        var url = $"api/activities?{string.Join("&", queryParams)}";
        return await GetAsync<List<Activity>>(url, cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>
    /// Creates an activity (comment or like).
    /// </summary>
    public async Task<Activity?> CreateActivityAsync(ActivityCreateRequest request, CancellationToken cancellationToken = default)
    {
        return await PostAsync<Activity>("api/activities", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes an activity.
    /// </summary>
    public async Task<bool> DeleteActivityAsync(string id, CancellationToken cancellationToken = default)
    {
        return await DeleteAsync($"api/activities/{id}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets activity statistics.
    /// </summary>
    public async Task<ActivityStatistics?> GetActivityStatisticsAsync(string albumId, string? assetId = null, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrEmpty(assetId)
            ? $"api/activities/statistics?albumId={albumId}"
            : $"api/activities/statistics?albumId={albumId}&assetId={assetId}";

        return await GetAsync<ActivityStatistics>(url, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region HTTP Helpers

    // NOTE ON ERROR HANDLING: these helpers never swallow an upstream failure into a
    // default/empty value. On a non-success status they throw ImmichApiException so the
    // MCP boundary can report a spec-compliant tool execution error (isError: true).
    // The single deliberate exception is 404 on a get-by-id, which is a legitimate
    // "not found" result the caller maps to a NOT_FOUND response.

    private async Task<(byte[] Bytes, string MimeType)> DownloadBytesAsync(string url, CancellationToken cancellationToken)
    {
        var maxBytes = _options.MaxInlineDownloadBytes;

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await ImmichApiException.FromResponseAsync(response, "GET", url, cancellationToken).ConfigureAwait(false);
        }

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength > maxBytes)
        {
            throw new InlineDownloadTooLargeException(url, contentLength, maxBytes);
        }

        var mimeType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        // Bounded read: Content-Length can be absent (chunked) or lie, so never
        // buffer more than the cap regardless of what the headers said.
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new InlineDownloadTooLargeException(url, contentLength, maxBytes);
            }

            buffer.Write(chunk, 0, read);
        }

        return (buffer.ToArray(), mimeType);
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default; // legitimate not-found, surfaced by the tool as NOT_FOUND
        }

        throw await ImmichApiException.FromResponseAsync(response, "GET", url, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> PostAsync<T>(string url, object request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync(url, request, JsonOptions, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        throw await ImmichApiException.FromResponseAsync(response, "POST", url, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> PutAsync<T>(string url, object request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PutAsJsonAsync(url, request, JsonOptions, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        throw await ImmichApiException.FromResponseAsync(response, "PUT", url, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T?> PatchAsync<T>(string url, object request, CancellationToken cancellationToken)
    {
        var content = JsonContent.Create(request, options: JsonOptions);
        var response = await _httpClient.PatchAsync(url, content, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        throw await ImmichApiException.FromResponseAsync(response, "PATCH", url, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> DeleteAsync(string url, CancellationToken cancellationToken)
    {
        var response = await _httpClient.DeleteAsync(url, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent)
        {
            return true;
        }

        throw await ImmichApiException.FromResponseAsync(response, "DELETE", url, cancellationToken).ConfigureAwait(false);
    }

    private static string GetContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".heic" => "image/heic",
        ".heif" => "image/heif",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".m4v" => "video/x-m4v",
        _ => "application/octet-stream"
    };

    #endregion
}

/// <summary>
/// Response for bulk ID operations.
/// </summary>
public record BulkIdResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("errorMessage")]
    public string? Error { get; init; }
}

/// <summary>
/// Response for shared-link asset membership operations.
/// </summary>
public record AssetIdResponse
{
    [JsonPropertyName("assetId")]
    public string AssetId { get; init; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

/// <summary>
/// Server information.
/// </summary>
public record ServerInfo
{
    public string Version { get; init; } = string.Empty;
    public string VersionUrl { get; init; } = string.Empty;
    public bool Licensed { get; init; }
    public string Build { get; init; } = string.Empty;
    public string BuildUrl { get; init; } = string.Empty;
    public string BuildImage { get; init; } = string.Empty;
    public string BuildImageUrl { get; init; } = string.Empty;
    public string Repository { get; init; } = string.Empty;
    public string RepositoryUrl { get; init; } = string.Empty;
    public string SourceRef { get; init; } = string.Empty;
    public string SourceCommit { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public string Nodejs { get; init; } = string.Empty;
    public string Ffmpeg { get; init; } = string.Empty;
    public string Libvips { get; init; } = string.Empty;
    public string Exiftool { get; init; } = string.Empty;
    public string ImageMagick { get; init; } = string.Empty;
}

/// <summary>
/// Server features.
/// </summary>
public record ServerFeatures
{
    public bool Trash { get; init; }
    public bool Map { get; init; }
    public bool ReverseGeocoding { get; init; }
    public bool ImportFaces { get; init; }
    public bool Sidecar { get; init; }
    public bool Search { get; init; }
    public bool FacialRecognition { get; init; }
    public bool Oauth { get; init; }
    public bool OauthAutoLaunch { get; init; }
    public bool PasswordLogin { get; init; }
    public bool ConfigFile { get; init; }
    public bool DuplicateDetection { get; init; }
    public bool Email { get; init; }
    public bool SmartSearch { get; init; }
    public bool Ocr { get; init; }
    public bool RealtimeTranscoding { get; init; }
}
