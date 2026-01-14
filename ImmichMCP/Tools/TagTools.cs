using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ImmichMCP.Client;
using ImmichMCP.Models.Common;
using ImmichMCP.Models.Tags;
using static ImmichMCP.Utils.ParsingHelpers;

namespace ImmichMCP.Tools;

/// <summary>
/// MCP tools for tag operations.
/// </summary>
[McpServerToolType]
public static class TagTools
{
    [McpServerTool(Name = "immich.tags.list")]
    [Description("List all tags.")]
    public static async Task<string> List(ImmichClient client)
    {
        var tags = await client.GetTagsAsync().ConfigureAwait(false);

        var summaries = tags.Select(TagSummary.FromTag).ToList();

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

    [McpServerTool(Name = "immich.tags.get")]
    [Description("Get tag by ID.")]
    public static async Task<string> Get(
        ImmichClient client,
        [Description("Tag ID (UUID)")] string id)
    {
        var tag = await client.GetTagAsync(id).ConfigureAwait(false);

        if (tag == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Tag with ID {id} not found",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Tag>.Success(
            tag,
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.tags.create")]
    [Description("Create a new tag.")]
    public static async Task<string> Create(
        ImmichClient client,
        [Description("Tag name")] string name,
        [Description("Tag color (hex, e.g., '#ff0000')")] string? color = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "Tag name is required",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var request = new TagCreateRequest
        {
            Name = name,
            Color = color
        };

        var tag = await client.CreateTagAsync(request).ConfigureAwait(false);

        if (tag == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to create tag",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Tag>.Success(
            tag,
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.tags.update")]
    [Description("Update a tag.")]
    public static async Task<string> Update(
        ImmichClient client,
        [Description("Tag ID (UUID)")] string id,
        [Description("New tag name")] string? name = null,
        [Description("New tag color (hex, e.g., '#ff0000')")] string? color = null)
    {
        var request = new TagUpdateRequest
        {
            Name = name,
            Color = color
        };

        var tag = await client.UpdateTagAsync(id, request).ConfigureAwait(false);

        if (tag == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Tag with ID {id} not found or update failed",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Tag>.Success(
            tag,
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.tags.delete")]
    [Description("Delete a tag. Requires explicit confirmation.")]
    public static async Task<string> Delete(
        ImmichClient client,
        [Description("Tag ID (UUID)")] string id,
        [Description("Must be true to confirm deletion")] bool confirm = false)
    {
        if (!confirm)
        {
            var tag = await client.GetTagAsync(id).ConfigureAwait(false);

            if (tag == null)
            {
                var notFoundResponse = McpErrorResponse.Create(
                    ErrorCodes.NotFound,
                    $"Tag with ID {id} not found",
                    meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
                );
                return JsonSerializer.Serialize(notFoundResponse);
            }

            var dryRunResponse = McpErrorResponse.Create(
                ErrorCodes.ConfirmationRequired,
                "Deletion requires confirm=true. This is a dry run showing what would be deleted.",
                new
                {
                    tag_id = id,
                    name = tag.Name,
                    value = tag.Value
                },
                new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(dryRunResponse);
        }

        var success = await client.DeleteTagAsync(id).ConfigureAwait(false);

        if (!success)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                $"Failed to delete tag with ID {id}",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new { deleted = true, tag_id = id },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.tags.assets.add")]
    [Description("Tag assets with a specific tag.")]
    public static async Task<string> TagAssets(
        ImmichClient client,
        [Description("Tag ID (UUID)")] string tagId,
        [Description("Asset IDs to tag (comma-separated UUIDs)")] string assetIds)
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

        var result = await client.TagAssetsAsync(tagId, ids).ConfigureAwait(false);

        if (result == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to tag assets",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var successCount = result.Count(r => r.Success);
        var failedCount = result.Count(r => !r.Success);

        var response = McpResponse<object>.Success(
            new
            {
                tag_id = tagId,
                tagged = successCount,
                failed = failedCount,
                results = result
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.tags.assets.remove")]
    [Description("Remove tag from assets.")]
    public static async Task<string> UntagAssets(
        ImmichClient client,
        [Description("Tag ID (UUID)")] string tagId,
        [Description("Asset IDs to untag (comma-separated UUIDs)")] string assetIds)
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

        var result = await client.UntagAssetsAsync(tagId, ids).ConfigureAwait(false);

        if (result == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to untag assets",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var successCount = result.Count(r => r.Success);
        var failedCount = result.Count(r => !r.Success);

        var response = McpResponse<object>.Success(
            new
            {
                tag_id = tagId,
                untagged = successCount,
                failed = failedCount,
                results = result
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }
}
