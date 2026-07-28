using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ImmichMCP.Client;
using ImmichMCP.Configuration;
using ImmichMCP.Services;
using ImmichMCP.Tools;

namespace ImmichMCP.Tests.Integration;

/// <summary>
/// Exercises every one of the 49 MCP tools against a LIVE Immich instance.
///
/// SAFETY CONTRACT (do not weaken): this test never creates, mutates, or deletes any
/// pre-existing library data. Every mutation runs on throwaway fixtures created by the
/// test itself (two tiny uploaded PNGs, one album, one tag, shared links, one activity)
/// and the finally-block deletes ONLY those tracked fixture IDs. The two tools that would
/// mutate real, un-creatable data — immich_people_update and immich_people_merge — are
/// exercised ONLY with bogus UUIDs, verifying the tool path + graceful error handling
/// without ever touching a real person.
/// </summary>
[Trait("Category", "Integration")]
public class ToolCoverageIntegrationTests
{
    private const string Bogus = "00000000-0000-0000-0000-000000000000";
    private const string Bogus2 = "11111111-1111-1111-1111-111111111111";

    // Two DISTINCT tiny PNGs -> distinct checksums -> distinct assets (Immich dedups by content).
    private static readonly byte[] Png1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
    private static readonly byte[] Png2 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAQAAADQ2CvWAAAADklEQVR42mP8z8BQz0AEAAoBAf3B5eQAAAAASUVORK5CYII=");

    private static readonly string[] AllToolNames =
    {
        "immich_ping", "immich_capabilities",
        "immich_search_metadata", "immich_search_smart", "immich_search_ocr", "immich_search_explore",
        "immich_assets_statistics", "immich_assets_list", "immich_assets_get", "immich_assets_exif",
        "immich_assets_download_original", "immich_assets_download_thumbnail", "immich_assets_upload",
        "immich_assets_upload_from_path", "immich_assets_upload_init", "immich_assets_upload_status",
        "immich_assets_update", "immich_assets_bulk_update", "immich_assets_delete", "immich_assets_upload_authorize",
        "immich_albums_list", "immich_albums_get", "immich_albums_create", "immich_albums_update",
        "immich_albums_assets_add", "immich_albums_assets_remove", "immich_albums_delete", "immich_albums_statistics",
        "immich_people_list", "immich_people_get", "immich_people_update", "immich_people_merge", "immich_people_assets",
        "immich_tags_list", "immich_tags_get", "immich_tags_create", "immich_tags_update",
        "immich_tags_delete", "immich_tags_assets_add", "immich_tags_assets_remove",
        "immich_shared_links_list", "immich_shared_links_get", "immich_shared_links_create",
        "immich_shared_links_update", "immich_shared_links_delete",
        "immich_activities_list", "immich_activities_create", "immich_activities_delete", "immich_activities_statistics",
    };

    [MutationIntegrationFact]
    public async Task AllTools_ExercisedSafely_AgainstLiveImmich()
    {
        AllToolNames.Should().HaveCount(49, "the fixed tool list must cover all 49 tools");

        var settings = IntegrationTestSettings.Load();
        var client = settings.CreateClient();
        var options = Options.Create(new ImmichOptions
        {
            BaseUrl = settings.BaseUrl,
            ApiKey = settings.ApiKey,
            MaxPageSize = 100,
            DownloadMode = "url"
        });
        var uploadTools = new UploadTools(new UploadSessionService(), options);

        // Pre-seed every tool as SKIP; each is overwritten as it runs. A leftover SKIP means a
        // fixture it depended on failed — surfaced in the final report instead of hidden.
        var status = AllToolNames.ToDictionary(t => t, _ => ("SKIP", (string?)null));
        void Set(string tool, string s, string? note) => status[tool] = (s, note);

        // Fixtures created by THIS test. Teardown deletes ONLY these.
        var createdAssets = new List<string>();
        var createdLinks = new List<string>();
        string? albumId = null;
        string? tagId = null;

        static string Trunc(string s) => s.Length > 180 ? s[..180] + "…" : s;

        static string FirstText(CallToolResult result) =>
            result.Content.OfType<TextContentBlock>().First().Text;

        async Task<JsonElement?> Ok(string name, Func<Task<string>> call, Func<JsonElement, bool>? assert = null)
        {
            try
            {
                var json = await call();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!(root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True))
                {
                    Set(name, "FAIL", Trunc(json));
                    return null;
                }
                JsonElement? res = root.TryGetProperty("result", out var r) ? r.Clone() : null;
                if (assert != null && (res is null || !assert(res.Value)))
                {
                    Set(name, "FAIL", "assertion failed: " + Trunc(json));
                    return null;
                }
                Set(name, "PASS", null);
                return res;
            }
            catch (Exception ex)
            {
                Set(name, "FAIL", ex.Message);
                return null;
            }
        }

        // For destructive-on-real-data tools invoked with bogus IDs: PASS if the tool refuses
        // safely — either an in-band ok:false OR a thrown ImmichApiException (which the gateway
        // boundary turns into an isError result). Either way, no real data was touched.
        async Task SafeReject(string name, Func<Task<string>> call)
        {
            try
            {
                var json = await call();
                using var doc = JsonDocument.Parse(json);
                var ok = doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
                Set(name, ok ? "FAIL" : "PASS", ok ? "expected refusal, got ok:true" : "structured error (safe)");
            }
            catch (ImmichApiException ex)
            {
                Set(name, "PASS", $"rejected upstream HTTP {(int)ex.StatusCode} — gateway surfaces as isError (safe)");
            }
            catch (Exception ex)
            {
                Set(name, "FAIL", "unexpected exception type: " + ex.Message);
            }
        }

        // Passes on ok:true OR a graceful error — for optional server features (e.g. OCR).
        async Task Tolerant(string name, Func<Task<string>> call)
        {
            try
            {
                var json = await call();
                using var doc = JsonDocument.Parse(json);
                var ok = doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
                Set(name, "PASS", ok ? null : "graceful (feature may be disabled)");
            }
            catch (Exception ex)
            {
                Set(name, "FAIL", ex.Message);
            }
        }

        static string? FindId(JsonElement? res, params string[] names)
        {
            if (res is not { } el || el.ValueKind != JsonValueKind.Object) return null;
            foreach (var n in names)
                if (el.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String)
                    return p.GetString();
            return null;
        }

        static string? FirstNestedId(JsonElement? res)
        {
            if (res is not { } el) return null;
            JsonElement arr = default;
            var have = false;
            if (el.ValueKind == JsonValueKind.Array) { arr = el; have = true; }
            else if (el.ValueKind == JsonValueKind.Object)
                foreach (var key in new[] { "people", "items", "assets" })
                    if (el.TryGetProperty(key, out var a) && a.ValueKind == JsonValueKind.Array) { arr = a; have = true; break; }
            if (!have) return null;
            foreach (var item in arr.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var idp))
                    return idp.GetString();
            return null;
        }

        try
        {
            // ===================== HEALTH (2) =====================
            await Ok("immich_ping", () => HealthTools.Ping(client));
            await Ok("immich_capabilities", () => HealthTools.GetCapabilities(client));

            // ===================== SEARCH (4) — read only =====================
            await Ok("immich_search_metadata", () => SearchTools.MetadataSearch(client, size: 1));
            await Ok("immich_search_smart", () => SearchTools.SmartSearch(client, "a photograph", size: 1));
            await Tolerant("immich_search_ocr", () => SearchTools.OcrSearch(client, "the", size: 1));
            await Ok("immich_search_explore", () => SearchTools.Explore(client));

            // ===================== read-only lists / stats =====================
            await Ok("immich_assets_statistics", () => AssetTools.Statistics(client));
            await Ok("immich_assets_list", () => AssetTools.List(client, size: 1));
            await Ok("immich_albums_statistics", () => AlbumTools.Statistics(client));
            await Ok("immich_albums_list", () => AlbumTools.List(client));
            await Ok("immich_tags_list", () => TagTools.List(client));
            await Ok("immich_shared_links_list", () => SharedLinkTools.List(client));

            // ===================== PEOPLE (5) — reads real (read-only), mutations bogus-only =====================
            var peopleRes = await Ok("immich_people_list", () => PeopleTools.List(client, size: 1));
            var realPerson = FirstNestedId(peopleRes);
            if (realPerson is not null)
            {
                await Ok("immich_people_get", () => PeopleTools.Get(client, realPerson));
                await Ok("immich_people_assets", () => PeopleTools.Assets(client, realPerson, size: 1));
            }
            else
            {
                await SafeReject("immich_people_get", () => PeopleTools.Get(client, Bogus));
                await SafeReject("immich_people_assets", () => PeopleTools.Assets(client, Bogus, size: 1));
            }
            // SAFETY: bogus IDs only — never mutate a real person. The tool must refuse (in-band
            // error or thrown/gateway-isError), never silently succeed.
            await SafeReject("immich_people_update", () => PeopleTools.Update(client, Bogus, name: "mcp-tool-test-should-not-persist"));
            await SafeReject("immich_people_merge", () => PeopleTools.Merge(client, Bogus, Bogus2, confirm: true));

            // ===================== ASSETS create fixtures (2 distinct) =====================
            var a1 = await Ok("immich_assets_upload",
                () => AssetTools.Upload(client, Convert.ToBase64String(Png1), $"mcp-tool-test-1-{Guid.NewGuid():N}.png", isArchived: true),
                r => r.TryGetProperty("asset_id", out _));
            var asset1 = FindId(a1, "asset_id");
            if (asset1 is not null) createdAssets.Add(asset1);

            var tmp = Path.Combine(Path.GetTempPath(), $"mcp-tool-test-2-{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(tmp, Png2);
            string? asset2 = null;
            try
            {
                var a2 = await Ok("immich_assets_upload_from_path",
                    () => AssetTools.UploadFromPath(client, tmp, isArchived: true),
                    r => r.TryGetProperty("asset_id", out _));
                asset2 = FindId(a2, "asset_id");
                if (asset2 is not null) createdAssets.Add(asset2);
            }
            finally { try { File.Delete(tmp); } catch { /* ignore */ } }

            // out-of-band upload session tools (no Immich data touched)
            var initRes = await Ok("immich_assets_upload_init", () => Task.FromResult(uploadTools.UploadInit(client, "mcp-tool-test.png")),
                r => r.TryGetProperty("session_id", out _));
            var sessionId = FindId(initRes, "session_id");
            await Ok("immich_assets_upload_status", () => Task.FromResult(uploadTools.UploadStatus(client, sessionId ?? Bogus)));

            // per-asset reads + updates (on our fixture only)
            if (asset1 is not null)
            {
                await Ok("immich_assets_get", () => AssetTools.Get(client, asset1));
                await Ok("immich_assets_exif", () => AssetTools.GetExif(client, asset1));
                await Ok("immich_assets_download_original", async () => FirstText(await AssetTools.DownloadOriginal(client, asset1)));
                await Ok("immich_assets_download_thumbnail", async () => FirstText(await AssetTools.DownloadThumbnail(client, asset1)));
                await Ok("immich_assets_update", () => AssetTools.Update(client, asset1, isFavorite: true, description: "mcp tool test"));
                await Ok("immich_assets_bulk_update", () => AssetTools.BulkUpdate(client, asset1, isFavorite: false, dryRun: false, confirm: true));
            }

            // ===================== ALBUMS (8) =====================
            var albRes = await Ok("immich_albums_create",
                () => AlbumTools.Create(client, $"mcp-tool-test-album-{Guid.NewGuid():N}", "safe to delete"),
                r => r.TryGetProperty("id", out _));
            albumId = FindId(albRes, "id");
            if (albumId is not null)
            {
                await Ok("immich_albums_get", () => AlbumTools.Get(client, albumId));
                await Ok("immich_albums_update", () => AlbumTools.Update(client, albumId, description: "updated", isActivityEnabled: true));
                var addIds = string.Join(",", createdAssets);
                if (addIds.Length > 0)
                    await Ok("immich_albums_assets_add", () => AlbumTools.AddAssets(client, albumId, addIds));
                if (asset2 is not null)
                    await Ok("immich_albums_assets_remove", () => AlbumTools.RemoveAssets(client, albumId, asset2));

                var authRes = await Ok("immich_assets_upload_authorize",
                    () => AssetTools.AuthorizeUpload(client, albumId: albumId),
                    r => r.TryGetProperty("upload_url", out _));
                var authLink = FindId(authRes, "shared_link_id");
                if (authLink is not null) createdLinks.Add(authLink);
            }

            // ===================== TAGS (7) =====================
            var tagRes = await Ok("immich_tags_create",
                () => TagTools.Create(client, $"mcp-tool-test-tag-{Guid.NewGuid():N}"),
                r => r.TryGetProperty("id", out _));
            tagId = FindId(tagRes, "id");
            if (tagId is not null)
            {
                await Ok("immich_tags_get", () => TagTools.Get(client, tagId));
                await Ok("immich_tags_update", () => TagTools.Update(client, tagId, color: "#ff8800"));
                if (asset1 is not null)
                {
                    await Ok("immich_tags_assets_add", () => TagTools.TagAssets(client, tagId, asset1));
                    await Ok("immich_tags_assets_remove", () => TagTools.UntagAssets(client, tagId, asset1));
                }
                // exercise the delete TOOL (also cleans up the tag)
                var tagDel = await Ok("immich_tags_delete", () => TagTools.Delete(client, tagId, confirm: true));
                if (tagDel is not null) tagId = null; // deleted via tool; teardown must not repeat
            }

            // ===================== SHARED LINKS (5) =====================
            if (albumId is not null)
            {
                var linkRes = await Ok("immich_shared_links_create",
                    () => SharedLinkTools.Create(client, type: "ALBUM", albumId: albumId, allowUpload: true, expiresAt: "2035-01-01T00:00:00.000Z"),
                    r => r.TryGetProperty("id", out _));
                var linkId = FindId(linkRes, "id");
                if (linkId is not null)
                {
                    await Ok("immich_shared_links_get", () => SharedLinkTools.Get(client, linkId));
                    await Ok("immich_shared_links_update", () => SharedLinkTools.Update(client, linkId, description: "updated"));
                    await Ok("immich_shared_links_delete", () => SharedLinkTools.Delete(client, linkId, confirm: true));
                }
            }

            // ===================== ACTIVITIES (4) =====================
            if (albumId is not null)
            {
                var actRes = await Ok("immich_activities_create",
                    () => ActivityTools.Create(client, albumId, type: "comment", comment: "mcp tool test"),
                    r => r.TryGetProperty("id", out _));
                var activityId = FindId(actRes, "id");
                await Ok("immich_activities_list", () => ActivityTools.List(client, albumId));
                await Ok("immich_activities_statistics", () => ActivityTools.Statistics(client, albumId));
                if (activityId is not null)
                    await Ok("immich_activities_delete", () => ActivityTools.Delete(client, activityId, confirm: true));
            }

            // ===================== ASSETS delete tool (on a fixture) =====================
            if (asset2 is not null)
            {
                var del = await Ok("immich_assets_delete",
                    () => AssetTools.DeleteAssets(client, asset2, force: true, confirm: true));
                if (del is not null) createdAssets.Remove(asset2); // deleted via the tool
            }

            // ===================== ALBUMS delete tool (last — after all album-dependent tools) =====================
            if (albumId is not null)
            {
                var albDel = await Ok("immich_albums_delete", () => AlbumTools.Delete(client, albumId, confirm: true));
                if (albDel is not null) albumId = null; // deleted via tool; teardown must not repeat
            }
        }
        finally
        {
            // TEARDOWN — delete ONLY fixtures this test created. Best-effort; never touches existing data.
            foreach (var link in createdLinks)
                try { await client.DeleteSharedLinkAsync(link); } catch { /* already gone */ }
            if (tagId is not null)
                try { await client.DeleteTagAsync(tagId); } catch { /* ignore */ }
            if (albumId is not null)
                try { await client.DeleteAlbumAsync(albumId); } catch { /* ignore */ }
            if (createdAssets.Count > 0)
                try { await client.DeleteAssetsAsync(createdAssets.ToArray(), force: true); } catch { /* ignore */ }
        }

        // ===================== report =====================
        var pass = status.Count(kv => kv.Value.Item1 == "PASS");
        var report = string.Join("\n", AllToolNames.Select(t =>
            $"  {status[t].Item1,-4} {t}{(status[t].Item2 is null ? "" : "  — " + status[t].Item2)}"));
        var notPassing = status.Where(kv => kv.Value.Item1 != "PASS").Select(kv => kv.Key).ToList();

        notPassing.Should().BeEmpty(
            $"every tool must PASS. {pass}/49 passed.\n{report}");
    }
}
