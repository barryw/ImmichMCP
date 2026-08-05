# ImmichMCP

A Model Context Protocol (MCP) server for [Immich](https://immich.app/) - the self-hosted photo and video management solution. This server provides a first-class AI interface to manage your Immich library.

## Features

- **Asset Management**: Search, browse, upload, update, and delete photos/videos
- **Direct Local Upload**: Authorize a short-lived, upload-only URL and stream a local folder straight to Immich — no API key exposed, nothing to install beyond `curl`, resumable by content dedup
- **Smart Search**: ML-powered semantic search using CLIP (e.g., "sunset at the beach")
- **Metadata Search**: Filter by date, location, camera, people, and more
- **Albums**: Create, manage, and share photo albums
- **People**: View and manage face recognition clusters
- **Tags**: Organize assets with custom tags
- **Shared Links**: Create shareable URLs for albums and assets
- **Activities**: Add comments and likes to albums/assets

## Requirements

- .NET 10.0 SDK
- Immich v3.0 or newer server instance
- Immich API key

## Compatibility

ImmichMCP 3.x targets Immich v3 APIs. Use an older ImmichMCP release for Immich v2 servers.

## Integration Tests

Read-only integration tests can run against an existing Immich server without deploying ImmichMCP:

```bash
export IMMICH_BASE_URL="http://127.0.0.1:2283"
export IMMICH_API_KEY="your-api-key"
export IMMICH_INTEGRATION_TESTS=true
dotnet test ImmichMCP.Tests/ImmichMCP.Tests.csproj --filter "Category=Integration"
```

Mutation coverage (create/update/delete paths) is disabled by default. Enable it explicitly
to also run the full 49-tool smoke:

```bash
export IMMICH_INTEGRATION_MUTATION_TESTS=true
dotnet test ImmichMCP.Tests/ImmichMCP.Tests.csproj --filter "Category=Integration"
```

(If your Immich runs somewhere not directly reachable, point `IMMICH_BASE_URL` at it however
you normally reach it — e.g. a port-forward or tunnel — before running the tests.)

With mutation coverage enabled, `ToolCoverageIntegrationTests` exercises **all 49 tools**
against the live server. It is strictly non-destructive to existing data: every mutation
runs on throwaway fixtures the test creates (uploaded PNGs, an album, a tag, shared links,
an activity) and teardown deletes only those; the two tools that would mutate real,
un-creatable data (`immich_people_update`, `immich_people_merge`) are exercised with bogus
IDs only and must refuse safely.

## Deployment

ImmichMCP is published as a container image at `ghcr.io/barryw/immichmcp`. Run it wherever you
host containers — Docker, Docker Compose, or Kubernetes.

- Set `IMMICH_BASE_URL` and `IMMICH_API_KEY` (see [Environment Variables](#environment-variables)).
- Expose the HTTP port (default `5000`). The MCP endpoint is served at `/mcp`. Two health
  endpoints are available: `/health` (liveness, use for restarts) and `/health/ready`
  (readiness, pings Immich and returns `503` if unreachable, use for traffic routing).
- For remote/HTTP clients, set `IMMICH_TOOL_MODE=gateway` so clients enable tool categories on
  demand instead of loading all tools up front.

### Docker Compose

```bash
cp .env.example .env          # set IMMICH_BASE_URL and IMMICH_API_KEY
docker compose up --build
```

### Kubernetes

A sample manifest is provided in [`k8s/deployment.yaml`](k8s/deployment.yaml) — set the image,
the two environment variables, and `IMMICH_TOOL_MODE=gateway`, then apply it with `kubectl`.

## Installation

### Option 1: Run from Source

```bash
# Clone the repository
git clone https://github.com/barryw/ImmichMCP.git
cd ImmichMCP

# Set environment variables
export IMMICH_BASE_URL="https://photos.example.com"
export IMMICH_API_KEY="your-api-key"

# Run with stdio transport (for Claude Desktop)
dotnet run --project ImmichMCP -- --stdio

# Or run with HTTP transport (for remote usage)
dotnet run --project ImmichMCP
```

### Option 2: Docker

```bash
docker run -e IMMICH_BASE_URL="https://photos.example.com" \
           -e IMMICH_API_KEY="your-api-key" \
           -p 5000:5000 \
           ghcr.io/barryw/immichmcp:latest
```

## Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `IMMICH_BASE_URL` | Yes | - | Base URL of your Immich instance |
| `IMMICH_API_KEY` | Yes | - | API key for authentication |
| `MCP_LOG_LEVEL` | No | `Information` | Logging level |
| `DOWNLOAD_MODE` | No | `url` | `url` returns URLs, `base64` returns the file content inline as MCP image/resource content |
| `MAX_INLINE_DOWNLOAD_BYTES` | No | `26214400` | Max asset size returned inline with `DOWNLOAD_MODE=base64`; larger assets get a `PAYLOAD_TOO_LARGE` error that includes the download URL |
| `MAX_PAGE_SIZE` | No | `100` | Maximum items per page |
| `MCP_PORT` | No | `5000` | HTTP server port |
| `IMMICH_TOOL_MODE` | No | `static` | `static` exposes all tools; `gateway` exposes `immich_tools_list` and `immich_tools_enable` first |

In `gateway` mode, `immich_tools_enable` emits the MCP `notifications/tools/list_changed` notification so clients can refresh the normal `tools/list` inventory after enabling a category or tool.

## Claude Desktop Configuration

Add to your Claude Desktop config (`~/.config/claude/claude_desktop_config.json` on Linux/macOS or `%APPDATA%\Claude\claude_desktop_config.json` on Windows):

```json
{
  "mcpServers": {
    "immich": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/ImmichMCP/ImmichMCP", "--", "--stdio"],
      "env": {
        "IMMICH_BASE_URL": "https://photos.example.com",
        "IMMICH_API_KEY": "your-api-key"
      }
    }
  }
}
```

Or with Docker:

```json
{
  "mcpServers": {
    "immich": {
      "command": "docker",
      "args": ["run", "-i", "--rm",
               "-e", "IMMICH_BASE_URL=https://photos.example.com",
               "-e", "IMMICH_API_KEY=your-api-key",
               "ghcr.io/barryw/immichmcp:latest", "--stdio"]
    }
  }
}
```

## Available Tools

### Health & Capabilities

| Tool | Description |
|------|-------------|
| `immich_ping` | Verify connectivity and return server version |
| `immich_capabilities` | List available API features |

### Assets

| Tool | Description |
|------|-------------|
| `immich_assets_list` | List recent assets with filters |
| `immich_assets_get` | Get full asset metadata |
| `immich_assets_exif` | Get EXIF data for an asset |
| `immich_assets_download_original` | Get download URL for original (or inline content with `DOWNLOAD_MODE=base64`) |
| `immich_assets_download_thumbnail` | Get thumbnail/preview URLs (or inline preview image with `DOWNLOAD_MODE=base64`) |
| `immich_assets_upload` | Upload asset (base64) |
| `immich_assets_upload_from_path` | Upload from local file path |
| `immich_assets_upload_authorize` | Mint a short-lived, upload-only URL so a client can upload local files **directly** to Immich (no API key exposed) |
| `immich_assets_upload_init` | Start an out-of-band upload session; returns a URL to POST a file to |
| `immich_assets_upload_status` | Check the status of an out-of-band upload session |
| `immich_assets_update` | Update asset metadata |
| `immich_assets_bulk_update` | Bulk update multiple assets |
| `immich_assets_delete` | Delete asset(s) |
| `immich_assets_statistics` | Get asset statistics |

### Search

| Tool | Description |
|------|-------------|
| `immich_search_metadata` | Search by metadata filters |
| `immich_search_smart` | ML-based semantic search (CLIP) |
| `immich_search_ocr` | OCR text search inside images |
| `immich_search_explore` | Get explore/discovery data |

### Albums

| Tool | Description |
|------|-------------|
| `immich_albums_list` | List all albums |
| `immich_albums_get` | Get album details |
| `immich_albums_create` | Create new album |
| `immich_albums_update` | Update album metadata |
| `immich_albums_assets_add` | Add assets to album |
| `immich_albums_assets_remove` | Remove assets from album |
| `immich_albums_delete` | Delete album |
| `immich_albums_statistics` | Get album statistics |

### People

| Tool | Description |
|------|-------------|
| `immich_people_list` | List all recognized people |
| `immich_people_get` | Get person details |
| `immich_people_update` | Update person info |
| `immich_people_merge` | Merge duplicate people |
| `immich_people_assets` | List assets for a person |

### Tags

| Tool | Description |
|------|-------------|
| `immich_tags_list` | List all tags |
| `immich_tags_get` | Get tag by ID |
| `immich_tags_create` | Create new tag |
| `immich_tags_update` | Update tag |
| `immich_tags_delete` | Delete tag |
| `immich_tags_assets_add` | Tag assets |
| `immich_tags_assets_remove` | Remove tag from assets |

### Shared Links

| Tool | Description |
|------|-------------|
| `immich_shared_links_list` | List all shared links |
| `immich_shared_links_get` | Get shared link details |
| `immich_shared_links_create` | Create shared link |
| `immich_shared_links_update` | Update shared link |
| `immich_shared_links_delete` | Delete shared link |

### Activities

| Tool | Description |
|------|-------------|
| `immich_activities_list` | List comments/likes |
| `immich_activities_create` | Add comment or like |
| `immich_activities_delete` | Delete activity |
| `immich_activities_statistics` | Get activity statistics |

## Example Usage

### Search for photos from last month

```
Search for photos taken in the last 30 days that are favorites
```

### Create an album and add photos

```
Create a new album called "2026 Winter Vacation" and add all photos from January 2026
```

### Smart search

```
Find photos of sunset at the beach
```

### Bulk archive

```
Archive all photos from 2020 that aren't favorites
```

### Upload a local folder (no install, no exposed API key)

```
Upload ~/Photos/Iceland2026 to Immich into an album called "Iceland 2026"
```

Because the MCP server is remote and cannot read your disk, `immich_assets_upload_authorize`
mints a short-lived, **upload-only** shared-link URL scoped to a (dynamically created) album.
The client then uploads the files **directly to Immich** with `curl` it already has — the master
API key never leaves the server, and no CLI/script needs to be installed. Re-running is safe and
resumable: Immich deduplicates by content, so already-uploaded files return `duplicate`. See the
[uploading-local-media](docs/uploading-local-media.md) doc for the exact client recipe.

```jsonc
// immich_assets_upload_authorize(album_name: "Iceland 2026", ttl_minutes: 120)
{
  "upload_url": "https://immich.example/api/assets?key=<token>",
  "album_id": "…", "shared_link_id": "…", "expires_at": "2026-07-02T14:00:00.0000000Z"
}
// then: POST each file to upload_url (multipart: assetData, fileCreatedAt, fileModifiedAt)
```

## Safety Features

- All destructive operations require explicit `confirm: true` parameter
- Bulk operations default to `dryRun: true` mode
- Dry runs return what would be affected without making changes

## Response Format

All tools return a consistent JSON envelope:

```json
{
  "ok": true,
  "result": { ... },
  "meta": {
    "request_id": "uuid",
    "page": 1,
    "page_size": 25,
    "total": 123,
    "next": "cursor-or-null",
    "immich_base_url": "https://photos.example.com"
  },
  "warnings": []
}
```

Error responses:

```json
{
  "ok": false,
  "error": {
    "code": "NOT_FOUND",
    "message": "Asset not found",
    "details": { ... }
  },
  "meta": { ... }
}
```

Upstream failures are never swallowed into empty/success-looking results: a non-2xx
response from Immich surfaces as an error, and per the MCP spec every tool-execution
error is returned as a result with `isError: true` (not a JSON-RPC protocol error), so
the calling model can see and react to it. Error `code` maps the upstream status
(`AUTH_FAILED`, `NOT_FOUND`, `VALIDATION`, `RATE_LIMIT`, `UPSTREAM_ERROR`).

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Related Projects

- [Immich](https://github.com/immich-app/immich) - Self-hosted photo and video management
- [PaperlessMCP](https://github.com/barryw/PaperlessMCP) - MCP server for Paperless-ngx
