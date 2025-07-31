#!/usr/bin/env bash
set -euo pipefail

# Build backend daemon
# Usage:
#   ./scripts/build.sh
#
# Env:
#   OUT_DIR (default: dist)
#   GOOS, GOARCH (optional for cross-compilation)

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

OUT_DIR="${OUT_DIR:-dist}"
mkdir -p "$OUT_DIR"

BIN_NAME="cronplusd"
if [[ -n "${GOOS:-}" && -n "${GOARCH:-}" ]]; then
  SUF="${GOOS}-${GOARCH}"
  echo "Building ${BIN_NAME} for ${SUF}"
  GOOS="$GOOS" GOARCH="$GOARCH" go build -o "${OUT_DIR}/${BIN_NAME}-${SUF}" ./cmd/cronplusd
else
  echo "Building ${BIN_NAME} for host"
  go build -o "${OUT_DIR}/${BIN_NAME}" ./cmd/cronplusd
fi

echo "Built artifacts in ${OUT_DIR}"
