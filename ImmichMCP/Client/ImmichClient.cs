using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public ImmichClient(HttpClient httpClient, IOptions<ImmichOptions> options, ILogger<ImmichClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string BaseUrl => _options.BaseUrl;

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
        string? userId = null,
        bool? isFavorite = null,
        bool? isArchived = null,
        bool? isTrashed = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();

        if (size.HasValue) queryParams.Add($"size={size.Value}");
        if (updatedAfter.HasValue) queryParams.Add($"updatedAfter={updatedAfter.Value:O}");
        if (updatedBefore.HasValue) queryParams.Add($"updatedBefore={updatedBefore.Value:O}");
        if (!string.IsNullOrEmpty(userId)) queryParams.Add($"userId={userId}");
        if (isFavorite.HasValue) queryParams.Add($"isFavorite={isFavorite.Value.ToString().ToLower()}");
        if (isArchived.HasValue) queryParams.Add($"isArchived={isArchived.Value.ToString().ToLower()}");
        if (isTrashed.HasValue) queryParams.Add($"isTrashed={isTrashed.Value.ToString().ToLower()}");

        var url = queryParams.Count > 0
            ? $"api/assets?{string.Join("&", queryParams)}"
            : "api/assets";

        return await GetAsync<List<Asset>>(url, cancellationToken).ConfigureAwait(false) ?? [];
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
        try
        {
            var response = await _httpClient.PutAsJsonAsync("api/assets", request, JsonOptions, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bulk update assets");
            return false;
        }
    }

    /// <summary>
    /// Deletes assets.
    /// </summary>
    public async Task<bool> DeleteAssetsAsync(string[] ids, bool force = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, "api/assets")
            {
                Content = JsonContent.Create(new { ids, force }, options: JsonOptions)
            };
            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete assets");
            return false;
        }
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
        string deviceAssetId,
        DateTime deviceModifiedAt,
        bool? isFavorite = null,
        bool? isArchived = null,
        bool? isVisible = null,
        string? duration = null,
        CancellationToken cancellationToken = default)
    {
        using var formContent = new MultipartFormDataContent();
        var fileStreamContent = new ByteArrayContent(fileContent);
        formContent.Add(fileStreamContent, "assetData", fileName);
        formContent.Add(new StringContent(deviceAssetId), "deviceAssetId");
        formContent.Add(new StringContent(deviceModifiedAt.ToString("O")), "deviceModifiedAt");
        formContent.Add(new StringContent(DateTime.UtcNow.ToString("O")), "fileCreatedAt");
        formContent.Add(new StringContent(DateTime.UtcNow.ToString("O")), "fileModifiedAt");

        if (isFavorite.HasValue)
            formContent.Add(new StringContent(isFavorite.Value.ToString().ToLower()), "isFavorite");
        if (isArchived.HasValue)
            formContent.Add(new StringContent(isArchived.Value.ToString().ToLower()), "isArchived");
        if (isVisible.HasValue)
            formContent.Add(new StringContent(isVisible.Value.ToString().ToLower()), "isVisible");
        if (!string.IsNullOrEmpty(duration))
            formContent.Add(new StringContent(duration), "duration");

        try
        {
            var response = await _httpClient.PostAsync("api/assets", formContent, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<Asset>(JsonOptions, cancellationToken).ConfigureAwait(false);
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
        var deviceAssetId = $"{fileName}-{fileInfo.Length}";

        _logger.LogInformation("Starting upload of {FileName} ({Size:N0} bytes)", fileName, fileInfo.Length);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
                var asset = await UploadAssetAsync(
                    fileBytes,
                    fileName,
                    deviceAssetId,
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

    #endregion

    #region Search

    /// <summary>
    /// Performs a metadata search.
    /// </summary>
    public async Task<SearchAssetResult<Asset>> SearchMetadataAsync(
        MetadataSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/search/metadata", request, JsonOptions, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SearchResult<Asset>>(JsonOptions, cancellationToken).ConfigureAwait(false);
                return result?.Assets ?? new SearchAssetResult<Asset>();
            }

            await LogErrorResponse(response, "POST", "api/search/metadata").ConfigureAwait(false);
            return new SearchAssetResult<Asset>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Metadata search failed");
            return new SearchAssetResult<Asset>();
        }
    }

    /// <summary>
    /// Performs a smart (ML/CLIP) search.
    /// </summary>
    public async Task<SearchAssetResult<Asset>> SearchSmartAsync(
        SmartSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/search/smart", request, JsonOptions, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SearchResult<Asset>>(JsonOptions, cancellationToken).ConfigureAwait(false);
                return result?.Assets ?? new SearchAssetResult<Asset>();
            }

            await LogErrorResponse(response, "POST", "api/search/smart").ConfigureAwait(false);
            return new SearchAssetResult<Asset>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Smart search failed");
            return new SearchAssetResult<Asset>();
        }
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
        string? assetId = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();

        if (shared.HasValue) queryParams.Add($"shared={shared.Value.ToString().ToLower()}");
        if (!string.IsNullOrEmpty(assetId)) queryParams.Add($"assetId={assetId}");

        var url = queryParams.Count > 0
            ? $"api/albums?{string.Join("&", queryParams)}"
            : "api/albums";

        return await GetAsync<List<Album>>(url, cancellationToken).ConfigureAwait(false) ?? [];
    }

    /// <summary>
    /// Gets an album by ID.
    /// </summary>
    public async Task<Album?> GetAlbumAsync(string id, bool? withoutAssets = null, CancellationToken cancellationToken = default)
    {
        var url = withoutAssets.HasValue
            ? $"api/albums/{id}?withoutAssets={withoutAssets.Value.ToString().ToLower()}"
            : $"api/albums/{id}";

        return await GetAsync<Album>(url, cancellationToken).ConfigureAwait(false);
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
        try
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

            await LogErrorResponse(response, "DELETE", $"api/albums/{albumId}/assets").ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove assets from album");
            return null;
        }
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
    public async Task<PeopleResponse?> GetPeopleAsync(bool? withHidden = null, CancellationToken cancellationToken = default)
    {
        var url = withHidden.HasValue
            ? $"api/people?withHidden={withHidden.Value.ToString().ToLower()}"
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
    /// Gets assets for a person.
    /// </summary>
    public async Task<List<Asset>> GetPersonAssetsAsync(string personId, CancellationToken cancellationToken = default)
    {
        return await GetAsync<List<Asset>>($"api/people/{personId}/assets", cancellationToken).ConfigureAwait(false) ?? [];
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
        try
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

            await LogErrorResponse(response, "DELETE", $"api/tags/{tagId}/assets").ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to untag assets");
            return null;
        }
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
    public async Task<List<BulkIdResponse>?> AddAssetsToSharedLinkAsync(string linkId, string[] assetIds, CancellationToken cancellationToken = default)
    {
        var request = new { ids = assetIds };
        return await PutAsync<List<BulkIdResponse>>($"api/shared-links/{linkId}/assets", request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes assets from a shared link.
    /// </summary>
    public async Task<List<BulkIdResponse>?> RemoveAssetsFromSharedLinkAsync(string linkId, string[] assetIds, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, $"api/shared-links/{linkId}/assets")
            {
                Content = JsonContent.Create(new { ids = assetIds }, options: JsonOptions)
            };
            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<List<BulkIdResponse>>(JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            await LogErrorResponse(response, "DELETE", $"api/shared-links/{linkId}/assets").ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove assets from shared link");
            return null;
        }
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

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            await LogErrorResponse(response, "GET", url).ConfigureAwait(false);
            return default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET request failed: {Url}", url);
            return default;
        }
    }

    private async Task<T?> PostAsync<T>(string url, object request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, request, JsonOptions, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            await LogErrorResponse(response, "POST", url).ConfigureAwait(false);
            return default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST request failed: {Url}", url);
            return default;
        }
    }

    private async Task<T?> PutAsync<T>(string url, object request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync(url, request, JsonOptions, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            await LogErrorResponse(response, "PUT", url).ConfigureAwait(false);
            return default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PUT request failed: {Url}", url);
            return default;
        }
    }

    private async Task<T?> PatchAsync<T>(string url, object request, CancellationToken cancellationToken)
    {
        try
        {
            var content = JsonContent.Create(request, options: JsonOptions);
            var response = await _httpClient.PatchAsync(url, content, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            await LogErrorResponse(response, "PATCH", url).ConfigureAwait(false);
            return default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PATCH request failed: {Url}", url);
            return default;
        }
    }

    private async Task<bool> DeleteAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.DeleteAsync(url, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent)
            {
                return true;
            }

            await LogErrorResponse(response, "DELETE", url).ConfigureAwait(false);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DELETE request failed: {Url}", url);
            return false;
        }
    }

    private async Task LogErrorResponse(HttpResponseMessage response, string method, string url)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        _logger.LogError("{Method} {Url} failed with {StatusCode}: {Body}",
            method, url, (int)response.StatusCode, body);
    }

    #endregion
}

/// <summary>
/// Response for bulk ID operations.
/// </summary>
public record BulkIdResponse
{
    public string Id { get; init; } = string.Empty;
    public bool Success { get; init; }
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
    public bool Import { get; init; }
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
}
