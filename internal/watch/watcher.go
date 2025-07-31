package watch

import (
	"context"
	"errors"
	"fmt"
	"path/filepath"
	"sync"
	"time"

	"github.com/fsnotify/fsnotify"
)

type Event struct {
	Path string
	Time time.Time
}

type Options struct {
	Directory     string        // absolute path to watch
	Glob          string        // glob filter (e.g., *.pdf or *)
	Debounce      time.Duration // collapse bursts within this window (0 = no debounce)
	Stabilization time.Duration // require file size to be stable for this duration before emitting (0 = no stabilization)
	PollInterval  time.Duration // interval used for stabilization checks
}

// Watcher watches a single directory for create/close-write/move-in events,
// applies debounce and stabilization, and emits file paths that are considered "ready".
type Watcher struct {
	opts Options

	mu      sync.Mutex
	w       *fsnotify.Watcher
	glob    string
	cancel  context.CancelFunc
	started bool
	closed  bool
}

// New creates a new Watcher for the given options.
func New(opts Options) (*Watcher, error) {
	if !filepath.IsAbs(opts.Directory) {
		return nil, errors.New("watch directory must be absolute")
	}
	if opts.Glob == "" {
		opts.Glob = "*"
	}
	if opts.PollInterval <= 0 {
		opts.PollInterval = 200 * time.Millisecond
	}
	return &Watcher{
		opts: opts,
		glob: opts.Glob,
	}, nil
}

// Start begins watching and returns a channel of stabilized events.
// Cancel the provided context to stop the watcher.
func (w *Watcher) Start(ctx context.Context) (<-chan Event, error) {
	w.mu.Lock()
	defer w.mu.Unlock()

	if w.started {
		return nil, errors.New("watcher already started")
	}
	if w.closed {
		return nil, errors.New("watcher closed")
	}

	fsw, err := fsnotify.NewWatcher()
	if err != nil {
		return nil, fmt.Errorf("fsnotify: %w", err)
	}
	if err := fsw.Add(w.opts.Directory); err != nil {
		_ = fsw.Close()
		return nil, fmt.Errorf("add watch: %w", err)
	}

	w.w = fsw
	ctx, cancel := context.WithCancel(ctx)
	w.cancel = cancel
	w.started = true

	out := make(chan Event, 128)

	go w.run(ctx, out)

	return out, nil
}

func (w *Watcher) run(ctx context.Context, out chan<- Event) {
	defer func() {
		w.mu.Lock()
		defer w.mu.Unlock()
		_ = w.w.Close()
		close(out)
		w.closed = true
	}()

	// pending holds last-seen time for paths to support debounce
	pending := make(map[string]time.Time)
	var mu sync.Mutex

	// ticker for debounce flush
	var debounceTicker *time.Ticker
	if w.opts.Debounce > 0 {
		debounceTicker = time.NewTicker(w.opts.Debounce)
		defer debounceTicker.Stop()
	}

	emitReady := func(p string) {
		// Stabilization: wait until file is stable in size for the stabilization window
		if w.opts.Stabilization <= 0 {
			out <- Event{Path: p, Time: time.Now()}
			return
		}
		// check file size repeatedly until unchanged across window
		firstSize := int64(-1)
		lastChange := time.Now()
		deadline := time.Now().Add(10 * time.Minute) // safety cap to avoid infinite wait

		for {
			select {
			case <-ctx.Done():
				return
			default:
			}

			info, err := lstatNoFollow(p)
			if err != nil || !info.Mode().IsRegular() {
				// File may have been moved/removed; abort silently
				return
			}
			sz := info.Size()
			now := time.Now()
			if firstSize == -1 || sz != firstSize {
				firstSize = sz
				lastChange = now
			}

			if now.Sub(lastChange) >= w.opts.Stabilization {
				out <- Event{Path: p, Time: time.Now()}
				return
			}
			if now.After(deadline) {
				// Give up stabilization after deadline
				out <- Event{Path: p, Time: time.Now()}
				return
			}
			time.Sleep(w.opts.PollInterval)
		}
	}

	matchGlob := func(name string) bool {
		ok, _ := filepath.Match(w.glob, filepath.Base(name))
		return ok
	}

	flush := func() {
		mu.Lock()
		items := make([]string, 0, len(pending))
		now := time.Now()
		for p, t := range pending {
			if w.opts.Debounce == 0 || now.Sub(t) >= w.opts.Debounce {
				items = append(items, p)
				delete(pending, p)
			}
		}
		mu.Unlock()

		for _, p := range items {
			emitReady(p)
		}
	}

	for {
		select {
		case <-ctx.Done():
			flush()
			return

		case ev, ok := <-w.w.Events:
			if !ok {
				flush()
				return
			}
			// We care about events that indicate a new/closed write or move into dir.
			// Note: fsnotify.CloseWrite is not available across all platforms/versions; use Create/Write/Rename/Chmod.
			if ev.Has(fsnotify.Create) || ev.Has(fsnotify.Write) || ev.Has(fsnotify.Rename) || ev.Has(fsnotify.Chmod) {
				// Restrict to files in directory matching glob
				path := ev.Name
				if matchGlob(path) {
					if w.opts.Debounce > 0 {
						mu.Lock()
						pending[path] = time.Now()
						mu.Unlock()
					} else {
						emitReady(path)
					}
				}
			}

		case _, ok := <-w.w.Errors:
			if !ok {
				continue
			}
			// Non-fatal; could add logging hook here if needed.

		case <-func() <-chan time.Time {
			if debounceTicker != nil {
				return debounceTicker.C
			}
			return make(chan time.Time)
		}():
			flush()
		}
	}
}

// Close stops the watcher if running.
func (w *Watcher) Close() {
	w.mu.Lock()
	defer w.mu.Unlock()
	if w.cancel != nil {
		w.cancel()
	}
}

// lstatNoFollow obtains FileInfo without following symlinks.
func lstatNoFollow(path string) (info fileInfoLike, err error) {
	return lstat(path)
}
