# Cronplus Execution Plan

## 1. Objectives
- Implement a Go backend that watches directories and executes tasks defined in a JSON config.
- Ensure each task runs independently with concurrency, retries, and idempotency.
- Provide a simple Go-based frontend to create and manage the config file.

## 2. Scope and Requirements
Tasks to implement:
1) On file created in a directory:
   - Print the file to a printer
   - Archive the printed file to a certain location

2) On file created in a directory:
   - Copy the file to a certain location
   - Delete the file from the original location

Core constraints:
- Configuration via JSON (directories, globs, debounce/stabilization timings, actions, retry policies).
- Independent tasks with per-task concurrency and isolation.
- Minimal control API for health, tasks snapshot, reload; metrics later.
- Persisted state for idempotency and visibility.

## 3. Architecture Overview
Components:
- Watcher: fsnotify-based directory watcher with glob filter, debounce, and stabilization, emitting stabilized file events.
- Task Manager: Coordinates per-task supervisors; applies config; spawns worker pools.
- Pipeline Engine: Executes ordered action steps (copy/delete/archive/print) with retry/backoff and state updates.
- Actions: Implement file-level operations with safety and atomic guarantees where possible.
- State Store: bbolt-based persistence for file processing state (status, attempts, last error).
- Control API: HTTP server exposing /health, /tasks, /reload, and later /metrics.
- Frontend (Go): Minimal UI to view snapshot and manage config (later phase).

Data flow:
Watcher -> Event -> Manager/Supervisor enqueue -> Worker executes Pipeline -> Actions operate on FS -> State persisted -> Logs/metrics emitted.

## 4. Phases and Deliverables

### Phase 0: Project Setup
- Initialize Go module and directories: cmd/, internal/{watch,task,actions,api}, scripts/, examples/.
- Scripts: build.sh, dev.sh, test.sh.
- Logger setup.
- Example config.json with both tasks and dev-friendly paths (/tmp/cronplus).

Deliverables:
- Compilable project; dev script to run the daemon.

### Phase 1: Minimal Pipeline (Baseline)
- Watch for file creation; minimal handler logs lifecycle and marks state (queued → processing → done).
- BBolt state store for idempotency.
- Control API endpoints: /health, /tasks (snapshot), /reload.

Deliverables:
- End-to-end: file creation triggers logs and state transitions.
- /tasks returns configured tasks snapshot.

### Phase 2: Actions & Pipelines
- Actions:
  - Copy: atomic temp write + rename, mkdir -p for destination, optional checksum verification.
  - Delete: safe removal; verify regular file; no-op if missing.
  - Archive: os.Rename with fallback to copy+delete; conflict strategies: rename (default), overwrite, skip.
  - Print: lp/lpr integration with timeout and retries (initially stub then enable).
- Pipeline engine:
  - Execute steps in order as specified in config.
  - Retry/backoff per step (max, backoffMs).
  - Update state attempts and errors; idempotency respected.

Deliverables:
- Validated copy+delete flow, then archive and print wiring.
- Retry/backoff applied to Copy, Delete, and Archive.

### Phase 3: Reliability and Observability
- Dead-letter handling: move files that fail after retries to a configured directory (optional).
- Metrics:
  - Prometheus counters/histograms: processed_total{status,task_id}, action_duration_seconds{action,task_id}, inflight{task_id}.
  - Expose /metrics toggle via runtime configuration.
- Extend /tasks snapshot with basic stats (optional).

Deliverables:
- Metrics available for scraping; richer snapshot (optional).

### Phase 4: Frontend (Go UI)
- Minimal UI using Go templates/htmx:
  - View /tasks snapshot and /health.
  - Edit config JSON and submit; trigger /reload.
  - Optional: recent events display.

Deliverables:
- Simple UI to manage config and observe status.

### Phase 5: Testing and CI
- Unit tests:
  - Actions: copy/delete/archive (conflict strategies) and print stub behavior.
  - Watcher: stabilization and glob filtering using temp directories.
  - Manager: runPipeline and retry via small doubles/mocks where necessary.
- Integration tests:
  - Start manager with temp config; create files; assert copies, deletions, archives, and state transitions.
  - API: /health, /tasks, /reload via httptest.
- CI:
  - GitHub Actions workflow: go build, go vet, golangci-lint (optional), go test ./...

Deliverables:
- Automated test coverage for critical paths; CI passing.

## 5. Configuration Schema (JSON)
- version: number
- runtime:
  - stateDbPath: string
  - maxConcurrentPerTask: int
  - apiAddr: string
  - metricsEnabled: bool
  - deadLetterDir: string (optional)
- tasks: Task[]
  - id: string
  - enabled: bool
  - watch:
    - directory: string
    - glob: string (e.g., "*.pdf")
    - debounceMs: int
    - stabilizationMs: int
  - pipeline: Step[]
    - type: "copy" | "delete" | "archive" | "print"
    - copy?: { destination: string, atomic: bool, verifyChecksum: bool, retry?: { max: int, backoffMs: int } }
    - delete?: { secure: bool, retry?: { max: int, backoffMs: int } }
    - archive?: { destination: string, conflictStrategy: "rename" | "overwrite" | "skip", preserveSubdirs: bool, retry?: { max: int, backoffMs: int } }
    - print?: { printer: string, options: map[string]string, timeoutMs: int, retry?: { max: int, backoffMs: int } }

## 6. Detailed Work Breakdown

### 6.1 Watcher
- Options: Directory, Glob, Debounce, Stabilization, PollInterval.
- Emit stabilized create events with path matching glob.
- Tests: temp directories with time-based stabilization assertions.

### 6.2 State Store
- bbolt buckets and keying: sha256(taskID|path|checksum).
- FileRecord fields: status, attempts, last_error, timestamps.
- Methods: Get, Mark, IterateByTask(optional).
- Tests: Temp DB, mark transitions, idempotency checks.

### 6.3 Task Manager and Supervisors
- Manager: ApplyConfig (start/stop supervisors), TasksSnapshot.
- Supervisor: watcher -> work queue -> worker pool.
- Handler: checksum, mark state, runPipeline; record failures.
- Retry: withRetry(ctx, policy) across actions.
- Tests: integration with temp dirs; mock actions where needed for deterministic failures.

### 6.4 Actions
- Copy/Delete/Archive/Print implementations.
- Tests:
  - Copy checksum equality, atomic rename behavior (best effort), and cleanup on failure.
  - Delete safety (regular file), idempotency when missing.
  - Archive conflict strategies (rename/overwrite/skip) and cross-device fallback.
  - Print: stub with timeout and simulated return codes (full integration later).

### 6.5 API
- Routes: /health, /tasks, /reload, /metrics (optional).
- Tests: httptest for handlers, snapshot expected structures.

### 6.6 Metrics
- Prometheus collectors registration.
- Middleware/timers around actions and pipeline to record durations and outcomes.
- Tests: scrape /metrics, check expected metric names and basic samples.

### 6.7 Frontend (optional milestone)
- Basic pages: snapshot view, config editor with validation and POST reload.
- Tests: template rendering and minimal e2e with httptest.

## 7. Operational Concerns
- Filesystem permissions: dev defaults to /tmp/cronplus; prod uses /var/lib/cronplus for state and ensure service user has access.
- Logging levels: debug/info/error; correlation IDs per file.
- Graceful shutdown: context cancellation; supervisors drain; API shutdown with timeout.

## 8. Milestones and Status
- M0: Setup and minimal pipeline [Done]
- M1: Copy + Delete with retry/backoff [Done]
- M2: Archive action + wiring [Done]
- M3: Print action (pending)
- M4: Metrics + richer /tasks snapshot (pending)
- M5: Frontend (pending)
- M6: Tests (unit + integration) and CI (in progress)

## 9. Acceptance Criteria
- File creation triggers correct pipeline behavior per task.
- Copy/Delete/Archive perform as configured with retries and idempotency; failures tracked in state.
- /tasks reflects current config; /health OK; /metrics emits counters when enabled.
- Frontend can edit and apply config.
- Unit and integration tests pass in CI.

## 10. Execution Log (to be updated)
- 2025-07-31: Minimal pipeline running; state persisted via bbolt.
- 2025-07-31: Copy + Delete wired with retry/backoff; Archive implemented with default rename conflict strategy.
- 2025-07-31: /tasks snapshot exposed via control API.
- Next: Implement Print, metrics, and CI tests.
