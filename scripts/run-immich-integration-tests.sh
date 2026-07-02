#!/usr/bin/env sh
set -eu

if [ -z "${IMMICH_API_KEY:-}" ] && [ -z "${IMMICH_INTEGRATION_API_KEY:-}" ]; then
  echo "IMMICH_API_KEY or IMMICH_INTEGRATION_API_KEY is required" >&2
  exit 1
fi

cleanup() {
  if [ -n "${PORT_FORWARD_PID:-}" ]; then
    kill "$PORT_FORWARD_PID" 2>/dev/null || true
  fi
}
trap cleanup EXIT INT TERM

if [ -z "${IMMICH_BASE_URL:-}" ] && [ -z "${IMMICH_INTEGRATION_BASE_URL:-}" ]; then
  : "${IMMICH_KUBE_NAMESPACE:=default}"
  : "${IMMICH_KUBE_SERVICE:=svc/immich-server}"
  : "${IMMICH_KUBE_SERVICE_PORT:=2283}"
  : "${IMMICH_LOCAL_PORT:=2283}"

  if [ -n "${IMMICH_KUBE_CONTEXT:-}" ]; then
    KUBECTL_CONTEXT_ARGS="--context ${IMMICH_KUBE_CONTEXT}"
  else
    KUBECTL_CONTEXT_ARGS=""
  fi

  # shellcheck disable=SC2086
  kubectl $KUBECTL_CONTEXT_ARGS -n "$IMMICH_KUBE_NAMESPACE" port-forward "$IMMICH_KUBE_SERVICE" "$IMMICH_LOCAL_PORT:$IMMICH_KUBE_SERVICE_PORT" &
  PORT_FORWARD_PID=$!
  sleep 3

  export IMMICH_BASE_URL="http://127.0.0.1:$IMMICH_LOCAL_PORT"
fi

export IMMICH_INTEGRATION_TESTS=true

dotnet test ImmichMCP.Tests/ImmichMCP.Tests.csproj --filter "Category=Integration" "$@"
