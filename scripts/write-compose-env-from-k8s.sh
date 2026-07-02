#!/usr/bin/env bash
set -euo pipefail

namespace="${IMMICH_KUBE_NAMESPACE:-default}"
secret_name="${IMMICH_API_KEY_SECRET:-immich-token}"
secret_key="${IMMICH_API_KEY_SECRET_KEY:-token}"
local_port="${IMMICH_LOCAL_PORT:-2283}"
mcp_port="${MCP_PORT:-5050}"
env_file="${IMMICH_MCP_ENV_FILE:-.env}"

secret_value="$(kubectl get secret -n "$namespace" "$secret_name" -o "jsonpath={.data.${secret_key}}")"

if [[ -z "$secret_value" ]]; then
  echo "Secret $namespace/$secret_name key $secret_key was empty or missing." >&2
  exit 1
fi

decode_base64() {
  if base64 --decode >/dev/null 2>&1 <<<"dGVzdA=="; then
    base64 --decode
  else
    base64 -D
  fi
}

api_key="$(decode_base64 <<<"$secret_value")"

umask 077
{
  echo "IMMICH_BASE_URL=http://host.docker.internal:${local_port}"
  printf 'IMMICH_API_KEY=%s\n' "$api_key"
  echo "IMMICH_TOOL_MODE=gateway"
  echo "MCP_PORT=${mcp_port}"
  echo "MCP_PUBLIC_URL=http://localhost:${mcp_port}"
  echo "MCP_LOG_LEVEL=Information"
  echo "DOWNLOAD_MODE=url"
  echo "MAX_PAGE_SIZE=100"
} > "$env_file"

echo "Wrote $env_file for docker compose. The file is gitignored."
