# ImmichMCP

A Model Context Protocol (MCP) server for [Immich](https://immich.app/) - the self-hosted photo and video management solution. This server provides a first-class AI interface to manage your Immich library.

## Features

- **Asset Management**: Search, browse, upload, update, and delete photos/videos
- **Smart Search**: ML-powered semantic search using CLIP (e.g., "sunset at the beach")
- **Metadata Search**: Filter by date, location, camera, people, and more
- **Albums**: Create, manage, and share photo albums
- **People**: View and manage face recognition clusters
- **Tags**: Organize assets with custom tags
- **Shared Links**: Create shareable URLs for albums and assets
- **Activities**: Add comments and likes to albums/assets

## Requirements

- .NET 10.0 SDK
- Immich server instance
- Immich API key

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
| `immich_assets_update` | Update asset metadata |
| `immich_assets_bulk_update` | Bulk update multiple assets |
| `immich_assets_delete` | Delete asset(s) |
| `immich_assets_statistics` | Get asset statistics |

### Search

| Tool | Description |
|------|-------------|
| `immich_search_metadata` | Search by metadata filters |
| `immich_search_smart` | ML-based semantic search (CLIP) |
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

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Related Projects

- [Immich](https://github.com/immich-app/immich) - Self-hosted photo and video management
- [PaperlessMCP](https://github.com/barryw/PaperlessMCP) - MCP server for Paperless-ngx
