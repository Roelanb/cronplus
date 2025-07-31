package watch

import (
	"os"
)

// fileModeLike is implemented here to wrap os.FileMode.
type fileModeLike interface {
	IsRegular() bool
}

type fileModeAdapter os.FileMode

func (m fileModeAdapter) IsRegular() bool { return os.FileMode(m).IsRegular() }

// fileInfoLike wraps os.FileInfo to expose Mode() as fileModeLike.
type fileInfoLike interface {
	Mode() fileModeLike
	Size() int64
}

// fileInfoAdapter adapts os.FileInfo to fileInfoLike.
type fileInfoAdapter struct{ os.FileInfo }

func (f fileInfoAdapter) Mode() fileModeLike { return fileModeAdapter(f.FileInfo.Mode()) }

// lstat provides os.Lstat info adapted to fileInfoLike.
func lstat(path string) (fileInfoLike, error) {
	fi, err := os.Lstat(path)
	if err != nil {
		return nil, err
	}
	return fileInfoAdapter{FileInfo: fi}, nil
}
