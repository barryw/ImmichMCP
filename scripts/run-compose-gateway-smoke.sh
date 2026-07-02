#!/usr/bin/env bash
set -euo pipefail

namespace="${IMMICH_KUBE_NAMESPACE:-default}"
service="${IMMICH_KUBE_SERVICE:-immich-server}"
service_port="${IMMICH_KUBE_SERVICE_PORT:-2283}"
local_port="${IMMICH_LOCAL_PORT:-2283}"
mcp_port="${MCP_PORT:-5050}"
env_file="${IMMICH_MCP_ENV_FILE:-.env}"

if [[ ! -f "$env_file" ]]; then
  IMMICH_LOCAL_PORT="$local_port" MCP_PORT="$mcp_port" IMMICH_MCP_ENV_FILE="$env_file" \
    scripts/write-compose-env-from-k8s.sh
fi

kubectl -n "$namespace" port-forward "svc/${service}" "${local_port}:${service_port}" >/tmp/immichmcp-port-forward.log 2>&1 &
port_forward_pid="$!"

cleanup() {
  kill "$port_forward_pid" >/dev/null 2>&1 || true
}
trap cleanup EXIT

for _ in {1..30}; do
  if curl -fsS "http://127.0.0.1:${local_port}/api/server/ping" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

docker compose --env-file "$env_file" up --build -d immichmcp

for _ in {1..30}; do
  if curl -fsS "http://127.0.0.1:${mcp_port}/health" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

IMMICH_MCP_INTEGRATION_TESTS=true \
IMMICH_MCP_BASE_URL="http://127.0.0.1:${mcp_port}/mcp" \
  dotnet test ImmichMCP.Tests/ImmichMCP.Tests.csproj --filter "Category=McpIntegration"
