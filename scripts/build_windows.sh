#!/usr/bin/env bash
set -euo pipefail

# Build Windows .exe for cronplusd from cmd/cronplusd/main.go
# Usage:
#   ./scripts/build_windows.sh                # default: amd64, CGO disabled
#   GOARCH=arm64 ./scripts/build_windows.sh   # build for arm64
#   VERSION=v1.2.3 ./scripts/build_windows.sh # set version string
#
# Output: dist/cronplusd-windows-${GOARCH}.exe

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." &>/dev/null && pwd)"
cd "$ROOT_DIR"

APP_NAME=cronplusd
PKG=./cmd/cronplusd

GOOS=windows
GOARCH="${GOARCH:-amd64}"
CGO_ENABLED="${CGO_ENABLED:-0}"

# Compute version
if [[ -n "${VERSION:-}" ]]; then
  VERSION_STR="$VERSION"
else
  if git -C "$ROOT_DIR" rev-parse --git-dir &>/dev/null; then
    if git -C "$ROOT_DIR" describe --tags --always --dirty &>/dev/null; then
      VERSION_STR="$(git -C "$ROOT_DIR" describe --tags --always --dirty)"
    else
      VERSION_STR="$(git -C "$ROOT_DIR" rev-parse --short HEAD)"
    fi
  else
    VERSION_STR="dev"
  fi
fi

mkdir -p dist

# Common ldflags: trim paths, reduce binary size
LDFLAGS="-s -w"

# Read and bump backend/frontend versions (patch)
read_version() {
  local f="$1"
  if [[ ! -f "$f" ]]; then
    echo "0.1.0"
    return
  fi
  cat "$f"
}
bump_patch() {
  local v="$1"
  IFS='.' read -r MA MI PA <<< "$v"
  if [[ -z "$MA" || -z "$MI" || -z "$PA" ]]; then
    echo "0.1.1"
    return
  fi
  PA=$((PA+1))
  echo "${MA}.${MI}.${PA}"
}

BACKEND_VER_FILE="version/backend.version"
FRONTEND_VER_FILE="version/frontend.version"

CUR_BACK="$(read_version "$BACKEND_VER_FILE")"
CUR_FRONT="$(read_version "$FRONTEND_VER_FILE")"

NEW_BACK="$(bump_patch "$CUR_BACK")"
NEW_FRONT="$(bump_patch "$CUR_FRONT")"

echo "$NEW_BACK" > "$BACKEND_VER_FILE"
echo "$NEW_FRONT" > "$FRONTEND_VER_FILE"

# Pass backend version into binary
LDFLAGS="${LDFLAGS} -X 'main.version=${NEW_BACK}'"

OUT="dist/${APP_NAME}-windows-${GOARCH}.exe"

echo "Building ${OUT} (GOOS=${GOOS} GOARCH=${GOARCH} CGO_ENABLED=${CGO_ENABLED}) BACKEND_VERSION=${NEW_BACK} FRONTEND_VERSION=${NEW_FRONT}"
GOOS="${GOOS}" GOARCH="${GOARCH}" CGO_ENABLED="${CGO_ENABLED}" \
  go build -o "${OUT}" -trimpath -ldflags "${LDFLAGS}" "${PKG}"

# Commit version bump (if in a git repo)
if git rev-parse --git-dir &>/dev/null; then
  git add "$BACKEND_VER_FILE" "$FRONTEND_VER_FILE"
  git commit -m "chore(version): bump backend to ${NEW_BACK} and frontend to ${NEW_FRONT}" || true
fi

echo "Built ${OUT}"
