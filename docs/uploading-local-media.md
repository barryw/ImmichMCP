# Uploading local media to Immich

The ImmichMCP server is typically **remote** and cannot read the client's disk. So it never
handles file bytes. Instead, `immich_assets_upload_authorize` mints a short-lived, **upload-only**
credential and the client uploads **directly to Immich** with `curl` it already has. The master
API key never leaves the server, and nothing needs to be installed.

This is the same separation Immich's own CLI uses; here it's exposed as a single MCP tool so any
agent (Claude Code, Codex, …) can drive it.

## Flow

1. **Authorize** — one MCP call:

   ```
   immich_assets_upload_authorize(album_name: "Iceland 2026", ttl_minutes: 120)
   ```

   Returns `upload_url` (contains `?key=<token>`), `album_id`, `shared_link_id`, `expires_at`.
   Pass `album_id` instead of `album_name` to upload into an existing album.

2. **Upload** each file to `upload_url` — no API key, required multipart fields `assetData`,
   `fileCreatedAt`, `fileModifiedAt` (ISO-8601 **with a `Z`**):

   ```bash
   URL='<upload_url>'
   TS=$(date -u +%Y-%m-%dT%H:%M:%S.000Z)   # fallback; EXIF capture date wins for real photos
   # Upload ONLY real images/videos. Guard by actual CONTENT TYPE (file --mime-type), not by
   # extension — so junk that merely looks like media is skipped: macOS AppleDouble files
   # (._foo.jpg are application/octet-stream), .DS_Store, sidecars, mislabeled files.
   # -not -name '.*' drops every dotfile (covers ._* and .DS_Store); the mime check is the
   # real guarantee.
   find "<path>" -type f -not -name '.*' -print0 \
   | xargs -0 -P 8 -I{} sh -c '
       f="$1"; url="$2"; ts="$3"
       case "$(file --mime-type -b "$f")" in
         image/*|video/*) ;;
         *) echo "skip (not media)  $(basename "$f")"; exit 0 ;;
       esac
       code=$(curl --retry 3 -s -o /dev/null -w "%{http_code}" -X POST "$url" \
         -F "assetData=@$f" -F "deviceId=mcp-client" -F "deviceAssetId=$f" \
         -F "fileCreatedAt=$ts" -F "fileModifiedAt=$ts")
       echo "$code  $(basename "$f")"
     ' _ {} "$URL" "$TS"
   ```

   `-P 8` runs 8 concurrent uploads. `201`=created, `200`=duplicate (already there),
   `skip`=non-media, anything else=failed. Content-type gating is what makes it
   "images and videos, nothing else."

3. **Revoke** when done (optional): `immich_shared_links_delete(id: <shared_link_id>)`.

## Resumability

Immich deduplicates by file **content**, so re-running the same command is safe — already-uploaded
files return `duplicate` and are not re-added. An interrupted run just needs to be re-run; it
converges.

Caveat: the share token cannot call `bulk-upload-check`, so a resume re-sends bytes for
already-uploaded files (rejected as duplicates server-side). Correct, but not bandwidth-optimal.
For a very large re-run, keep a local list of files that returned `201`/`200` and skip them.

## Expiry

If a long import outlives `ttl_minutes`, uploads start returning `401`. Call
`immich_assets_upload_authorize` again for the **same album** (`album_id`) to get a fresh
`upload_url` and continue.

> Never send file bytes through MCP tool calls (base64) — it destroys the context window. Bytes go
> client→Immich directly. That is the entire purpose of the authorize tool.
