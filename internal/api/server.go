package api

import (
	"context"
	"encoding/json"
	"io"
	"net"
	"net/http"
	"sync"
	"time"
)

type Logger interface {
	Infow(msg string, keysAndValues ...any)
	Errorw(msg string, keysAndValues ...any)
}

type Control interface {
	// Reload triggers a configuration reload (to be implemented later by manager/daemon).
	Reload(ctx context.Context) error
	// TasksSnapshot returns a lightweight view of running tasks (id, enabled) if available.
	TasksSnapshot() any
	// GetConfig returns the current config model as JSON-able structure.
	GetConfig() any
	// ApplyConfig replaces the current config with the provided JSON bytes.
	ApplyConfig(ctx context.Context, raw []byte) error
}

type Server struct {
	log   Logger
	ctrl  Control
	mux   *http.ServeMux
	srv   *http.Server
	addr  string
	ln    net.Listener
	mu    sync.Mutex
	start bool
}

func New(log Logger, ctrl Control, addr string) *Server {
	mux := http.NewServeMux()
	s := &Server{
		log:  log,
		ctrl: ctrl,
		mux:  mux,
		addr: addr,
	}
	mux.HandleFunc("/health", s.handleHealth)
	mux.HandleFunc("/tasks", s.handleTasks)
	mux.HandleFunc("/reload", s.handleReload)
	// Config management endpoints
	mux.HandleFunc("/config", s.handleConfig)
	// Mount server-rendered UI
	s.mountUI()
	// Prometheus /metrics will be mounted later via promhttp if enabled
	return s
}

func (s *Server) Start(ctx context.Context) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.start {
		return nil
	}
	ln, err := net.Listen("tcp", s.addr)
	if err != nil {
		return err
	}
	s.ln = ln
	s.srv = &http.Server{
		Handler:           s.mux,
		ReadHeaderTimeout: 5 * time.Second,
	}
	go func() {
		s.log.Infow("api server listening", "addr", s.addr)
		if err := s.srv.Serve(ln); err != nil && err != http.ErrServerClosed {
			s.log.Errorw("api server error", "error", err)
		}
	}()
	s.start = true
	go func() {
		<-ctx.Done()
		_ = s.Shutdown(context.Background())
	}()
	return nil
}

func (s *Server) Shutdown(ctx context.Context) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.srv == nil {
		return nil
	}
	err := s.srv.Shutdown(ctx)
	s.start = false
	return err
}

func (s *Server) handleHealth(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "application/json")
	_ = json.NewEncoder(w).Encode(map[string]any{"status": "ok"})
}

func (s *Server) handleTasks(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "application/json")
	if s.ctrl == nil {
		_ = json.NewEncoder(w).Encode([]any{})
		return
	}
	_ = json.NewEncoder(w).Encode(s.ctrl.TasksSnapshot())
}

func (s *Server) handleConfig(w http.ResponseWriter, r *http.Request) {
	switch r.Method {
	case http.MethodGet:
		if s.ctrl == nil {
			http.Error(w, "control unavailable", http.StatusServiceUnavailable)
			return
		}
		w.Header().Set("Content-Type", "application/json")
		_ = json.NewEncoder(w).Encode(s.ctrl.GetConfig())
	case http.MethodPost:
		if s.ctrl == nil {
			http.Error(w, "control unavailable", http.StatusServiceUnavailable)
			return
		}
		raw, err := io.ReadAll(r.Body)
		if err != nil {
			http.Error(w, "read body: "+err.Error(), http.StatusBadRequest)
			return
		}
		ctx, cancel := context.WithTimeout(r.Context(), 10*time.Second)
		defer cancel()
		if err := s.ctrl.ApplyConfig(ctx, raw); err != nil {
			http.Error(w, err.Error(), http.StatusBadRequest)
			return
		}
		w.WriteHeader(http.StatusNoContent)
	default:
		http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
	}
}

func (s *Server) handleReload(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodPost {
		http.Error(w, "POST only", http.StatusMethodNotAllowed)
		return
	}
	if s.ctrl == nil {
		http.Error(w, "control unavailable", http.StatusServiceUnavailable)
		return
	}
	ctx, cancel := context.WithTimeout(r.Context(), 10*time.Second)
	defer cancel()
	if err := s.ctrl.Reload(ctx); err != nil {
		http.Error(w, err.Error(), http.StatusBadRequest)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}
