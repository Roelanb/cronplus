# CronPlus

CronPlus is a cross-platform, modern task automation and scheduling tool with a web-based UI and SurrealDB backend. It enables you to automate file operations (copy, move), printing, and more, triggered by file events, schedules, or intervals. All configurations and logs are managed in SurrealDB, and the UI is built with React 19, TypeScript, and TailwindCSS.

## Key Features
- **Modern Web UI**: Built with React 19, TypeScript, and TailwindCSS for a responsive experience.
- **SurrealDB Integration**: Stores all task configurations and execution logs in SurrealDB (namespace: `cronplus`, database: `cronplus`).
- **Flexible Scheduling**: Supports file triggers (e.g., file created), time-based, and interval-based execution.
- **Configurable Actions**: Tasks can Copy, Move, or Print files with full parameterization.
- **Task Logs**: View execution logs in the UI, with flexible column visibility (show/hide columns as needed).
- **CRUD Operations**: Create, update, and delete TaskConfig records directly from the UI.
- **Authentication & Namespaces**: Secure connection to SurrealDB with full namespace/database selection.

## TaskConfig Model
The TaskConfig model defines how tasks are triggered and executed. Example TypeScript interface:

```typescript
export interface TaskConfig {
  id: string;
  taskType: 'Copy' | 'Move' | 'Print';
  triggerType: 'FileCreated' | 'FileRenamed' | 'Time' | 'Interval';
  sourceFolder: string;
  destinationFolder?: string;
  printerName?: string;
  archiveDirectory?: string;
  time?: string; // e.g., '21:00'
  intervalMinutes?: number;
  createdAt?: string;
  updatedAt?: string;
}
```

## Example Configuration (JSON)
```json
[
  {
    "triggerType": "FileCreated",
    "taskType": "Copy",
    "sourceFolder": "/data/source",
    "destinationFolder": "/data/dest",
    "archiveDirectory": "/data/archive"
  },
  {
    "triggerType": "Time",
    "taskType": "Print",
    "sourceFolder": "/data/to-print",
    "printerName": "PRINTER1",
    "time": "21:00"
  }
]
```

## Usage
1. **Start SurrealDB**: Ensure SurrealDB is running with namespace/database `cronplus`/`cronplus`.
2. **Backend**: Start the CronPlus backend service (see `/cronplusservice`).
3. **Frontend**: Run the React UI (`/cronplusui`).
4. **Credentials**: Use root/root for development (configurable).
5. **Access the UI**: Open the web interface to manage tasks and view logs.

## UI Features
- **Tasks Page**: List, create, update, and delete TaskConfig records.
- **Task Logs Page**: View execution logs with pagination, sorting, and customizable columns.
- **Column Visibility**: Show/hide columns in the logs table for a personalized view.

## Development Notes
- **Tech Stack**: React 19, TypeScript, TailwindCSS, SurrealDB
- **Known Issues**: Some dependencies (e.g., lucide-react) may not fully support React 19. Use compatible versions or alternatives as needed.

---
For more details, see the source code and `/instructions.md`.
