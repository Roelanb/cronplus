#!/usr/bin/env bash
set -euo pipefail

# Development helper: run the daemon with the example config.
# Usage:
#   ./scripts/dev.sh
#
# Env:
#   LOG_LEVEL (default: debug)
#   CONFIG (default: examples/config.json)

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

LOG_LEVEL="${LOG_LEVEL:-debug}"
CONFIG="${CONFIG:-examples/config.json}"

cd "$ROOT_DIR"

if ! command -v go &>/dev/null; then
  echo "Go toolchain is required on PATH" >&2
  exit 1
fi

go run ./cmd/cronplusd -config "$CONFIG" -log-level "$LOG_LEVEL"
