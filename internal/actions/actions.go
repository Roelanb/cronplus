package actions

import (
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"io"
	"os"
	"path/filepath"
)

// CopyOptions controls copy behavior.
type CopyOptions struct {
	Destination    string
	Atomic         bool
	VerifyChecksum bool
}

// DeleteOptions controls deletion behavior.
type DeleteOptions struct {
	Secure bool // placeholder; secure deletion not implemented in this iteration
}

// Copy copies src file to destination directory, preserving filename.
// If Atomic is true, writes to a temporary file then renames.
// If VerifyChecksum is true, verifies SHA-256 checksum matches after copy.
func Copy(src string, opts CopyOptions) (destPath string, err error) {
	info, err := os.Lstat(src)
	if err != nil {
		return "", fmt.Errorf("lstat src: %w", err)
	}
	if !info.Mode().IsRegular() {
		return "", fmt.Errorf("source is not a regular file: %s", src)
	}

	if err := os.MkdirAll(opts.Destination, 0o755); err != nil {
		return "", fmt.Errorf("mkdir dest: %w", err)
	}

	base := filepath.Base(src)
	destPath = filepath.Join(opts.Destination, base)

	if opts.Atomic {
		tmp, err := os.CreateTemp(opts.Destination, "."+base+".tmp-*")
		if err != nil {
			return "", fmt.Errorf("create temp: %w", err)
		}
		tmpPath := tmp.Name()
		defer func() {
			_ = tmp.Close()
			// best-effort cleanup of temp on failure
			if err != nil {
				_ = os.Remove(tmpPath)
			}
		}()

		if err = copyFileContents(src, tmp); err != nil {
			return "", err
		}
		if err = tmp.Sync(); err != nil {
			return "", fmt.Errorf("sync temp: %w", err)
		}
		if err = tmp.Close(); err != nil {
			return "", fmt.Errorf("close temp: %w", err)
		}

		// rename into place atomically
		if err = os.Rename(tmpPath, destPath); err != nil {
			return "", fmt.Errorf("rename temp: %w", err)
		}
	} else {
		// direct copy
		df, err := os.Create(destPath)
		if err != nil {
			return "", fmt.Errorf("create dest: %w", err)
		}
		defer func() {
			if cerr := df.Close(); cerr != nil && err == nil {
				err = cerr
			}
			if err != nil {
				_ = os.Remove(destPath)
			}
		}()
		if err = copyFileContents(src, df); err != nil {
			return "", err
		}
		if err = df.Sync(); err != nil {
			return "", fmt.Errorf("sync dest: %w", err)
		}
	}

	if opts.VerifyChecksum {
		srcSum, err := fileSHA256(src)
		if err != nil {
			return "", fmt.Errorf("src checksum: %w", err)
		}
		dstSum, err := fileSHA256(destPath)
		if err != nil {
			return "", fmt.Errorf("dest checksum: %w", err)
		}
		if srcSum != dstSum {
			return "", fmt.Errorf("checksum mismatch: %s != %s", srcSum, dstSum)
		}
	}

	return destPath, nil
}

// Delete deletes the given file. If Secure is true, a placeholder exists for future secure deletion.
func Delete(path string, opts DeleteOptions) error {
	// basic safety: ensure file exists and is regular
	info, err := os.Lstat(path)
	if err != nil {
		if os.IsNotExist(err) {
			return nil // already gone
		}
		return fmt.Errorf("lstat: %w", err)
	}
	if !info.Mode().IsRegular() {
		return fmt.Errorf("not a regular file: %s", path)
	}

	// TODO: implement secure deletion if required.
	return os.Remove(path)
}

func copyFileContents(src string, dst *os.File) error {
	sf, err := os.Open(src)
	if err != nil {
		return fmt.Errorf("open src: %w", err)
	}
	defer sf.Close()

	if _, err := io.Copy(dst, sf); err != nil {
		return fmt.Errorf("copy: %w", err)
	}
	return nil
}

func fileSHA256(path string) (string, error) {
	f, err := os.Open(path)
	if err != nil {
		return "", fmt.Errorf("open: %w", err)
	}
	defer f.Close()
	h := sha256.New()
	if _, err := io.Copy(h, f); err != nil {
		return "", fmt.Errorf("hash: %w", err)
	}
	return hex.EncodeToString(h.Sum(nil)), nil
}
