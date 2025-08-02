package task

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"path/filepath"
	"sync"
	"time"

	"github.com/Roelanb/cronplus/internal/actions"
	"github.com/Roelanb/cronplus/internal/config"
	"github.com/Roelanb/cronplus/internal/watch"
)

// Manager owns per-task supervisors and coordinates watchers and workers.
type Manager struct {
	log    observabilityLogger
	state  StateStore
	mu     sync.Mutex
	tasks  map[string]*supervisor
	concur int
	cfg    *config.Config

	// reason per task if it couldn't be started
	notStartedReasons map[string]string
}

// observabilityLogger is minimal interface from zap.SugaredLogger we use.
type observabilityLogger interface {
	Infow(msg string, keysAndValues ...any)
	Errorw(msg string, keysAndValues ...any)
	Debugw(msg string, keysAndValues ...any)
	Warnw(msg string, keysAndValues ...any)
}

// NewManager creates a manager with provided logger and state store.
func NewManager(logger observabilityLogger, state StateStore, defaultConcurrency int) *Manager {
	return &Manager{
		log:               logger,
		state:             state,
		tasks:             map[string]*supervisor{},
		concur:            defaultConcurrency,
		notStartedReasons: map[string]string{},
	}
}

// ApplyConfig starts/stops supervisors to match cfg.Tasks.
// Minimal pipeline version: log file events and mark done.
func (m *Manager) ApplyConfig(ctx context.Context, cfg *config.Config) error {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.cfg = cfg

	// stop removed tasks
	existing := map[string]struct{}{}
	for _, t := range cfg.Tasks {
		existing[t.ID] = struct{}{}
	}
	for id, sup := range m.tasks {
		if _, ok := existing[id]; !ok {
			sup.stop()
			delete(m.tasks, id)
		}
	}
	// reset reasons for tasks not present anymore
	for id := range m.notStartedReasons {
		if _, ok := existing[id]; !ok {
			delete(m.notStartedReasons, id)
		}
	}

	// start/update tasks
	for ti := range cfg.Tasks {
		t := &cfg.Tasks[ti]
		if !t.Enabled {
			// If disabled explicitly, clear any previous not-started reason
			delete(m.notStartedReasons, t.ID)
			if sup, ok := m.tasks[t.ID]; ok {
				sup.stop()
				delete(m.tasks, t.ID)
			}
			continue
		}
		if sup, ok := m.tasks[t.ID]; ok {
			// existing running supervisor; clear any previous reason since it's running
			delete(m.notStartedReasons, t.ID)
			// TODO: for future: compare settings and restart if needed
			_ = sup // unchanged for minimal iteration
			continue
		}
		conc := cfg.Runtime.MaxConcurrentPerTask
		if conc <= 0 {
			conc = m.concur
		}
		sup, err := newSupervisor(ctx, m.log, m.state, *t, conc)
		if err != nil {
			// If a task cannot be started (e.g., watch directory missing), disable it in cfg,
			// remember the reason, and log a warning.
			t.Enabled = false
			reason := err.Error()
			m.notStartedReasons[t.ID] = reason
			m.log.Warnw("disabling task due to start failure", "task", t.ID, "error", reason)
			// If there was a previously running supervisor, stop it.
			if supOld, ok := m.tasks[t.ID]; ok {
				supOld.stop()
				delete(m.tasks, t.ID)
			}
			continue
		}
		// Successfully started; clear any previous reason
		delete(m.notStartedReasons, t.ID)
		m.tasks[t.ID] = sup
	}
	return nil
}

// TasksSnapshot returns a summary of currently configured tasks.
func (m *Manager) TasksSnapshot() any {
	m.mu.Lock()
	defer m.mu.Unlock()
	type taskView struct {
		ID      string `json:"id"`
		Enabled bool   `json:"enabled"`
		Watch   struct {
			Directory string `json:"directory"`
			Glob      string `json:"glob"`
		} `json:"watch"`
		Workers    int    `json:"workers"`
		NotStarted string `json:"notStartedReason,omitempty"`
	}
	var out []taskView
	if m.cfg != nil {
		for _, t := range m.cfg.Tasks {
			tv := taskView{
				ID:      t.ID,
				Enabled: t.Enabled,
				Workers: m.cfg.Runtime.MaxConcurrentPerTask,
			}
			tv.Watch.Directory = t.Watch.Directory
			tv.Watch.Glob = t.Watch.Glob
			if !t.Enabled {
				if rsn, ok := m.notStartedReasons[t.ID]; ok {
					tv.NotStarted = rsn
				}
			}
			out = append(out, tv)
		}
	}
	return out
}

type supervisor struct {
	id     string
	cancel context.CancelFunc
	wg     sync.WaitGroup
}

func newSupervisor(parent context.Context, log observabilityLogger, state StateStore, t config.Task, concurrency int) (*supervisor, error) {
	ctx, cancel := context.WithCancel(parent)

	// Configure watcher based on task watch spec
	opts := watch.Options{
		Directory:     t.Watch.Directory,
		Glob:          t.Watch.Glob,
		Debounce:      time.Duration(t.Watch.DebounceMs) * time.Millisecond,
		Stabilization: time.Duration(t.Watch.StabilizationMs) * time.Millisecond,
		PollInterval:  200 * time.Millisecond,
	}
	w, err := watch.New(opts)
	if err != nil {
		cancel()
		return nil, err
	}
	events, err := w.Start(ctx)
	if err != nil {
		cancel()
		return nil, err
	}

	s := &supervisor{
		id:     t.ID,
		cancel: cancel,
	}

	// workers
	workCh := make(chan string, 256)
	for i := 0; i < concurrency; i++ {
		s.wg.Add(1)
		go func(workerID int) {
			defer s.wg.Done()
			for path := range workCh {
				handleFilePipeline(ctx, log, state, t, path)
			}
		}(i + 1)
	}

	// event pump
	s.wg.Add(1)
	go func() {
		defer s.wg.Done()
		defer close(workCh)
		for {
			select {
			case <-ctx.Done():
				return
			case ev, ok := <-events:
				if !ok {
					return
				}
				// Enqueue path for processing
				workCh <- ev.Path
			}
		}
	}()

	return s, nil
}

func (s *supervisor) stop() {
	if s.cancel != nil {
		s.cancel()
	}
	s.wg.Wait()
}

func handleFilePipeline(ctx context.Context, log observabilityLogger, state StateStore, t config.Task, path string) {
	abs := path
	if !filepath.IsAbs(path) {
		if base := t.Watch.Directory; base != "" {
			abs = filepath.Join(base, path)
		}
	}
	// Simplified checksum (placeholder)
	chk := checksumFromPath(abs)
	corrID := fmt.Sprintf("%s-%d", t.ID, time.Now().UnixNano())

	// Idempotency skip
	if rec, _ := state.Get(t.ID, abs, chk); rec != nil && rec.Status == StatusDone {
		log.Debugw("skip already done", "task", t.ID, "path", abs)
		return
	}

	_ = state.Mark(t.ID, abs, chk, StatusQueued, 0, "")
	log.Infow("queued file", "task", t.ID, "path", abs, "correlation", corrID)

	_ = state.Mark(t.ID, abs, chk, StatusProcessing, 1, "")
	log.Infow("processing file", "task", t.ID, "path", abs, "correlation", corrID)

	// Execute configured pipeline
	if err := runPipeline(ctx, log, t, abs); err != nil {
		_ = state.Mark(t.ID, abs, chk, StatusFailed, 1, err.Error())
		log.Errorw("pipeline failed", "task", t.ID, "path", abs, "error", err, "correlation", corrID)
		return
	}

	if err := state.Mark(t.ID, abs, chk, StatusDone, 1, ""); err != nil {
		log.Errorw("mark done failed", "task", t.ID, "path", abs, "error", err, "correlation", corrID)
		return
	}
	log.Infow("done file", "task", t.ID, "path", abs, "correlation", corrID)
}

func runPipeline(ctx context.Context, log observabilityLogger, t config.Task, srcPath string) error {
	// Build variable map for interpolation from task.Variables
	vars := map[string]string{}
	for _, v := range t.Variables {
		// values are already validated/normalized by loader; keep as provided
		vars[v.Name] = v.Value
	}

	// Extended to support copy, delete, and archive with retry/backoff per-step.
	for i, step := range t.Pipeline {
		switch step.Type {
		case "copy":
			if step.Copy == nil {
				return fmt.Errorf("pipeline[%d] copy: missing options", i)
			}
			// Interpolate destination
			local := step
			if local.Copy != nil {
				local.Copy.Destination = actions.ResolveVariables(local.Copy.Destination, vars)
			}
			fn := func() error {
				_, err := doCopy(srcPath, local)
				return err
			}
			if err := withRetry(ctx, log, "copy", t.ID, i, step.Copy.Retry, fn); err != nil {
				return fmt.Errorf("pipeline[%d] copy: %w", i, err)
			}
		case "delete":
			if step.Delete == nil {
				return fmt.Errorf("pipeline[%d] delete: missing options", i)
			}
			// Nothing to interpolate for delete currently
			fn := func() error {
				return doDelete(srcPath, step)
			}
			if err := withRetry(ctx, log, "delete", t.ID, i, nil, fn); err != nil {
				return fmt.Errorf("pipeline[%d] delete: %w", i, err)
			}
		case "archive":
			if step.Archive == nil {
				return fmt.Errorf("pipeline[%d] archive: missing options", i)
			}
			// Interpolate destination
			local := step
			if local.Archive != nil {
				local.Archive.Destination = actions.ResolveVariables(local.Archive.Destination, vars)
			}
			fn := func() error {
				return doArchive(srcPath, local)
			}
			// No retry field on archive step in model; treat as no-retry unless added
			if err := withRetry(ctx, log, "archive", t.ID, i, nil, fn); err != nil {
				return fmt.Errorf("pipeline[%d] archive: %w", i, err)
			}
		case "print":
			// Not implemented in this iteration; placeholder for future interpolation:
			// printerName, options values could be interpolated similarly.
		default:
			// Unknown type; ignore
		}
	}
	return nil
}

func doCopy(src string, step config.PipelineStep) (string, error) {
	opts := actions.CopyOptions{
		Destination:    step.Copy.Destination,
		Atomic:         step.Copy.Atomic,
		VerifyChecksum: step.Copy.VerifyChecksum,
	}
	return actions.Copy(src, opts)
}

func doArchive(src string, step config.PipelineStep) error {
	conflict := actions.ConflictStrategy(step.Archive.ConflictStrategy)
	if conflict == "" {
		conflict = actions.ConflictRename
	}
	_, err := actions.Archive(src, actions.ArchiveOptions{
		Destination:     step.Archive.Destination,
		PreserveSubdirs: step.Archive.PreserveSubdirs,
		Conflict:        conflict,
	})
	return err
}

func doDelete(path string, step config.PipelineStep) error {
	opts := actions.DeleteOptions{
		Secure: step.Delete.Secure,
	}
	return actions.Delete(path, opts)
}

type retrySpec struct {
	Max       int
	BackoffMs int
}

func withRetry(ctx context.Context, log observabilityLogger, action, taskID string, idx int, rp *config.RetryPolicy, fn func() error) error {
	max := 0
	backoff := 0
	if rp != nil {
		max = rp.Max
		backoff = rp.BackoffMs
	}
	var attempt int
	for {
		err := fn()
		if err == nil {
			return nil
		}
		if attempt >= max {
			return err
		}
		attempt++
		sleep := time.Duration(backoff) * time.Millisecond
		if sleep <= 0 {
			sleep = 1 * time.Second
		}
		log.Errorw("action failed, will retry", "task", taskID, "action", action, "step", idx, "attempt", attempt, "max", max, "error", err)
		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-time.After(sleep):
		}
	}
}

func checksumFromPath(p string) string {
	h := sha256.New()
	h.Write([]byte(p))
	h.Write([]byte{0})
	h.Write([]byte(time.Now().Truncate(time.Second).Format(time.RFC3339)))
	return hex.EncodeToString(h.Sum(nil))
}
