package main

import (
	"context"
	"flag"
	"fmt"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/Roelanb/cronplus/internal/api"
	"github.com/Roelanb/cronplus/internal/config"
	"github.com/Roelanb/cronplus/internal/observability"
	"github.com/Roelanb/cronplus/internal/task"
	zap "go.uber.org/zap"
)

var (
	configPath = flag.String("config", "examples/config.json", "Path to config JSON file")
	logLevel   = flag.String("log-level", "info", "Log level: debug|info|warn|error")
	apiAddr    = flag.String("api-addr", "127.0.0.1:8080", "Control API listen address")
)

// // Backend version injected at build time with: -ldflags "-X 'main.version=1.2.3'"
var version = "dev"

type controlPlane struct {
	logger  loggerIface
	manager *task.Manager
	cfgPath string
	cfg     *config.Config

	// add concrete logger so we can pass it into config.Load/Parse
	sugar *zap.SugaredLogger
}

func (c *controlPlane) TasksSnapshot() any {
	if c.manager == nil {
		return []any{}
	}
	return c.manager.TasksSnapshot()
}

type loggerIface interface {
	Infow(string, ...any)
	Errorw(string, ...any)
	Debugw(string, ...any)
	Warn(...any)
	Warnw(string, ...any)
	Sync() error
}

func (c *controlPlane) Reload(ctx context.Context) error {
	// Reload from disk path (legacy support)
	cfg, err := config.Load(c.cfgPath, c.sugar)
	if err != nil {
		return err
	}
	c.cfg = cfg
	return c.manager.ApplyConfig(ctx, cfg)
}

func (c *controlPlane) GetConfig() any {
	if c.cfg == nil {
		// best-effort load from file path if not set yet
		if cfg, err := config.Load(c.cfgPath, c.sugar); err == nil {
			c.cfg = cfg
		}
	}
	return c.cfg
}

func (c *controlPlane) ApplyConfig(ctx context.Context, raw []byte) error {
	cfg, err := config.Parse(raw, c.sugar)
	if err != nil {
		return err
	}
	// persist to disk to keep single source of truth
	if err := config.Save(c.cfgPath, cfg); err != nil {
		return err
	}
	c.cfg = cfg
	return c.manager.ApplyConfig(ctx, cfg)
}

func main() {
	flag.Parse()

	logger := observability.NewLogger(*logLevel)
	defer logger.Sync() //nolint:errcheck

	// Load initial config
	cfg, err := config.Load(*configPath, logger)
	if err != nil {
		logger.Errorw("failed to load config", "path", *configPath, "error", err)
		fmt.Fprintf(os.Stderr, "Config error: %v\n", err)
		os.Exit(1)
	}
	logger.Infow("config loaded", "tasks", len(cfg.Tasks), "version", cfg.Version)

	// Open bbolt state store (default to runtime.stateDbPath or temp if empty)
	statePath := cfg.Runtime.StateDbPath
	if statePath == "" {
		statePath = "/var/lib/cronplus/state.db"
	}
	store, err := task.OpenBBolt(statePath)
	if err != nil {
		logger.Errorw("failed to open state store", "path", statePath, "error", err)
		fmt.Fprintf(os.Stderr, "State store error: %v\n", err)
		os.Exit(1)
	}
	defer store.Close()

	// Task manager
	manager := task.NewManager(logger, store, cfg.Runtime.MaxConcurrentPerTask)

	// Root context with graceful shutdown
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	// Apply config to start supervisors
	if err := manager.ApplyConfig(ctx, cfg); err != nil {
		logger.Errorw("failed to apply config", "error", err)
		fmt.Fprintf(os.Stderr, "Apply config error: %v\n", err)
		os.Exit(1)
	}
	logger.Infow("task supervisors started")

	// Control API
	ctrl := &controlPlane{logger: logger, manager: manager, cfgPath: *configPath, cfg: cfg, sugar: logger}
	apiSrv := api.New(logger, ctrl, *apiAddr)
	if err := apiSrv.Start(ctx); err != nil {
		logger.Errorw("failed to start api server", "addr", *apiAddr, "error", err)
		fmt.Fprintf(os.Stderr, "API error: %v\n", err)
		os.Exit(1)
	}

	// Wait for termination signal
	sigCh := make(chan os.Signal, 2)
	signal.Notify(sigCh, syscall.SIGINT, syscall.SIGTERM)
	sig := <-sigCh
	logger.Infow("signal received, shutting down", "signal", sig.String())

	// Graceful shutdown
	shCtx, shCancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer shCancel()
	_ = apiSrv.Shutdown(shCtx)

	// Cancel root; supervisors will drain and exit
	cancel()

	// Give some time for goroutines to finish
	<-shCtx.Done()
	if shCtx.Err() == context.DeadlineExceeded {
		logger.Errorw("graceful shutdown timed out")
	}

	logger.Infow("shutdown complete")
	_ = http.ErrServerClosed // keep net/http imported if needed in future
}
