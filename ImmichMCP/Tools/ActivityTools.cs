using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ImmichMCP.Client;
using ImmichMCP.Models.Common;
using ImmichMCP.Models.Activities;

namespace ImmichMCP.Tools;

/// <summary>
/// MCP tools for activity operations (comments and likes).
/// </summary>
[McpServerToolType]
public static class ActivityTools
{
    [McpServerTool(Name = "immich.activities.list")]
    [Description("List activities (comments and likes) for an album or asset.")]
    public static async Task<string> List(
        ImmichClient client,
        [Description("Album ID (UUID) - required")] string albumId,
        [Description("Asset ID (UUID) - optional, to filter to a specific asset")] string? assetId = null,
        [Description("Activity type: comment or like")] string? type = null,
        [Description("Level: album or asset")] string? level = null)
    {
        if (string.IsNullOrWhiteSpace(albumId))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "Album ID is required",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var activities = await client.GetActivitiesAsync(albumId, assetId, type, level).ConfigureAwait(false);

        var summaries = activities.Select(ActivitySummary.FromActivity).ToList();

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

    [McpServerTool(Name = "immich.activities.create")]
    [Description("Create a comment or like on an album or asset.")]
    public static async Task<string> Create(
        ImmichClient client,
        [Description("Album ID (UUID)")] string albumId,
        [Description("Activity type: comment or like")] string type = "comment",
        [Description("Asset ID (UUID) - optional, for asset-level activity")] string? assetId = null,
        [Description("Comment text (required for comment type)")] string? comment = null)
    {
        if (string.IsNullOrWhiteSpace(albumId))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "Album ID is required",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var lowerType = type.ToLowerInvariant();
        if (lowerType != "comment" && lowerType != "like")
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "Type must be 'comment' or 'like'",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        if (lowerType == "comment" && string.IsNullOrWhiteSpace(comment))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "Comment text is required for comment type",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var request = new ActivityCreateRequest
        {
            AlbumId = albumId,
            AssetId = assetId,
            Type = lowerType,
            Comment = lowerType == "comment" ? comment : null
        };

        var activity = await client.CreateActivityAsync(request).ConfigureAwait(false);

        if (activity == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to create activity",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new
            {
                id = activity.Id,
                type = activity.Type,
                comment = activity.Comment,
                album_id = albumId,
                asset_id = activity.AssetId,
                created_at = activity.CreatedAt,
                user = activity.User.Name
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.activities.delete")]
    [Description("Delete an activity (comment or like).")]
    public static async Task<string> Delete(
        ImmichClient client,
        [Description("Activity ID (UUID)")] string id,
        [Description("Must be true to confirm deletion")] bool confirm = false)
    {
        if (!confirm)
        {
            var dryRunResponse = McpErrorResponse.Create(
                ErrorCodes.ConfirmationRequired,
                "Deletion requires confirm=true.",
                new { activity_id = id },
                new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(dryRunResponse);
        }

        var success = await client.DeleteActivityAsync(id).ConfigureAwait(false);

        if (!success)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                $"Failed to delete activity with ID {id}",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new { deleted = true, activity_id = id },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.activities.statistics")]
    [Description("Get activity statistics (comment count) for an album or asset.")]
    public static async Task<string> Statistics(
        ImmichClient client,
        [Description("Album ID (UUID)")] string albumId,
        [Description("Asset ID (UUID) - optional")] string? assetId = null)
    {
        if (string.IsNullOrWhiteSpace(albumId))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "Album ID is required",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var stats = await client.GetActivityStatisticsAsync(albumId, assetId).ConfigureAwait(false);

        if (stats == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to retrieve activity statistics",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new
            {
                album_id = albumId,
                asset_id = assetId,
                comments = stats.Comments
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }
}
