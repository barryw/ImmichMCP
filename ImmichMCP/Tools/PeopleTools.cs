using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ImmichMCP.Client;
using ImmichMCP.Models.Common;
using ImmichMCP.Models.People;
using ImmichMCP.Models.Assets;
using static ImmichMCP.Utils.ParsingHelpers;

namespace ImmichMCP.Tools;

/// <summary>
/// MCP tools for people/face recognition operations.
/// </summary>
[McpServerToolType]
public static class PeopleTools
{
    [McpServerTool(Name = "immich.people.list")]
    [Description("List all recognized people in the library.")]
    public static async Task<string> List(
        ImmichClient client,
        [Description("Include hidden people")] bool withHidden = false)
    {
        var result = await client.GetPeopleAsync(withHidden).ConfigureAwait(false);

        if (result == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to retrieve people",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var summaries = result.People.Select(p => PersonSummary.FromPerson(p)).ToList();

        var response = McpResponse<object>.Success(
            new
            {
                people = summaries,
                total = result.Total,
                visible = result.Visible,
                hidden = result.Hidden
            },
            new McpMeta
            {
                Total = result.Total,
                ImmichBaseUrl = client.BaseUrl
            }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.people.get")]
    [Description("Get person details by ID.")]
    public static async Task<string> Get(
        ImmichClient client,
        [Description("Person ID (UUID)")] string id)
    {
        var person = await client.GetPersonAsync(id).ConfigureAwait(false);

        if (person == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Person with ID {id} not found",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Person>.Success(
            person,
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.people.update")]
    [Description("Update person information (name, birth date, hidden status).")]
    public static async Task<string> Update(
        ImmichClient client,
        [Description("Person ID (UUID)")] string id,
        [Description("Person's name")] string? name = null,
        [Description("Birth date (YYYY-MM-DD)")] string? birthDate = null,
        [Description("Hide this person from views")] bool? isHidden = null)
    {
        DateOnly? parsedBirthDate = null;
        if (!string.IsNullOrEmpty(birthDate) && DateOnly.TryParse(birthDate, out var bd))
        {
            parsedBirthDate = bd;
        }

        var request = new PersonUpdateRequest
        {
            Name = name,
            BirthDate = parsedBirthDate,
            IsHidden = isHidden
        };

        var person = await client.UpdatePersonAsync(id, request).ConfigureAwait(false);

        if (person == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.NotFound,
                $"Person with ID {id} not found or update failed",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<Person>.Success(
            person,
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.people.merge")]
    [Description("Merge multiple people into one (for duplicate face clusters).")]
    public static async Task<string> Merge(
        ImmichClient client,
        [Description("Target person ID to merge into (UUID)")] string targetId,
        [Description("Source person IDs to merge from (comma-separated UUIDs)")] string sourceIds,
        [Description("Must be true to confirm merge")] bool confirm = false)
    {
        var ids = ParseStringArray(sourceIds);

        if (ids == null || ids.Length == 0)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.Validation,
                "No valid source person IDs provided",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        if (!confirm)
        {
            var targetPerson = await client.GetPersonAsync(targetId).ConfigureAwait(false);
            var sourcePeople = new List<object>();

            foreach (var id in ids.Take(5))
            {
                var p = await client.GetPersonAsync(id).ConfigureAwait(false);
                if (p != null)
                {
                    sourcePeople.Add(new { id = p.Id, name = p.Name });
                }
            }

            var dryRunResponse = McpErrorResponse.Create(
                ErrorCodes.ConfirmationRequired,
                "Merge requires confirm=true. This will merge the source people into the target person.",
                new
                {
                    target = targetPerson != null ? new { id = targetPerson.Id, name = targetPerson.Name } : null,
                    sources = sourcePeople,
                    source_count = ids.Length
                },
                new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(dryRunResponse);
        }

        var result = await client.MergePeopleAsync(targetId, ids).ConfigureAwait(false);

        if (result == null)
        {
            var errorResponse = McpErrorResponse.Create(
                ErrorCodes.UpstreamError,
                "Failed to merge people",
                meta: new McpMeta { ImmichBaseUrl = client.BaseUrl }
            );
            return JsonSerializer.Serialize(errorResponse);
        }

        var response = McpResponse<object>.Success(
            new
            {
                target_id = targetId,
                merged_count = ids.Length,
                results = result
            },
            new McpMeta { ImmichBaseUrl = client.BaseUrl }
        );
        return JsonSerializer.Serialize(response);
    }

    [McpServerTool(Name = "immich.people.assets")]
    [Description("List assets containing a specific person.")]
    public static async Task<string> Assets(
        ImmichClient client,
        [Description("Person ID (UUID)")] string personId)
    {
        var assets = await client.GetPersonAssetsAsync(personId).ConfigureAwait(false);

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
}
