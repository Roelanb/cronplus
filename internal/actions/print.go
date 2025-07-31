package actions

import (
	"context"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"time"
)

type PrintOptions struct {
	Printer string            // required
	Options map[string]string // optional: key=value passed as -o key=value
	Timeout time.Duration     // default 30s if zero
	// Backend is fixed to "lp" per project decision; reserved for future.
}

// Print sends the given file to a CUPS printer using `lp`.
// It returns an error if lp is not available, the file does not exist,
// or the command fails within the timeout.
func Print(ctx context.Context, filePath string, opts PrintOptions) error {
	if opts.Printer == "" {
		return errors.New("print: printer is required")
	}
	abs := filePath
	if !filepath.IsAbs(abs) {
		if a, err := filepath.Abs(filePath); err == nil {
			abs = a
		}
	}
	if stat, err := os.Stat(abs); err != nil || !stat.Mode().IsRegular() {
		return fmt.Errorf("print: file invalid: %s", filePath)
	}

	timeout := opts.Timeout
	if timeout <= 0 {
		timeout = 30 * time.Second
	}
	ctx, cancel := context.WithTimeout(ctx, timeout)
	defer cancel()

	args := []string{"-d", opts.Printer}
	for k, v := range opts.Options {
		if v == "" {
			args = append(args, "-o", k)
		} else {
			args = append(args, "-o", fmt.Sprintf("%s=%s", k, v))
		}
	}
	args = append(args, abs)

	cmd := exec.CommandContext(ctx, "lp", args...)
	// Inherit environment; capture combined output for diagnostics.
	out, err := cmd.CombinedOutput()
	if ctx.Err() == context.DeadlineExceeded {
		return fmt.Errorf("print: timeout after %s: %w; output=%s", timeout, ctx.Err(), string(out))
	}
	if err != nil {
		return fmt.Errorf("print: lp failed: %w; output=%s", err, string(out))
	}
	return nil
}
