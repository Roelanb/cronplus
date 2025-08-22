# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Cronplus is a lightweight file-driven automation daemon written in Go that watches directories for file events and executes configurable pipelines of actions. The system includes an HTTP API, a web UI, and state persistence using BoltDB.

## Development Commands

### Build
```bash
./scripts/build.sh                    # Build for host platform
OUT_DIR=build ./scripts/build.sh      # Build to custom directory
GOOS=windows GOARCH=amd64 ./scripts/build.sh  # Cross-compile
```

### Test
```bash
./scripts/test.sh                     # Run tests with race detector
RACE=0 ./scripts/test.sh             # Run tests without race detector
go test ./internal/actions/...       # Run specific package tests
```

### Run Development Server
```bash
./scripts/dev.sh                      # Run with example config
LOG_LEVEL=debug CONFIG=examples/config.json ./scripts/dev.sh
go run ./cmd/cronplusd -config examples/config.json -api-addr 127.0.0.1:8080
```

### Linting
```bash
go vet ./...                         # Run Go vet
go fmt ./...                         # Format code
```

## Architecture

### Core Components

**Task Manager** (`internal/task/manager.go`)
- Orchestrates all task supervisors
- Manages task lifecycle and configuration updates
- Handles concurrent task execution with configurable limits

**File Watcher** (`internal/watch/watcher.go`)
- Uses fsnotify for filesystem events
- Supports glob patterns for file matching
- Implements debouncing and stabilization to avoid duplicate events

**Action Pipeline** (`internal/actions/`)
- Modular action system with retry policies
- Actions: copy, delete, archive, print
- Each action returns metadata for logging and state tracking

**State Persistence** (`internal/task/state_bbolt.go`)
- Uses BoltDB for lightweight embedded storage
- Tracks task execution history and file processing state
- Prevents duplicate processing of files

**HTTP API & UI** (`internal/api/`)
- RESTful API for configuration management
- Server-rendered HTML UI using Go templates
- Endpoints: `/api/config`, `/api/tasks`, `/api/reload`

### Configuration Model

The configuration is JSON-based with the following structure:
- **Tasks**: Array of task definitions with watch specs and pipelines
- **Watch Spec**: Directory, glob pattern, debounce/stabilization settings
- **Pipeline Steps**: Sequence of actions (copy, delete, archive, print)
- **Variables**: Task-scoped variables with interpolation support
- **Retry Policies**: Per-step retry configuration with backoff

### Key Design Patterns

1. **Supervisor Pattern**: Each task runs in its own goroutine supervisor
2. **Pipeline Pattern**: Actions are chained in a configurable pipeline
3. **Event Debouncing**: File events are debounced to handle rapid changes
4. **Atomic Operations**: Copy operations support atomic moves with checksums
5. **Graceful Shutdown**: Signal handling for clean daemon termination

## Testing Strategy

- Unit tests for action modules (`internal/actions/*_test.go`)
- Use `testing.T.TempDir()` for isolated filesystem testing
- Mock interfaces for external dependencies
- Race condition testing enabled by default

## Important Considerations

- File operations check for atomic moves and checksum verification
- The daemon requires appropriate OS permissions for file watching and printing
- Configuration changes trigger a full reload of task supervisors
- State database uses BoltDB for simplicity and portability
- Variable interpolation supports `${VAR_NAME}` syntax in pipeline steps
- Always update the plan.md after implementing a feature
