using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using ImmichMCP.Client;
using ImmichMCP.Configuration;
using ImmichMCP.Models.Common;
using ImmichMCP.Services;

namespace ImmichMCP.Tools;

/// <summary>
/// MCP tools for out-of-band file uploads.
/// </summary>
[McpServerToolType]
public class UploadTools
{
    private readonly UploadSessionService _uploadService;
    private readonly ImmichOptions _options;

    public UploadTools(UploadSessionService uploadService, IOptions<ImmichOptions> options)
    {
        _uploadService = uploadService;
        _options = options.Value;
    }

    [McpServerTool(Name = "immich.assets.upload_init")]
    [Description("Initialize an out-of-band file upload. Returns an upload URL where you can POST the file directly. Use this for uploading files when the MCP server cannot access the local filesystem.")]
    public string UploadInit(
        ImmichClient client,
        [Description("Suggested filename (optional, can be overridden in upload)")] string? fileName = null,
        [Description("Mark as favorite (default: false)")] bool? isFavorite = null,
        [Description("Mark as archived (default: false)")] bool? isArchived = null)
    {
        var session = _uploadService.CreateSession(fileName, isFavorite, isArchived);

        // Build the upload URL - use the MCP server's own address
        var mcpBaseUrl = Environment.GetEnvironmentVariable("MCP_PUBLIC_URL")
                         ?? Environment.GetEnvironmentVariable("MCP_BASE_URL")
                         ?? "http://localhost:5000";

        var uploadUrl = $"{mcpBaseUrl.TrimEnd('/')}/upload/{session.SessionId}";

        var response = McpResponse<object>.Success(
            new
            {
                session_id = session.SessionId,
                upload_url = uploadUrl,
                expires_at = session.ExpiresAt.ToString("O"),
                instructions = new
                {
                    method = "POST",
                    content_type = "multipart/form-data",
                    form_field = "file",
                    example_curl = $"curl -X POST -F \"file=@/path/to/image.jpg\" \"{uploadUrl}\""
                }
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.assets.upload_status")]
    [Description("Check the status of an upload session.")]
    public string UploadStatus(
        ImmichClient client,
        [Description("Upload session ID")] string sessionId)
    {
        var session = _uploadService.GetSession(sessionId);

        if (session == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Upload session {sessionId} not found",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new
            {
                session_id = session.SessionId,
                status = session.Status.ToString().ToLower(),
                asset_id = session.AssetId,
                error = session.Error,
                created_at = session.CreatedAt.ToString("O"),
                expires_at = session.ExpiresAt.ToString("O")
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }
}
