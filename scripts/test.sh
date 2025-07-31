#!/usr/bin/env bash
set -euo pipefail

# Run linters and tests
# Usage:
#   ./scripts/test.sh
#
# Env:
#   RACE (default: 1) - enable race detector

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

RACE="${RACE:-1}"

if ! command -v go &>/dev/null; then
  echo "Go toolchain is required on PATH" >&2
  exit 1
fi

echo "Running go vet..."
go vet ./...

echo "Running tests..."
if [[ "$RACE" == "1" ]]; then
  go test -race ./...
else
  go test ./...
fi
