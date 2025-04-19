# CronPlus

CronPlus is a file monitoring and automated printing utility that watches specified directories for new or renamed files and performs configured actions like printing or archiving.

## Features

- **File System Monitoring**: Watches specified directories for file creation and rename events
- **Cross-Platform PDF Printing**:
  - Windows: Uses PDFsharp for direct PDF printing without requiring a PDF reader
  - Linux: Uses lpr command for direct PDF printing through CUPS
- **Automatic Monthly Archiving**: 
  - Automatically organizes processed files into monthly folders
  - Creates folders in format `YYYY-MM` (e.g., "2025-03")
  - Handles filename conflicts by adding timestamps
  - Example: If "report.pdf" exists, saves as "report_20250308_013734.pdf"
- **Time-Based Triggers**:
  - Schedule tasks to run at specific times
  - Support for interval-based execution
  - Time format: "HH:mm" (24-hour format)
  - Interval in minutes
- **Configurable Actions**: Supports multiple task types:
  - Print: Send files directly to specified printers
  - Copy: Copy files to specified destinations
  - Move: Move files to specified locations
- **Error Handling & Logging**:
  - Validates configuration file existence and format
  - Verifies directory and file paths before operations
  - Checks printer availability before printing
  - Provides detailed console output for all operations
  - Reports errors with specific messages for troubleshooting

## Configuration

The program uses SurrealDB to store configuration. Example configuration:

```json
[
  {
    "triggerType": "fileCreated",
    "directory": "C:\\NetData",
    "taskType": "print",
    "printerName": "PRINTER1",
    "archiveDirectory": "C:\\NetData\\Archive"
  },
  {
    "triggerType": "time",
    "time": "21:00",
    "taskType": "print",
    "sourceFile": "C:\\Reports\\daily.pdf",
    "printerName": "PRINTER1"
  },
  {
    "triggerType": "interval",
    "interval": 60,
    "taskType": "copy",
    "sourceFile": "C:\\Data\\backup.txt",
    "destinationFile": "C:\\Backup\\backup.txt"
  }
]
```

To start SurrealDB:

```bash
surreal start --user root --pass root rocksdb:cronplusstorage.db
```

### Configuration Options

- `triggerType`: Type of event to monitor:
  - `fileCreated`: Trigger on new file creation
  - `fileRenamed`: Trigger on file rename
  - `time`: Trigger at specific time
  - `interval`: Trigger at regular intervals
- `directory`: Directory to monitor for file events (for file triggers)
- `taskType`: Action to perform (`print`, `copy`, `move`)
- `printerName`: Name of the printer for print tasks
- `archiveDirectory`: Directory for archiving processed files (will create monthly subdirectories automatically)
- `sourceFile`: Source file path for copy/move tasks or time-based print tasks
- `destinationFile`: Destination file path for copy/move tasks
- `time`: Time to execute task in "HH:mm" format (for time trigger)
- `interval`: Minutes between task executions (for interval trigger)

### Archive Structure

When using the archive feature, files are organized as follows:
```
archiveDirectory/
├── 2025-01/
│   ├── document1.pdf
│   └── document2_20250115_143022.pdf
├── 2025-02/
│   └── report.pdf
└── 2025-03/
    ├── invoice.pdf
    └── statement_20250308_013734.pdf
```

## Usage

```bash
cronplus <path-to-config.json>
```

Example:
```bash
cronplus ConfigWinTest.json
```

### Console Output

The program provides detailed console output for monitoring and troubleshooting:

```
CronPlus started. Press any key to exit.
Loading config from: ConfigWinTest.json
Watching directory: C:\NetData
File created: C:\NetData\document.pdf
PDF file C:\NetData\document.pdf sent to printer PRINTER1
Archiving file to: C:\NetData\Archive\2025-03\document.pdf
```

### Error Messages

Common error messages and their meanings:

- `Please provide the path to config.json as an argument`: No configuration file specified
- `Config file not found: <path>`: The specified configuration file doesn't exist
- `File not found: <path>`: The file to be processed doesn't exist
- `Printer <name> is not valid`: The specified printer is not available
- `Error printing PDF file: <details>`: Problem occurred during PDF printing
- `Error printing file on Linux: <details>`: Linux-specific printing error

## Requirements

- .NET 8.0 or later
- Windows or Linux operating system
- For Linux: CUPS printing system installed

## Dependencies

- PDFsharp (6.0.0) - For Windows PDF printing
- Newtonsoft.Json - For JSON configuration handling

## Troubleshooting

### Common Issues

1. **Program doesn't start**
   - Ensure .NET 8.0 is installed
   - Check that the config file path is correct
   - Verify the config file contains valid JSON

2. **File monitoring not working**
   - Verify the watch directory exists and is accessible
   - Check that the program has read permissions for the directory
   - Ensure the directory path in config matches exactly (case-sensitive on Linux)

3. **Printing issues**
   - Verify the printer name exactly matches the system printer name
   - For PDF printing on Windows, ensure PDFsharp is properly installed
   - For Linux, verify CUPS is installed and `lpr` command is available
   - Check that the printer is online and has paper

4. **Archiving problems**
   - Ensure the archive directory exists or can be created
   - Verify write permissions for the archive location
   - Check disk space availability

5. **Time-based triggers not firing**
   - Verify time format is correct (HH:mm, 24-hour format)
   - For interval triggers, ensure value is a positive number
   - Check that source files exist for scheduled tasks

### Platform-Specific Notes

#### Windows
- Printer names can be found in Windows Settings > Printers & Scanners
- Use double backslashes in JSON paths: `C:\\Directory\\File.pdf`
- Run the program with appropriate permissions if accessing system printers

#### Linux
- Printer names can be found using `lpstat -p -d`
- Use forward slashes in paths: `/home/user/directory/file.pdf`
- Ensure CUPS service is running: `systemctl status cups`

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request. For major changes, please open an issue first to discuss what you would like to change.

### Development Setup

1. Clone the repository
2. Install .NET 8.0 SDK
3. Open the solution in your preferred IDE
4. Build the project: `dotnet build`
5. Run tests: `dotnet test`

### Testing

When testing the application:
1. Create a test configuration file using the examples provided
2. Set up test directories for file monitoring
3. Configure a test printer
4. Test both PDF and non-PDF file printing
5. Verify archive folder creation and file organization

## License

This project is licensed under the MIT License - see below for details:

```
MIT License

Copyright (c) 2025 CronPlus Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
