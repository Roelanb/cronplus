package actions

import (
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"time"
)

type ConflictStrategy string

const (
	ConflictRename    ConflictStrategy = "rename"
	ConflictOverwrite ConflictStrategy = "overwrite"
	ConflictSkip      ConflictStrategy = "skip"
)

type ArchiveOptions struct {
	Destination     string
	PreserveSubdirs bool // reserved for future use
	Conflict        ConflictStrategy
}

// Archive moves a file into Destination. If os.Rename fails due to cross-device link,
// it falls back to copy then delete.
// Conflict strategies:
//   - rename: append timestamp/hash suffix to avoid clobbering
//   - overwrite: replace existing file
//   - skip: keep existing and return nil (no change)
func Archive(src string, opts ArchiveOptions) (finalDest string, err error) {
	if opts.Destination == "" {
		return "", fmt.Errorf("archive: destination is required")
	}
	if err := os.MkdirAll(opts.Destination, 0o755); err != nil {
		return "", fmt.Errorf("archive: mkdir dest: %w", err)
	}

	base := filepath.Base(src)
	destPath := filepath.Join(opts.Destination, base)

	resolveDest := func(path string) (string, error) {
		_, statErr := os.Lstat(path)
		switch {
		case statErr == nil:
			// exists
			switch opts.Conflict {
			case ConflictOverwrite:
				// ok, keep same target
				return path, nil
			case ConflictSkip:
				// do nothing
				return "", nil
			case ConflictRename, "":
				// generate unique name
				return uniqueName(path), nil
			default:
				return "", fmt.Errorf("archive: unknown conflict strategy %q", opts.Conflict)
			}
		case os.IsNotExist(statErr):
			return path, nil
		default:
			return "", fmt.Errorf("archive: stat dest: %w", statErr)
		}
	}

	target, err := resolveDest(destPath)
	if err != nil {
		return "", err
	}
	// Skip if requested
	if target == "" && opts.Conflict == ConflictSkip {
		return "", nil
	}

	// Attempt rename first (fast path, atomic on same filesystem)
	if err := os.Rename(src, target); err == nil {
		return target, nil
	}

	// Fallback: copy then delete
	if err := copyFile(src, target); err != nil {
		return "", fmt.Errorf("archive: copy fallback: %w", err)
	}
	if err := os.Remove(src); err != nil {
		return "", fmt.Errorf("archive: remove src after copy: %w", err)
	}
	return target, nil
}

func uniqueName(path string) string {
	dir := filepath.Dir(path)
	base := filepath.Base(path)
	ext := filepath.Ext(base)
	name := base[:len(base)-len(ext)]
	h := sha256.New()
	io.WriteString(h, base)
	io.WriteString(h, time.Now().UTC().Format(time.RFC3339Nano))
	sum := hex.EncodeToString(h.Sum(nil))[:8]
	return filepath.Join(dir, fmt.Sprintf("%s-%s%s", name, sum, ext))
}

func copyFile(src, dst string) (err error) {
	sf, err := os.Open(src)
	if err != nil {
		return fmt.Errorf("open src: %w", err)
	}
	defer sf.Close()

	tmpDir := filepath.Dir(dst)
	if err := os.MkdirAll(tmpDir, 0o755); err != nil {
		return fmt.Errorf("mkdir dst dir: %w", err)
	}

	df, err := os.Create(dst)
	if err != nil {
		return fmt.Errorf("create dst: %w", err)
	}
	defer func() {
		if cerr := df.Close(); cerr != nil && err == nil {
			err = cerr
		}
		if err != nil {
			_ = os.Remove(dst)
		}
	}()

	if _, err := io.Copy(df, sf); err != nil {
		return fmt.Errorf("copy data: %w", err)
	}
	if err := df.Sync(); err != nil {
		return fmt.Errorf("sync dst: %w", err)
	}
	return nil
}
