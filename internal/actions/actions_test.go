package actions

import (
	"os"
	"path/filepath"
	"testing"
)

func TestCopyAndDelete(t *testing.T) {
	tmp := t.TempDir()
	srcDir := filepath.Join(tmp, "src")
	dstDir := filepath.Join(tmp, "dst")
	if err := os.MkdirAll(srcDir, 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.MkdirAll(dstDir, 0o755); err != nil {
		t.Fatal(err)
	}
	src := filepath.Join(srcDir, "a.txt")
	if err := os.WriteFile(src, []byte("hello"), 0o644); err != nil {
		t.Fatal(err)
	}

	// Copy with atomic + checksum
	_, err := Copy(src, CopyOptions{
		Destination:    dstDir,
		Atomic:         true,
		VerifyChecksum: true,
	})
	if err != nil {
		t.Fatalf("copy failed: %v", err)
	}
	dst := filepath.Join(dstDir, "a.txt")
	if _, err := os.Stat(dst); err != nil {
		t.Fatalf("dest missing: %v", err)
	}

	// Delete original
	if err := Delete(src, DeleteOptions{}); err != nil {
		t.Fatalf("delete failed: %v", err)
	}
	if _, err := os.Stat(src); !os.IsNotExist(err) {
		t.Fatalf("source not deleted, err=%v", err)
	}
}

func TestArchive_ConflictRename(t *testing.T) {
	tmp := t.TempDir()
	srcDir := filepath.Join(tmp, "src")
	dstDir := filepath.Join(tmp, "dst")
	if err := os.MkdirAll(srcDir, 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.MkdirAll(dstDir, 0o755); err != nil {
		t.Fatal(err)
	}
	// create two source files with same name sequentially to trigger rename
	src1 := filepath.Join(srcDir, "doc.pdf")
	if err := os.WriteFile(src1, []byte("v1"), 0o644); err != nil {
		t.Fatal(err)
	}
	// archive first one
	p1, err := Archive(src1, ArchiveOptions{
		Destination: dstDir,
		Conflict:    ConflictRename,
	})
	if err != nil {
		t.Fatalf("archive 1 failed: %v", err)
	}
	if _, err := os.Stat(p1); err != nil {
		t.Fatalf("archived file missing: %v", err)
	}

	// second same-name source
	src2 := filepath.Join(srcDir, "doc.pdf")
	if err := os.WriteFile(src2, []byte("v2"), 0o644); err != nil {
		t.Fatal(err)
	}
	p2, err := Archive(src2, ArchiveOptions{
		Destination: dstDir,
		Conflict:    ConflictRename,
	})
	if err != nil {
		t.Fatalf("archive 2 failed: %v", err)
	}
	if p1 == p2 {
		t.Fatalf("expected different archived names, got same: %s", p1)
	}
}

func TestArchive_ConflictOverwrite(t *testing.T) {
	tmp := t.TempDir()
	srcDir := filepath.Join(tmp, "src")
	dstDir := filepath.Join(tmp, "dst")
	if err := os.MkdirAll(srcDir, 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.MkdirAll(dstDir, 0o755); err != nil {
		t.Fatal(err)
	}
	// prepare destination existing file
	existing := filepath.Join(dstDir, "doc.pdf")
	if err := os.WriteFile(existing, []byte("old"), 0o644); err != nil {
		t.Fatal(err)
	}
	// archive with overwrite
	src := filepath.Join(srcDir, "doc.pdf")
	if err := os.WriteFile(src, []byte("new"), 0o644); err != nil {
		t.Fatal(err)
	}
	if _, err := Archive(src, ArchiveOptions{
		Destination: dstDir,
		Conflict:    ConflictOverwrite,
	}); err != nil {
		t.Fatalf("archive overwrite failed: %v", err)
	}
	// destination should contain "new"
	got, err := os.ReadFile(existing)
	if err != nil {
		t.Fatal(err)
	}
	if string(got) != "new" {
		t.Fatalf("expected overwritten content 'new', got %q", string(got))
	}
}
