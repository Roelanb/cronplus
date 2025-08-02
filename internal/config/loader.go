package config

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"

	"go.uber.org/zap"
)

func Load(path string, logger *zap.SugaredLogger) (*Config, error) {
	if path == "" {
		return nil, errors.New("config path is empty")
	}
	b, err := os.ReadFile(path)
	if err != nil {
		return nil, fmt.Errorf("read config: %w", err)
	}

	var cfg Config
	if err := json.Unmarshal(b, &cfg); err != nil {
		return nil, fmt.Errorf("parse config: %w", err)
	}

	applyDefaults(&cfg)

	// Validate config; if errors are only in tasks, disable invalid tasks and warn.
	// If there are global errors (version/runtime), still return error.
	if err := validateLenient(&cfg, logger); err != nil {
		return nil, err
	}
	return &cfg, nil
}

// Parse parses a raw JSON config into Config, applies defaults and validates.
func Parse(raw []byte, logger *zap.SugaredLogger) (*Config, error) {
	var cfg Config
	if err := json.Unmarshal(raw, &cfg); err != nil {
		return nil, fmt.Errorf("parse config: %w", err)
	}
	applyDefaults(&cfg)
	if err := validateLenient(&cfg, logger); err != nil {
		return nil, err
	}
	return &cfg, nil
}

// Save writes the provided config to disk at the given path (pretty-printed JSON).
func Save(path string, cfg *Config) error {
	if path == "" {
		return errors.New("save config: path is empty")
	}
	b, err := json.MarshalIndent(cfg, "", "  ")
	if err != nil {
		return fmt.Errorf("marshal config: %w", err)
	}
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return fmt.Errorf("mkdir config dir: %w", err)
	}
	if err := os.WriteFile(path, b, 0o644); err != nil {
		return fmt.Errorf("write config: %w", err)
	}
	return nil
}

func applyDefaults(cfg *Config) {
	// Global defaults
	if cfg.Version == 0 {
		cfg.Version = 1
	}
	if cfg.Logging.Level == "" {
		cfg.Logging.Level = "info"
	}
	// Metrics defaults
	if cfg.Metrics.EnablePrometheus && cfg.Metrics.Listen == "" {
		cfg.Metrics.Listen = "127.0.0.1:9090"
	}
	// Runtime defaults
	if cfg.Runtime.MaxConcurrentPerTask <= 0 {
		cfg.Runtime.MaxConcurrentPerTask = 2
	}
	if cfg.Runtime.DeadLetterDir == "" {
		cfg.Runtime.DeadLetterDir = "/var/lib/cronplus/dead"
	}

	// Per-task defaults
	for ti := range cfg.Tasks {
		t := &cfg.Tasks[ti]
		// Watch defaults
		if t.Watch.DebounceMs < 0 {
			t.Watch.DebounceMs = 0
		}
		if t.Watch.StabilizationMs < 0 {
			t.Watch.StabilizationMs = 0
		}
		// Variables: no defaults besides trimming spaces
		if len(t.Variables) > 0 {
			for vi := range t.Variables {
				t.Variables[vi].Name = strings.TrimSpace(t.Variables[vi].Name)
				t.Variables[vi].Type = strings.TrimSpace(t.Variables[vi].Type)
			}
		}

		// Pipeline step defaults
		for pi := range t.Pipeline {
			step := &t.Pipeline[pi]
			switch step.Type {
			case "":
				// Backward/alternative JSON shape support: allow step details at top-level with no explicit "type"
				// Infer step type by populated sub-struct.
				if step.Print != nil {
					step.Type = "print"
				} else if step.Archive != nil {
					step.Type = "archive"
				} else if step.Copy != nil {
					step.Type = "copy"
				} else if step.Delete != nil {
					step.Type = "delete"
				}
			case "print":
				if step.Print != nil {
					if step.Print.Copies <= 0 {
						step.Print.Copies = 1
					}
					if step.Print.TimeoutSec <= 0 {
						step.Print.TimeoutSec = 60
					}
					if step.Print.Retry != nil {
						if step.Print.Retry.Max < 0 {
							step.Print.Retry.Max = 0
						}
						if step.Print.Retry.BackoffMs <= 0 {
							step.Print.Retry.BackoffMs = 1000
						}
					}
				}
			case "archive":
				if step.Archive != nil {
					if step.Archive.ConflictStrategy == "" {
						step.Archive.ConflictStrategy = "rename"
					}
				}
			case "copy":
				if step.Copy != nil {
					// reasonable defaults
					// atomic true by default
					// checksum verification optional default false
					// retry defaults
					if step.Copy.Retry != nil {
						if step.Copy.Retry.Max < 0 {
							step.Copy.Retry.Max = 0
						}
						if step.Copy.Retry.BackoffMs <= 0 {
							step.Copy.Retry.BackoffMs = 1000
						}
					}
				}
			case "delete":
				// no defaults needed currently
			}
		}
	}
}

func Validate(cfg *Config) error {
	if cfg.Version <= 0 {
		return errors.New("version must be > 0")
	}
	if len(cfg.Tasks) == 0 {
		return errors.New("at least one task must be defined")
	}
	ids := map[string]struct{}{}
	for i, t := range cfg.Tasks {
		if t.ID == "" {
			return fmt.Errorf("tasks[%d]: id is required", i)
		}
		if _, ok := ids[t.ID]; ok {
			return fmt.Errorf("tasks[%d]: duplicate id %q", i, t.ID)
		}
		ids[t.ID] = struct{}{}
		if t.Watch.Directory == "" {
			return fmt.Errorf("tasks[%s]: watch.directory is required", t.ID)
		}
		if !filepath.IsAbs(t.Watch.Directory) {
			return fmt.Errorf("tasks[%s]: watch.directory must be absolute", t.ID)
		}
		if t.Watch.DebounceMs < 0 {
			return fmt.Errorf("tasks[%s]: watch.debounceMs must be >= 0", t.ID)
		}
		if t.Watch.StabilizationMs < 0 {
			return fmt.Errorf("tasks[%s]: watch.stabilizationMs must be >= 0", t.ID)
		}
		if len(t.Pipeline) == 0 {
			return fmt.Errorf("tasks[%s]: pipeline must not be empty", t.ID)
		}
		for j, step := range t.Pipeline {
			// Allow omission of "type" if the nested object is present
			if step.Type == "" {
				if step.Print != nil {
					step.Type = "print"
				} else if step.Archive != nil {
					step.Type = "archive"
				} else if step.Copy != nil {
					step.Type = "copy"
				} else if step.Delete != nil {
					step.Type = "delete"
				}
			}
			switch step.Type {
			case "print":
				if step.Print == nil {
					return fmt.Errorf("tasks[%s].pipeline[%d]: print step missing details", t.ID, j)
				}
				if step.Print.PrinterName == "" {
					return fmt.Errorf("tasks[%s].pipeline[%d]: printerName required", t.ID, j)
				}
				if step.Print.Copies <= 0 {
					return fmt.Errorf("tasks[%s].pipeline[%d]: copies must be > 0", t.ID, j)
				}
				if step.Print.TimeoutSec <= 0 {
					return fmt.Errorf("tasks[%s].pipeline[%d]: timeoutSec must be > 0", t.ID, j)
				}
			case "archive":
				if step.Archive == nil {
					return fmt.Errorf("tasks[%s].pipeline[%d]: archive step missing details", t.ID, j)
				}
				if step.Archive.Destination == "" {
					return fmt.Errorf("tasks[%s].pipeline[%d]: archive.destination required", t.ID, j)
				}
				if !filepath.IsAbs(step.Archive.Destination) {
					return fmt.Errorf("tasks[%s].pipeline[%d]: archive.destination must be absolute", t.ID, j)
				}
				switch step.Archive.ConflictStrategy {
				case "rename", "overwrite", "skip":
				default:
					return fmt.Errorf("tasks[%s].pipeline[%d]: archive.conflictStrategy invalid", t.ID, j)
				}
			case "copy":
				if step.Copy == nil {
					return fmt.Errorf("tasks[%s].pipeline[%d]: copy step missing details", t.ID, j)
				}
				if step.Copy.Destination == "" {
					return fmt.Errorf("tasks[%s].pipeline[%d]: copy.destination required", t.ID, j)
				}
				if !filepath.IsAbs(step.Copy.Destination) {
					return fmt.Errorf("tasks[%s].pipeline[%d]: copy.destination must be absolute", t.ID, j)
				}
			case "delete":
				// no additional required fields
			default:
				return fmt.Errorf("tasks[%s].pipeline[%d]: unsupported type %q", t.ID, j, step.Type)
			}
		}
	}

	// Runtime checks
	if cfg.Runtime.MaxConcurrentPerTask <= 0 {
		return errors.New("runtime.maxConcurrentPerTask must be >= 1")
	}
	if cfg.Runtime.StateDbPath != "" && !filepath.IsAbs(cfg.Runtime.StateDbPath) {
		return errors.New("runtime.stateDbPath must be absolute if set")
	}
	if cfg.Runtime.DeadLetterDir != "" && !filepath.IsAbs(cfg.Runtime.DeadLetterDir) {
		return errors.New("runtime.deadLetterDir must be absolute if set")
	}
	return nil
}

// validateLenient validates global config strictly, but handles per-task validation leniently:
// - If a task is invalid, it is disabled (Enabled=false) and a warning is logged.
// - The application still starts as long as global config is valid.
// - If all tasks end up disabled/invalid, we still allow startup but log a warning.
func validateLenient(cfg *Config, logger *zap.SugaredLogger) error {
	// Basic global checks aligned with Validate but without per-task strict errors.
	if cfg.Version <= 0 {
		return errors.New("version must be > 0")
	}
	if len(cfg.Tasks) == 0 {
		// Keep strict here: completely empty config is likely misconfiguration.
		return errors.New("at least one task must be defined")
	}
	// Runtime checks (strict)
	if cfg.Runtime.MaxConcurrentPerTask <= 0 {
		return errors.New("runtime.maxConcurrentPerTask must be >= 1")
	}
	if cfg.Runtime.StateDbPath != "" && !filepath.IsAbs(cfg.Runtime.StateDbPath) {
		return errors.New("runtime.stateDbPath must be absolute if set")
	}
	if cfg.Runtime.DeadLetterDir != "" && !filepath.IsAbs(cfg.Runtime.DeadLetterDir) {
		return errors.New("runtime.deadLetterDir must be absolute if set")
	}

	ids := map[string]struct{}{}
	for i := range cfg.Tasks {
		t := &cfg.Tasks[i]

		// Track if we encountered any validation error for this task.
		var taskErr error

		// ID required and unique
		if t.ID == "" {
			taskErr = fmt.Errorf("tasks[%d]: id is required", i)
		} else {
			if _, ok := ids[t.ID]; ok {
				taskErr = fmt.Errorf("tasks[%d]: duplicate id %q", i, t.ID)
			} else {
				ids[t.ID] = struct{}{}
			}
		}

		// Watch validations
		if taskErr == nil {
			if t.Watch.Directory == "" {
				taskErr = fmt.Errorf("tasks[%s]: watch.directory is required", t.ID)
			} else if !filepath.IsAbs(t.Watch.Directory) {
				taskErr = fmt.Errorf("tasks[%s]: watch.directory must be absolute", t.ID)
			} else if t.Watch.DebounceMs < 0 {
				taskErr = fmt.Errorf("tasks[%s]: watch.debounceMs must be >= 0", t.ID)
			} else if t.Watch.StabilizationMs < 0 {
				taskErr = fmt.Errorf("tasks[%s]: watch.stabilizationMs must be >= 0", t.ID)
			}
		}

		// Variables validation (lenient): drop invalid variables, keep task running
		if taskErr == nil && len(t.Variables) > 0 {
			seen := map[string]struct{}{}
			valid := make([]Variable, 0, len(t.Variables))
			for _, v := range t.Variables {
				name := strings.TrimSpace(v.Name)
				typ := strings.TrimSpace(strings.ToLower(v.Type))
				val := strings.TrimSpace(v.Value)
				if name == "" {
					if logger != nil {
						logger.Warnw("Dropping invalid variable (empty name)", "taskID", t.ID)
					}
					continue
				}
				if _, dup := seen[name]; dup {
					if logger != nil {
						logger.Warnw("Dropping duplicate variable", "taskID", t.ID, "name", name)
					}
					continue
				}
				switch typ {
				case "string", "":
					// always ok
				case "int":
					if _, err := strconv.Atoi(val); err != nil {
						if logger != nil {
							logger.Warnw("Dropping invalid int variable", "taskID", t.ID, "name", name, "value", val, "error", err.Error())
						}
						continue
					}
				case "bool":
					if _, err := strconv.ParseBool(strings.ToLower(val)); err != nil {
						if logger != nil {
							logger.Warnw("Dropping invalid bool variable", "taskID", t.ID, "name", name, "value", val, "error", err.Error())
						}
						continue
					}
				case "date":
					// Accept ISO date
					if _, err := time.Parse("2006-01-02", val); err != nil {
						if logger != nil {
							logger.Warnw("Dropping invalid date variable (expect YYYY-MM-DD)", "taskID", t.ID, "name", name, "value", val, "error", err.Error())
						}
						continue
					}
				case "datetime":
					// Accept RFC3339 or common "YYYY-MM-DD HH:MM:SS"
					if _, err := time.Parse(time.RFC3339, val); err != nil {
						if _, err2 := time.Parse("2006-01-02 15:04:05", val); err2 != nil {
							if logger != nil {
								logger.Warnw("Dropping invalid datetime variable (expect RFC3339 or 'YYYY-MM-DD HH:MM:SS')", "taskID", t.ID, "name", name, "value", val, "error", err.Error())
							}
							continue
						}
					}
				default:
					if logger != nil {
						logger.Warnw("Dropping variable with unsupported type", "taskID", t.ID, "name", name, "type", typ)
					}
					continue
				}
				seen[name] = struct{}{}
				// Normalize accepted type (default to "string" if empty)
				if typ == "" {
					typ = "string"
				}
				valid = append(valid, Variable{
					Name:  name,
					Type:  typ,
					Value: val,
				})
			}
			t.Variables = valid
		}

		// Pipeline validations
		if taskErr == nil {
			if len(t.Pipeline) == 0 {
				taskErr = fmt.Errorf("tasks[%s]: pipeline must not be empty", t.ID)
			} else {
				for j := range t.Pipeline {
					step := &t.Pipeline[j]
					// Infer type if omitted
					if step.Type == "" {
						if step.Print != nil {
							step.Type = "print"
						} else if step.Archive != nil {
							step.Type = "archive"
						} else if step.Copy != nil {
							step.Type = "copy"
						} else if step.Delete != nil {
							step.Type = "delete"
						}
					}

					switch step.Type {
					case "print":
						if step.Print == nil {
							taskErr = fmt.Errorf("tasks[%s].pipeline[%d]: print step missing details", t.ID, j)
							break
						}
						if step.Print.PrinterName == "" {
							taskErr = fmt.Errorf("tasks[%s].pipeline[%d]: printerName required", t.ID, j)
							break
						}
						if step.Print.Copies <= 0 {
							taskErr = fmt.Errorf("tasks[%s].pipeline[%d]: copies must be > 0", t.ID, j)
							break
						}
						if step.Print.TimeoutSec <= 0 {
							taskErr = fmt.Errorf("tasks[%s].pipeline[%d]: timeoutSec must be > 0", t.ID, j)
							break
						}
					case "archive":
						if step.Archive == nil {
							taskErr = fmt.Errorf("tasks[%s].pipeline[%d]: archive step missing details", t.ID, j)
							break
						}
						if step.Archive.Destination == "" {
							taskErr = fmt.Errorf("tasks[%s].pipeline[%d]: archive.destination required", t.ID, j)
							break
						}
						if !filepath.IsAbs(step.Archive.Destination) {
							taskErr = fmt.Errorf("tasks[%s].pipeline[%d]: archive.destination must be absolute", t.ID, j)
							break
						}
						switch step.Archive.ConflictStrategy {
						case "rename", "overwrite", "skip":
						default:
							taskErr = fmt.Errorf("tasks[%s].pipeline[%d]: archive.conflictStrategy invalid", t.ID, j)
						}
					case "copy":
						if step.Copy == nil {
							taskErr = fmt.Errorf("tasks[%s].pipeline[%d]: copy step missing details", t.ID, j)
							break
						}
						if step.Copy.Destination == "" {
							taskErr = fmt.Errorf("tasks[%s].pipeline[%d]: copy.destination required", t.ID, j)
							break
						}
						if !filepath.IsAbs(step.Copy.Destination) {
							taskErr = fmt.Errorf("tasks[%s].pipeline[%d]: copy.destination must be absolute", t.ID, j)
							break
						}
					case "delete":
						// no additional required fields
					default:
						taskErr = fmt.Errorf("tasks[%s].pipeline[%d]: unsupported type %q", t.ID, j, step.Type)
					}
					if taskErr != nil {
						break
					}
				}
			}
		}

		if taskErr != nil {
			// Disable the task and log a warning, continue startup.
			if logger != nil {
				logger.Warnw("Disabling invalid task", "taskID", t.ID, "error", taskErr.Error())
			}
			t.Enabled = false
		}
	}

	// If all tasks are disabled, warn (but do not fail).
	allDisabled := true
	for i := range cfg.Tasks {
		if cfg.Tasks[i].Enabled {
			allDisabled = false
			break
		}
	}
	if allDisabled && logger != nil {
		logger.Warn("All tasks are disabled after validation; application will start without active tasks")
	}

	return nil
}
