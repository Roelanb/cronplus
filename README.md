# Cronplus

Cronplus is a lightweight, file-driven automation daemon. It watches directories for file events and executes configurable pipelines of actions (copy, delete, archive, print, etc.). It includes:
- A built-in HTTP API for control and configuration
- A simple, server-rendered UI (THUI) for managing tasks and configuration
- A persisted versioning mechanism for backend and frontend
- Cross-compilation scripts to produce a Windows .exe

This README explains installation, configuration, UI usage, API endpoints, development, and packaging in detail.

## Features

- Watch any directory for changes using glob patterns
- Debounce and stabilization timers to avoid duplicate/incomplete event handling
- Pipelines with steps such as:
  - copy: Copy files to a destination with optional atomic move and checksum verification
  - delete: Remove files (with a placeholder for secure delete toggle)
  - archive: Archive files to a destination with conflict strategy
  - print: Send files to a printer with options, timeout, and copies
- Retry policy per step (max attempts, backoffMs)
- Config persisted as JSON on disk and editable via UI or API
- Minimal built-in UI for administration
- Windows build scripts that auto-bump persisted backend/frontend versions and inject backend version into the binary

## Repository Layout

- cmd/cronplusd: Main daemon entry point
- internal/api: HTTP server and UI templates
- internal/config: Config model, loader and parser
- internal/task: Task manager and supervisors, bbolt-backed state
- internal/actions: Implementations of pipeline actions
- internal/watch: Filesystem watcher and utilities
- scripts: Development/build/test helper scripts
- examples/config.json: Example configuration
- version/backend.version: Persisted backend semantic version (patched on each Windows build via script)
- version/frontend.version: Persisted frontend semantic version (patched on each Windows build via script)

## Requirements

- Go 1.21+ (recommended)
- Linux, macOS, or Windows
- For file watching and printing actions, ensure OS-level permissions are in place

## Quick Start

1) Build and run locally (Linux/macOS):
- Compile and run:
  go build -o cronplusd ./cmd/cronplusd
  ./cronplusd -config examples/config.json -api-addr 127.0.0.1:8080

- Visit the UI:
  http://127.0.0.1:8080/ui

2) Build Windows executable from Linux/macOS:
- Use the script to cross-compile and auto-bump versions:
  ./scripts/build_windows.sh
  # Output: dist/cronplusd-windows-amd64.exe

3) Build on Windows (PowerShell):
- Build the Windows executable:
  ./scripts/build_windows.ps1
  # Output: dist/cronplusd-windows-amd64.exe

Note: The Linux/macOS build script performs the version bump and commit; the PowerShell script focuses on building the executable. If you want bump logic on Windows too, replicate the logic from build_windows.sh into the PowerShell script.

## Configuration

Cronplus reads configuration from a JSON file (default: examples/config.json). You can pass a path via -config.

Top-level fields overview:
- version: Optional config version string
- runtime:
  - stateDbPath: Path to the bbolt state DB (defaults to /var/lib/cronplus/state.db)
  - maxConcurrentPerTask: Max concurrent workers per task
- tasks: Array of task objects:
  - id: Unique task ID
  - enabled: true/false
  - watch:
    - directory: Absolute or relative path to watch
    - glob: Glob pattern to match files (e.g., *.pdf)
    - debounceMs: Debounce interval in milliseconds
    - stabilizationMs: Stabilization time in milliseconds before processing
  - pipeline: Array of step objects, each with a type and parameters:
    - type: copy | delete | archive | print
    - For copy:
      {
        "type": "copy",
        "copy": {
          "destination": "/path/to/dir",
          "atomic": true,
          "verifyChecksum": false,
          "retry": { "max": 3, "backoffMs": 1000 }
        }
      }
    - For delete:
      {
        "type": "delete",
        "delete": {
          "secure": false,
          "retry": { "max": 3, "backoffMs": 1000 }
        }
      }
    - For archive:
      {
        "type": "archive",
        "archive": {
          "destination": "/path/to/archive",
          "conflictStrategy": "rename",  // rename|overwrite|skip
          "retry": { "max": 3, "backoffMs": 1000 }
        }
      }
    - For print:
      {
        "type": "print",
        "print": {
          "printerName": "MyPrinter",
          "options": { "media": "A4", "sides": "two-sided-long-edge" },
          "timeoutSec": 60,
          "copies": 1,
          "retry": { "max": 3, "backoffMs": 1000 }
        }
      }

Example:
See examples/config.json for a full, working example.

## UI (THUI)

Navigate to /ui for a dark-themed, server-rendered UI to:
- View health and running tasks snapshot
- Reload configuration in the daemon
- List tasks, toggle enable/disable, delete tasks
- Add a new task or edit existing tasks with a form
- Edit raw JSON config at /ui/config

Pipeline Editor:
- The form shows pipeline steps for a task when you click Edit
- You can add steps with the “Add Step” control and choose type
- Each step shows its fields including Retry configuration
- Saving will update the config via POST /config

Footer Versions:
- The footer displays:
  Cronplus UI — Backend vX.Y.Z — Frontend vA.B.C
- Backend version is injected at build time and passed into templates
- Frontend version is read from version/frontend.version

## API

All API endpoints are exposed on the configured address (default 127.0.0.1:8080).

- GET /health
  - Returns {"status":"ok"} when alive

- GET /tasks
  - Returns a lightweight snapshot of running tasks

- GET /config
  - Returns the current config as JSON (server model)
  - If the control plane hasn’t loaded config yet, it attempts to load from disk

- POST /config
  - Content-Type: application/json
  - Applies the provided JSON as the new configuration
  - Persists to disk and propagates to task manager
  - Returns 204 No Content on success; 400 with message on validation errors

- POST /reload
  - Triggers the control plane to reload config from disk path
  - Useful when external processes update the config file

Note: The UI uses these endpoints. You can also drive the daemon programmatically or via curl.

## Daemon Lifecycle

- On startup:
  - Loads the config file specified by -config (defaults to examples/config.json)
  - Opens bbolt state DB (defaults to /var/lib/cronplus/state.db if not specified)
  - Starts task supervisors according to the pipeline configuration
  - Starts the HTTP API and UI
- On shutdown (SIGINT/SIGTERM):
  - The API server is gracefully shut down
  - Supervisors are drained and the process exits

## Logging

- Configurable via -log-level flag: debug|info|warn|error
- Log entries include contextual key/value data
- Critical events are logged to stderr and may cause process exit

## State Store

- Uses a bbolt database for state (job state, bookkeeping)
- Controlled by runtime.stateDbPath in the config
- Automatically created if absent; ensure the process has write permissions

## Versioning

Persisted version files:
- version/backend.version: Backend semantic version. The Linux/macOS build script bumps the patch component on every Windows build.
- version/frontend.version: Frontend semantic version. The Linux/macOS build script bumps the patch component on every Windows build.

Backend injection:
- cmd/cronplusd/main.go exposes a version variable (default "dev")
- The build script injects a value using:
  -ldflags "-X 'main.version=${BACKEND_VERSION}'"

UI display:
- The UI templates receive BackendVersion and FrontendVersion in the handler maps
- The footer prints both version strings

Auto-bump:
- scripts/build_windows.sh reads current versions, bumps patch (X.Y.Z -> X.Y.(Z+1)), writes back, builds, and commits the change if in a git repo

## Building

Go CLI:
- Build locally:
  go build -o cronplusd ./cmd/cronplusd

Linux/macOS cross-compile to Windows:
- With version bump and commit:
  ./scripts/build_windows.sh
  # dist/cronplusd-windows-amd64.exe

Windows PowerShell:
- Build without bump:
  ./scripts/build_windows.ps1
  # dist/cronplusd-windows-amd64.exe

CGO:
- Default is CGO_ENABLED=0 for portability
- Override:
  CGO_ENABLED=1 ./scripts/build_windows.sh
  # or
  ./scripts/build_windows.ps1 -CgoEnabled 1

Architectures:
- Default GOARCH=amd64
- Override:
  GOARCH=arm64 ./scripts/build_windows.sh
  # or
  ./scripts/build_windows.ps1 -GoArch arm64

## Running

Basic:
- Run the daemon:
  ./cronplusd -config examples/config.json -api-addr 127.0.0.1:8080

- Visit the UI:
  http://127.0.0.1:8080/ui

Permissions:
- Ensure the daemon has read permission on watched directories and write permission on destination/archival directories and state DB

## Development

Scripts:
- scripts/dev.sh: helper for local development server (if present)
- scripts/test.sh: run unit tests (if present)
- scripts/build.sh: generic build helper (if present)
- scripts/build_windows.sh: cross-compile and bump versions
- scripts/build_windows.ps1: Windows build

Testing:
- Go unit tests:
  go test ./...

Linting:
- Use your preferred linter (e.g., golangci-lint)

Contributing:
- Fork, create a feature branch, and open a PR
- Write concise commit messages and include tests where appropriate

## Troubleshooting

Pipeline steps not visible in UI:
- Fixed in UI: pipeline JSON is now coerced properly from typed config to generic map
- Ensure your task has a pipeline in the config
- Try refreshing /ui/task/edit?id=YOUR_TASK_ID

Cannot write state DB:
- Check runtime.stateDbPath and file permissions
- Default path: /var/lib/cronplus/state.db

Print action fails:
- Verify printerName and options
- Ensure the host can reach the printer and drivers are installed

Config POST fails with 400:
- Validate JSON syntax and field names
- Required fields:
  - task.id, watch.directory
- Check per-step required fields (e.g., copy.destination)

## Roadmap

- Additional built-in actions (transform, shell, HTTP callbacks)
- Pluggable action interface and registry
- Advanced retry strategies (jitter, exponential backoff)
- Per-task metrics and Prometheus /metrics endpoint
- Role-based access control for the UI/API

## License

This project is provided under the MIT License unless otherwise specified in the repository.
