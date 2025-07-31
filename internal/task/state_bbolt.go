package task

import (
	"crypto/sha256"
	"encoding/binary"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"time"

	bolt "go.etcd.io/bbolt"
)

var (
	filesBucket = []byte("files")
	metaBucket  = []byte("meta")
)

type FileStatus string

const (
	StatusQueued     FileStatus = "queued"
	StatusProcessing FileStatus = "processing"
	StatusDone       FileStatus = "done"
	StatusFailed     FileStatus = "failed"
)

type FileRecord struct {
	TaskID        string     `json:"task_id"`
	Path          string     `json:"path"`
	Checksum      string     `json:"checksum,omitempty"`
	Status        FileStatus `json:"status"`
	Attempts      int        `json:"attempts"`
	LastError     string     `json:"last_error,omitempty"`
	UpdatedAt     time.Time  `json:"updated_at"`
	CreatedAt     time.Time  `json:"created_at"`
	CorrelationID string     `json:"correlation_id,omitempty"`
}

type StateStore interface {
	Close() error
	Put(rec *FileRecord) error
	Get(taskID, path, checksum string) (*FileRecord, error)
	Mark(taskID, path, checksum string, status FileStatus, attempts int, lastErr string) error
}

type BBoltStore struct {
	db *bolt.DB
}

func OpenBBolt(path string) (*BBoltStore, error) {
	if path == "" {
		return nil, errors.New("bbolt path is empty")
	}
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return nil, fmt.Errorf("mkdir state dir: %w", err)
	}
	db, err := bolt.Open(path, 0o600, &bolt.Options{Timeout: 2 * time.Second})
	if err != nil {
		return nil, fmt.Errorf("open bbolt: %w", err)
	}
	if err := db.Update(func(tx *bolt.Tx) error {
		if _, e := tx.CreateBucketIfNotExists(filesBucket); e != nil {
			return e
		}
		if _, e := tx.CreateBucketIfNotExists(metaBucket); e != nil {
			return e
		}
		return nil
	}); err != nil {
		_ = db.Close()
		return nil, err
	}
	return &BBoltStore{db: db}, nil
}

func (s *BBoltStore) Close() error {
	return s.db.Close()
}

func key(taskID, path, checksum string) []byte {
	// deterministic key: sha256(taskID|0|path|0|checksum)
	h := sha256.New()
	h.Write([]byte(taskID))
	h.Write([]byte{0})
	h.Write([]byte(path))
	h.Write([]byte{0})
	h.Write([]byte(checksum))
	sum := h.Sum(nil)
	return sum
}

func (s *BBoltStore) Put(rec *FileRecord) error {
	rec.UpdatedAt = time.Now()
	if rec.CreatedAt.IsZero() {
		rec.CreatedAt = rec.UpdatedAt
	}
	b, err := json.Marshal(rec)
	if err != nil {
		return err
	}
	k := key(rec.TaskID, rec.Path, rec.Checksum)
	return s.db.Update(func(tx *bolt.Tx) error {
		return tx.Bucket(filesBucket).Put(k, b)
	})
}

func (s *BBoltStore) Get(taskID, path, checksum string) (*FileRecord, error) {
	var out *FileRecord
	k := key(taskID, path, checksum)
	err := s.db.View(func(tx *bolt.Tx) error {
		v := tx.Bucket(filesBucket).Get(k)
		if v == nil {
			return nil
		}
		var rec FileRecord
		if e := json.Unmarshal(v, &rec); e != nil {
			return e
		}
		out = &rec
		return nil
	})
	return out, err
}

func (s *BBoltStore) Mark(taskID, path, checksum string, status FileStatus, attempts int, lastErr string) error {
	k := key(taskID, path, checksum)
	return s.db.Update(func(tx *bolt.Tx) error {
		bkt := tx.Bucket(filesBucket)
		v := bkt.Get(k)
		if v == nil {
			// create if missing
			rec := FileRecord{
				TaskID:    taskID,
				Path:      path,
				Checksum:  checksum,
				Status:    status,
				Attempts:  attempts,
				LastError: lastErr,
			}
			return putJSON(bkt, k, rec)
		}
		var rec FileRecord
		if err := json.Unmarshal(v, &rec); err != nil {
			return err
		}
		rec.Status = status
		rec.Attempts = attempts
		rec.LastError = lastErr
		rec.UpdatedAt = time.Now()
		return putJSON(bkt, k, rec)
	})
}

func putJSON(b *bolt.Bucket, k []byte, v any) error {
	data, err := json.Marshal(v)
	if err != nil {
		return err
	}
	return b.Put(k, data)
}

// Helpers to store small meta values if needed later.
func putUint64(b *bolt.Bucket, key []byte, v uint64) error {
	var buf [8]byte
	binary.BigEndian.PutUint64(buf[:], v)
	return b.Put(key, buf[:])
}
