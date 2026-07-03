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

For a Kubernetes-hosted Immich server, use the helper script to open a temporary port-forward:

```bash
export IMMICH_API_KEY="your-api-key"
scripts/run-immich-integration-tests.sh
```

By default the script forwards `svc/immich-server` in the `default` namespace from port `2283`.
Override with `IMMICH_KUBE_CONTEXT`, `IMMICH_KUBE_NAMESPACE`, `IMMICH_KUBE_SERVICE`, `IMMICH_KUBE_SERVICE_PORT`, or `IMMICH_LOCAL_PORT`.

Mutation coverage, including upload/delete, is disabled by default. Enable it explicitly:

```bash
export IMMICH_INTEGRATION_MUTATION_TESTS=true
scripts/run-immich-integration-tests.sh
```

With mutation coverage enabled, `ToolCoverageIntegrationTests` exercises **all 49 tools**
against the live server. It is strictly non-destructive to existing data: every mutation
runs on throwaway fixtures the test creates (uploaded PNGs, an album, a tag, shared links,
an activity) and teardown deletes only those; the two tools that would mutate real,
un-creatable data (`immich_people_update`, `immich_people_merge`) are exercised with bogus
IDs only and must refuse safely.

### Docker Compose MCP smoke test

For local MCP server testing without deploying a new ImmichMCP image, run the server in Docker Compose with a gitignored `.env` file:

```bash
cp .env.example .env
# edit IMMICH_API_KEY, or populate it from the existing Kubernetes secret:
scripts/write-compose-env-from-k8s.sh

scripts/run-compose-gateway-smoke.sh
```

The smoke script port-forwards `svc/immich-server` from Kubernetes, starts ImmichMCP locally in gateway mode, then verifies over MCP HTTP that the gateway exposes only bootstrap tools, enables the `health` category, and calls `immich_ping` against the real Immich server.

For manual interactive testing, keep a port-forward open in one shell:

```bash
kubectl -n default port-forward svc/immich-server 2283:2283
```

Then run Compose in another:

```bash
docker compose --env-file .env up --build
```

## Deployment

Woodpecker builds, tests, packages, and publishes container images on pushes to `main`.
Deployment is GitOps-managed: the release pipeline updates `barryw/infrastructure` at `kubernetes-default/immich/resources.yaml` through an infrastructure pull request, and ArgoCD reconciles the `immich` application from that repository after the PR merges. The pipeline does not mutate Kubernetes directly.

The GitOps update sets the ImmichMCP image tag and ensures the Argo-managed deployment has `IMMICH_TOOL_MODE=gateway`. After merging the infrastructure PR, Woodpecker waits for Argo to reconcile by running the MCP gateway integration test against `http://immich-mcp.default.svc.cluster.local:5000/mcp`.

Required Woodpecker secrets:

| Secret | Purpose |
|--------|---------|
| `github_username` | GHCR username |
| `github_token` | GHCR token plus GitHub write access to `barryw/ImmichMCP` and `barryw/infrastructure` |

The direct Immich API integration step runs before image publish only when `IMMICH_API_KEY` is already injected into the runner environment. The required deployment verification does not need that key; it exercises the Argo-managed MCP service after the GitOps commit reconciles.

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
| `DOWNLOAD_MODE` | No | `url` | `url` returns URLs, `base64` returns encoded content |
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
| `immich_assets_download_original` | Get download URL for original |
| `immich_assets_download_thumbnail` | Get thumbnail/preview URLs |
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
