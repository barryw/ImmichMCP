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
| `immich.ping` | Verify connectivity and return server version |
| `immich.capabilities` | List available API features |

### Assets

| Tool | Description |
|------|-------------|
| `immich.assets.list` | List recent assets with filters |
| `immich.assets.get` | Get full asset metadata |
| `immich.assets.exif` | Get EXIF data for an asset |
| `immich.assets.download.original` | Get download URL for original |
| `immich.assets.download.thumbnail` | Get thumbnail/preview URLs |
| `immich.assets.upload` | Upload asset (base64) |
| `immich.assets.upload_from_path` | Upload from local file path |
| `immich.assets.update` | Update asset metadata |
| `immich.assets.bulk_update` | Bulk update multiple assets |
| `immich.assets.delete` | Delete asset(s) |
| `immich.assets.statistics` | Get asset statistics |

### Search

| Tool | Description |
|------|-------------|
| `immich.search.metadata` | Search by metadata filters |
| `immich.search.smart` | ML-based semantic search (CLIP) |
| `immich.search.explore` | Get explore/discovery data |

### Albums

| Tool | Description |
|------|-------------|
| `immich.albums.list` | List all albums |
| `immich.albums.get` | Get album details |
| `immich.albums.create` | Create new album |
| `immich.albums.update` | Update album metadata |
| `immich.albums.assets.add` | Add assets to album |
| `immich.albums.assets.remove` | Remove assets from album |
| `immich.albums.delete` | Delete album |
| `immich.albums.statistics` | Get album statistics |

### People

| Tool | Description |
|------|-------------|
| `immich.people.list` | List all recognized people |
| `immich.people.get` | Get person details |
| `immich.people.update` | Update person info |
| `immich.people.merge` | Merge duplicate people |
| `immich.people.assets` | List assets for a person |

### Tags

| Tool | Description |
|------|-------------|
| `immich.tags.list` | List all tags |
| `immich.tags.get` | Get tag by ID |
| `immich.tags.create` | Create new tag |
| `immich.tags.update` | Update tag |
| `immich.tags.delete` | Delete tag |
| `immich.tags.assets.add` | Tag assets |
| `immich.tags.assets.remove` | Remove tag from assets |

### Shared Links

| Tool | Description |
|------|-------------|
| `immich.shared_links.list` | List all shared links |
| `immich.shared_links.get` | Get shared link details |
| `immich.shared_links.create` | Create shared link |
| `immich.shared_links.update` | Update shared link |
| `immich.shared_links.delete` | Delete shared link |

### Activities

| Tool | Description |
|------|-------------|
| `immich.activities.list` | List comments/likes |
| `immich.activities.create` | Add comment or like |
| `immich.activities.delete` | Delete activity |
| `immich.activities.statistics` | Get activity statistics |

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
