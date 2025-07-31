package config

type RetryPolicy struct {
	Max       int `json:"max"`
	BackoffMs int `json:"backoffMs"`
}

type WatchSpec struct {
	Directory       string `json:"directory"`
	Glob            string `json:"glob"`
	DebounceMs      int    `json:"debounceMs"`
	StabilizationMs int    `json:"stabilizationMs"`
}

type PrintStep struct {
	PrinterName string       `json:"printerName"`
	Copies      int          `json:"copies"`
	Duplex      bool         `json:"duplex"`
	TimeoutSec  int          `json:"timeoutSec"`
	Retry       *RetryPolicy `json:"retry,omitempty"`
}

type ArchiveStep struct {
	Destination      string `json:"destination"`
	PreserveSubdirs  bool   `json:"preserveSubdirs"`
	ConflictStrategy string `json:"conflictStrategy"` // rename|overwrite|skip
}

type CopyStep struct {
	Destination    string       `json:"destination"`
	Atomic         bool         `json:"atomic"`
	VerifyChecksum bool         `json:"verifyChecksum"`
	Retry          *RetryPolicy `json:"retry,omitempty"`
}

type DeleteStep struct {
	Secure bool `json:"secure"`
}

type PipelineStep struct {
	Type    string       `json:"type"` // print|archive|copy|delete
	Print   *PrintStep   `json:"print,omitempty"`
	Archive *ArchiveStep `json:"archive,omitempty"`
	Copy    *CopyStep    `json:"copy,omitempty"`
	Delete  *DeleteStep  `json:"delete,omitempty"`
}

type Task struct {
	ID       string         `json:"id"`
	Enabled  bool           `json:"enabled"`
	Watch    WatchSpec      `json:"watch"`
	Pipeline []PipelineStep `json:"pipeline"`
}

type LoggingCfg struct {
	Level string `json:"level"`
}

type MetricsCfg struct {
	EnablePrometheus bool   `json:"enablePrometheus"`
	Listen           string `json:"listen"`
}

type RuntimeCfg struct {
	MaxConcurrentPerTask int    `json:"maxConcurrentPerTask"`
	StateDbPath          string `json:"stateDbPath"`
	DeadLetterDir        string `json:"deadLetterDir"`
}

type Config struct {
	Version int        `json:"version"`
	Tasks   []Task     `json:"tasks"`
	Logging LoggingCfg `json:"logging"`
	Metrics MetricsCfg `json:"metrics"`
	Runtime RuntimeCfg `json:"runtime"`
}
